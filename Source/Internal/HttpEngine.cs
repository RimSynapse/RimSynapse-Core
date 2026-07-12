using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Low-level HTTP client wrapper for LM Studio communication.
    /// All calls run on background threads — never blocks Unity's main thread.
    /// </summary>
    internal static class HttpEngine
    {
        private static HttpClient _client;
        private static readonly object _initLock = new object();

        /// <summary>
        /// Ensure the HttpClient is initialized with current settings.
        /// </summary>
        internal static void EnsureInitialized()
        {
            if (_client != null) return;

            lock (_initLock)
            {
                if (_client != null) return;

                var handler = new HttpClientHandler
                {
                    // Accept self-signed certs for local development
                    ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true,
                };

                _client = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };
                _client.DefaultRequestHeaders.Add("Accept", "application/json");

                SynapseLogger.Message("HttpClient initialized.");
            }
        }

        /// <summary>
        /// Dispose the HttpClient on shutdown.
        /// </summary>
        internal static void Shutdown()
        {
            _client?.Dispose();
            _client = null;
        }

        /// <summary>
        /// POST a chat completion request to LM Studio.
        /// SYNCHRONOUS — intended to be called from the queue worker thread.
        /// Returns the result directly (never dispatches to main thread).
        /// </summary>
        internal static ChatResult PostChatCompletionSync(
            SynapseModHandle mod,
            List<ChatMessage> messages,
            ChatOptions options)
        {
            EnsureInitialized();
            var settings = RimSynapseMod.Instance?.Settings;
            if (settings == null)
            {
                return ChatResult.Failure("RimSynapse settings not loaded.");
            }

            long startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                ProviderRouting routing = ProviderRouting.LocalOnly;
                string queryKey = $"{mod?.ModId}:{options?.queryId}";
                if (mod != null && !string.IsNullOrEmpty(options?.queryId) && settings.queryRouting.TryGetValue(queryKey, out var savedRouting))
                {
                    routing = savedRouting;
                }

                string baseUrl = settings.lmStudioUrl;
                string apiKey = settings.lmStudioApiKey;
                ApiProvider providerHit = ApiProvider.Local_LMStudio;

                if (routing == ProviderRouting.Specific_OpenAI)
                {
                    baseUrl = settings.openAiUrl;
                    apiKey = settings.openAiApiKey;
                    providerHit = ApiProvider.OpenAI;
                }
                else if (routing == ProviderRouting.Specific_Gemini)
                {
                    baseUrl = settings.geminiUrl;
                    apiKey = settings.geminiApiKey;
                    providerHit = ApiProvider.Google_Gemini;
                }
                else if (routing == ProviderRouting.Specific_Claude)
                {
                    baseUrl = settings.claudeUrl;
                    apiKey = settings.claudeApiKey;
                    providerHit = ApiProvider.Anthropic_Claude;
                }
                else if (routing == ProviderRouting.Specific_Custom)
                {
                    baseUrl = settings.customUrl;
                    apiKey = settings.customApiKey;
                    providerHit = ApiProvider.Custom;
                }

                baseUrl = baseUrl.TrimEnd('/');
                string url = $"{baseUrl}/v1/chat/completions";
                
                if (providerHit == ApiProvider.Google_Gemini && baseUrl.Contains("generativelanguage"))
                {
                    url = $"{baseUrl}/v1beta/openai/chat/completions";
                }

                // Build request body
                var body = new JObject
                {
                    ["messages"] = JArray.FromObject(messages),
                };

                // Model (null = let ModelManager resolve)
                string targetModel = options?.model;
                if (!string.IsNullOrEmpty(targetModel))
                {
                    body["model"] = targetModel;
                }

                // Optional parameters
                if (options?.maxTokens.HasValue == true)
                {
                    int tokens = options.maxTokens.Value;
                    // Bump low max_tokens for reasoning models
                    if (tokens < 8192)
                    {
                        SynapseLogger.Message($"Bumping max_tokens from {tokens} to 8192 for reasoning model headroom.");
                        tokens = 8192;
                    }
                    body["max_tokens"] = tokens;
                }

                if (options?.temperature.HasValue == true)
                {
                    body["temperature"] = options.temperature.Value;
                }

                // Thinking/reasoning control:
                // Per-request options.thinking overrides global setting.
                // null = use global, true = force on, false = force off.
                bool thinkingEnabled = options?.thinking
                    ?? !settings.disableThinking; // Global default: thinking OFF

                if (!thinkingEnabled)
                {
                    // LM Studio supports both formats for disabling thinking:
                    // 1. "thinking": { "type": "disabled" }  (newer LM Studio)
                    // 2. "reasoning_effort": "none"           (OpenAI-compat)
                    body["thinking"] = new JObject { ["type"] = "disabled" };

                    SynapseLogger.Message("Thinking disabled for this request.");
                }

                // LM Studio quirk: remove response_format.type=json_object to prevent 400
                body.Remove("response_format");

                string jsonBody = body.ToString(Formatting.None);
                
                SynapseLogger.TraceContext(jsonBody, url);

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                };

                // Auth header
                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue(
                            "Bearer", apiKey);
                }

                // Set timeout using CancellationToken because HttpClient.Timeout cannot be modified per-request
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.timeoutSeconds));
                var response = _client.SendAsync(request, cts.Token).Result;
                string responseBody = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                {
                    long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                    var error = $"LM Studio returned {(int)response.StatusCode}: {responseBody}";
                    SynapseLogger.Error(error);
                    return ChatResult.Failure(error, dur);
                }

                var result = JObject.Parse(responseBody);

                // Extract response content
                string content = null;
                var choices = result["choices"] as JArray;
                if (choices != null && choices.Count > 0)
                {
                    var message = choices[0]?["message"];
                    content = message?["content"]?.ToString();

                    // Reasoning content fallback: if content is empty but
                    // reasoning_content exists, use that instead
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        string reasoningContent = message?["reasoning_content"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(reasoningContent))
                        {
                            SynapseLogger.Warning("Assistant content was empty but reasoning_content present. Using fallback.");
                            content = reasoningContent;
                        }
                    }
                }

                // Sanitize if enabled
                if (options?.sanitize != false && settings.sanitizeResponse)
                {
                    content = Sanitizer.Clean(content);
                }

                // Extract usage
                var usage = result["usage"];
                int promptTokens = usage?["prompt_tokens"]?.Value<int>() ?? 0;
                int completionTokens = usage?["completion_tokens"]?.Value<int>() ?? 0;
                string model = result["model"]?.ToString() ?? targetModel ?? "unknown";

                long durationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;

                SynapseLogger.Message($"Completion received in {durationMs}ms — {promptTokens}p/{completionTokens}c tokens.");

                // Record usage per provider
                if (providerHit == ApiProvider.Local_LMStudio) { settings.tokensPromptLocal += promptTokens; settings.tokensCompletionLocal += completionTokens; }
                else if (providerHit == ApiProvider.OpenAI) { settings.tokensPromptOpenAi += promptTokens; settings.tokensCompletionOpenAi += completionTokens; }
                else if (providerHit == ApiProvider.Google_Gemini) { settings.tokensPromptGemini += promptTokens; settings.tokensCompletionGemini += completionTokens; }
                else if (providerHit == ApiProvider.Anthropic_Claude) { settings.tokensPromptClaude += promptTokens; settings.tokensCompletionClaude += completionTokens; }
                else if (providerHit == ApiProvider.Custom) { settings.tokensPromptCustom += promptTokens; settings.tokensCompletionCustom += completionTokens; }

                return ChatResult.Success(content, model, promptTokens, completionTokens, durationMs);
            }
            catch (Exception ex)
            {
                long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                string error = ex.InnerException?.Message ?? ex.Message;
                SynapseLogger.Error($"Request failed: {error}");
                return ChatResult.Failure(error, dur);
            }
        }

        /// <summary>
        /// GET the loaded models list from LM Studio.
        /// SYNCHRONOUS — caller handles threading.
        /// </summary>
        internal static ModelsResult GetModelsSync()
        {
            EnsureInitialized();
            var settings = RimSynapseMod.Instance?.Settings;
            if (settings == null)
            {
                return new ModelsResult { online = false, error = "Settings not loaded." };
            }

            try
            {
                string baseUrl = settings.lmStudioUrl;
                string apiKey = settings.lmStudioApiKey;

                if (settings.apiProvider == ApiProvider.OpenAI) { baseUrl = settings.openAiUrl; apiKey = settings.openAiApiKey; }
                else if (settings.apiProvider == ApiProvider.Google_Gemini) { baseUrl = settings.geminiUrl; apiKey = settings.geminiApiKey; }
                else if (settings.apiProvider == ApiProvider.Anthropic_Claude) { baseUrl = settings.claudeUrl; apiKey = settings.claudeApiKey; }
                else if (settings.apiProvider == ApiProvider.Custom) { baseUrl = settings.customUrl; apiKey = settings.customApiKey; }

                baseUrl = baseUrl.TrimEnd('/');
                string url = $"{baseUrl}/v1/models";
                if (settings.apiProvider == ApiProvider.Google_Gemini && baseUrl.Contains("generativelanguage"))
                {
                    url = $"{baseUrl}/v1beta/openai/models";
                }

                var result = new ModelsResult();

                // OpenAI-compatible endpoint
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue(
                            "Bearer", apiKey);
                }

                var response = _client.SendAsync(request).Result;
                if (!response.IsSuccessStatusCode)
                {
                    result.online = false;
                    result.error = $"HTTP {(int)response.StatusCode}";
                    return result;
                }

                string body = response.Content.ReadAsStringAsync().Result;
                var json = JObject.Parse(body);
                var data = json["data"] as JArray;

                result.online = true;
                if (data != null)
                {
                    foreach (var m in data)
                    {
                        string id = m["id"]?.ToString();
                        if (!string.IsNullOrEmpty(id))
                            result.modelIds.Add(id);
                    }
                }

                // ── Active Model Discovery ──
                // The /v1/models endpoint returns ALL downloaded models, which is useless for
                // knowing what's actually in VRAM. The native /api/v1/models endpoint is often
                // disabled or hanging in newer LM Studio versions.
                // The most reliable way to find the loaded model is to send a tiny dummy request.
                // LM Studio will process it with the active model and return its ID in the response.
                try
                {
                    var dummyReq = new HttpRequestMessage(
                        HttpMethod.Post, $"{baseUrl}/v1/chat/completions");
                    if (!string.IsNullOrEmpty(settings.lmStudioApiKey))
                    {
                        dummyReq.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue(
                                "Bearer", settings.lmStudioApiKey);
                    }

                    // A minimal request that takes almost 0 compute
                    string dummyPayload = "{\"model\":\"local-model\",\"messages\":[{\"role\":\"system\",\"content\":\"ping\"}],\"max_tokens\":1}";
                    dummyReq.Content = new StringContent(
                        dummyPayload,
                        System.Text.Encoding.UTF8,
                        "application/json");

                    var dummyRes = _client.SendAsync(dummyReq).Result;
                    if (dummyRes.IsSuccessStatusCode)
                    {
                        string dummyBody = dummyRes.Content.ReadAsStringAsync().Result;
                        var dummyJson = JObject.Parse(dummyBody);
                        string activeModelId = dummyJson["model"]?.ToString();
                        
                        if (!string.IsNullOrEmpty(activeModelId))
                        {
                            // Overwrite the full list with just the active model
                            result.modelIds = new List<string> { activeModelId };
                            SynapseLogger.Message($"Active model discovered via dummy request: {activeModelId}");
                        }
                    }
                }
                catch
                {
                    // Ignore failures and fall back to the full list from /v1/models
                }

                return result;
            }
            catch (Exception ex)
            {
                string error = ex.InnerException?.Message ?? ex.Message;
                return new ModelsResult { online = false, error = error };
            }
        }

        /// <summary>
        /// Send a minimal keep-alive ping to prevent model unloading.
        /// Fire-and-forget, errors silently ignored.
        /// </summary>
        internal static void SendKeepAlivePing(string model)
        {
            EnsureInitialized();
            var settings = RimSynapseMod.Instance?.Settings;
            if (settings == null) return;

            Task.Run(() =>
            {
                try
                {
                    string baseUrl = settings.lmStudioUrl.TrimEnd('/');
                    string url = $"{baseUrl}/v1/chat/completions";

                    var body = new JObject
                    {
                        ["model"] = model,
                        ["messages"] = new JArray
                        {
                            new JObject
                            {
                                ["role"] = "user",
                                ["content"] = "keep-alive ping",
                            }
                        },
                        ["max_tokens"] = 1,
                    };

                    var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(
                            body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
                    };

                    if (!string.IsNullOrEmpty(settings.lmStudioApiKey))
                    {
                        request.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue(
                                "Bearer", settings.lmStudioApiKey);
                    }

                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    _client.SendAsync(request, cts.Token).Wait(cts.Token);

                    SynapseLogger.Message($"Keep-alive ping sent to \"{model}\".");
                }
                catch
                {
                    // Best-effort — silently ignore
                }
            });
        }
    }
}
