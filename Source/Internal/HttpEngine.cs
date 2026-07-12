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
                string routingId = RoutingId.LocalOnly;
                string queryKey = $"{mod?.ModId}:{options?.queryId}";
                if (mod != null && !string.IsNullOrEmpty(options?.queryId) && settings.queryRoutingIds.TryGetValue(queryKey, out var savedRouting))
                {
                    routingId = savedRouting;
                }
                else
                {
                    LlmCapabilities reqCaps = LlmCapabilities.Text;
                    if (mod != null && !string.IsNullOrEmpty(options?.queryId) && mod.RegisteredQueries.TryGetValue(options.queryId, out var queryDef))
                    {
                        reqCaps = queryDef.requiredCaps;
                    }
                    
                    if ((reqCaps & LlmCapabilities.Image) == LlmCapabilities.Image) routingId = settings.defaultRoutingImage;
                    else if ((reqCaps & LlmCapabilities.Vision) == LlmCapabilities.Vision) routingId = settings.defaultRoutingVision;
                    else if ((reqCaps & LlmCapabilities.Audio) == LlmCapabilities.Audio) routingId = settings.defaultRoutingAudio;
                    else routingId = settings.defaultRoutingText;
                }

                string baseUrl = settings.lmStudioUrl;
                string apiKey = settings.lmStudioApiKey;
                ApiProvider providerHit = ApiProvider.Local_LMStudio;

                if (routingId == RoutingId.OpenAI)
                {
                    baseUrl = settings.openAiUrl;
                    apiKey = settings.openAiApiKey;
                    providerHit = ApiProvider.OpenAI;
                }
                else if (routingId == RoutingId.Gemini)
                {
                    baseUrl = settings.geminiUrl;
                    apiKey = settings.geminiApiKey;
                    providerHit = ApiProvider.Google_Gemini;
                }
                else if (routingId == RoutingId.Claude)
                {
                    baseUrl = settings.claudeUrl;
                    apiKey = settings.claudeApiKey;
                    providerHit = ApiProvider.Anthropic_Claude;
                }
                else if (routingId != null && routingId.StartsWith(RoutingId.CustomPrefix))
                {
                    string customId = routingId.Substring(RoutingId.CustomPrefix.Length);
                    var custom = settings.customProviders.Find(c => c.id == customId);
                    if (custom != null)
                    {
                        baseUrl = custom.url;
                        apiKey = custom.apiKey;
                        providerHit = ApiProvider.Custom;
                        if (string.IsNullOrEmpty(options?.model))
                        {
                            options = new ChatOptions {
                                queryId = options?.queryId,
                                maxTokens = options?.maxTokens,
                                temperature = options?.temperature,
                                model = custom.model, // inject the custom provider's model
                                thinking = options?.thinking,
                                sanitize = options?.sanitize ?? true,
                                priority = options?.priority ?? 0,
                                maxWaitMs = options?.maxWaitMs,
                                eventType = options?.eventType,
                                contextTiers = options?.contextTiers,
                                weightOverrides = options?.weightOverrides,
                                sourcePawn = options?.sourcePawn,
                                targetPawn = options?.targetPawn
                            };
                        }
                    }
                }

                baseUrl = baseUrl.TrimEnd('/');
                string url;
                if (providerHit == ApiProvider.Anthropic_Claude)
                {
                    if (baseUrl.EndsWith("/v1") || baseUrl.EndsWith("/v1/messages")) url = $"{baseUrl.Replace("/v1/messages", "/v1")}/messages";
                    else url = $"{baseUrl}/v1/messages";
                }
                else
                {
                    if (baseUrl.EndsWith("/v1") || baseUrl.EndsWith("/v1beta/openai") || baseUrl.EndsWith("/v1/messages")) url = $"{baseUrl.Replace("/v1/messages", "/v1")}/chat/completions";
                    else url = $"{baseUrl}/v1/chat/completions";
                }

                JObject body;
                string targetModel = options?.model;
                if (providerHit == ApiProvider.Anthropic_Claude)
                {
                    body = new JObject();
                    if (!string.IsNullOrEmpty(targetModel)) body["model"] = targetModel;
                    else body["model"] = "claude-opus-4-6"; // default fallback

                    // max_tokens is required by Anthropic
                    int tokens = options?.maxTokens ?? 8192;
                    body["max_tokens"] = tokens;

                    if (options?.temperature.HasValue == true)
                        body["temperature"] = options.temperature.Value;

                    string systemStr = "";
                    var anthropicMessages = new JArray();
                    foreach (var msg in messages)
                    {
                        if (msg.role == "system")
                        {
                            if (systemStr.Length > 0) systemStr += "\n";
                            systemStr += msg.content;
                        }
                        else
                        {
                            anthropicMessages.Add(new JObject { ["role"] = msg.role, ["content"] = msg.content });
                        }
                    }

                    if (!string.IsNullOrEmpty(systemStr))
                        body["system"] = systemStr;
                    body["messages"] = anthropicMessages;
                }
                else
                {
                    body = new JObject { ["messages"] = JArray.FromObject(messages) };
                    if (!string.IsNullOrEmpty(targetModel)) body["model"] = targetModel;

                    if (options?.maxTokens.HasValue == true)
                    {
                        int tokens = options.maxTokens.Value;
                        if (tokens < 8192)
                        {
                            SynapseLogger.Message($"Bumping max_tokens from {tokens} to 8192 for reasoning model headroom.");
                            tokens = 8192;
                        }
                        body["max_tokens"] = tokens;
                    }

                    if (options?.temperature.HasValue == true)
                        body["temperature"] = options.temperature.Value;

                    bool thinkingEnabled = options?.thinking ?? !settings.disableThinking;
                    if (!thinkingEnabled)
                    {
                        body["thinking"] = new JObject { ["type"] = "disabled" };
                        SynapseLogger.Message("Thinking disabled for this request.");
                    }
                    body.Remove("response_format");
                }

                string jsonBody = body.ToString(Formatting.None);
                
                SynapseLogger.TraceContext(jsonBody, url);

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                };

                // Auth header
                if (!string.IsNullOrEmpty(apiKey))
                {
                    if (providerHit == ApiProvider.Anthropic_Claude)
                    {
                        request.Headers.Add("x-api-key", apiKey);
                        request.Headers.Add("anthropic-version", "2023-06-01");
                    }
                    else
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                    }
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

                string content = null;
                int promptTokens = 0;
                int completionTokens = 0;
                string model = targetModel ?? "unknown";

                if (providerHit == ApiProvider.Anthropic_Claude)
                {
                    var contentArr = result["content"] as JArray;
                    if (contentArr != null && contentArr.Count > 0)
                    {
                        content = contentArr[0]?["text"]?.ToString();
                    }
                    var usage = result["usage"];
                    promptTokens = usage?["input_tokens"]?.Value<int>() ?? 0;
                    completionTokens = usage?["output_tokens"]?.Value<int>() ?? 0;
                    model = result["model"]?.ToString() ?? model;
                }
                else
                {
                    var choices = result["choices"] as JArray;
                    if (choices != null && choices.Count > 0)
                    {
                        var message = choices[0]?["message"];
                        content = message?["content"]?.ToString();

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
                    var usage = result["usage"];
                    promptTokens = usage?["prompt_tokens"]?.Value<int>() ?? 0;
                    completionTokens = usage?["completion_tokens"]?.Value<int>() ?? 0;
                    model = result["model"]?.ToString() ?? model;
                }

                // Sanitize if enabled
                if (options?.sanitize != false && settings.sanitizeResponse)
                {
                    content = Sanitizer.Clean(content);
                }

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
                string url;
                if (baseUrl.EndsWith("/v1") || baseUrl.EndsWith("/v1beta/openai") || baseUrl.EndsWith("/v1/messages"))
                {
                    url = $"{baseUrl}/models";
                }
                else
                {
                    url = $"{baseUrl}/v1/models";
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

        /// <summary>
        /// Test the API connection for a specific provider.
        /// Sends a minimal ping and returns the result asynchronously via callback on the main thread.
        /// </summary>
        public static void FetchProviderModelsAsync(ApiProvider provider, string baseUrl, string apiKey, Action<bool, System.Collections.Generic.List<string>, string> callback)
        {
            EnsureInitialized();
            Task.Run(() =>
            {
                try
                {
                    baseUrl = baseUrl.TrimEnd('/');
                    string url;
                    if (baseUrl.EndsWith("/v1") || baseUrl.EndsWith("/v1beta/openai") || baseUrl.EndsWith("/v1/messages"))
                    {
                        url = $"{baseUrl}/models";
                    }
                    else
                    {
                        url = $"{baseUrl}/v1/models";
                    }

                    var request = new HttpRequestMessage(HttpMethod.Get, url);

                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        if (provider == ApiProvider.Anthropic_Claude)
                        {
                            request.Headers.Add("x-api-key", apiKey);
                            request.Headers.Add("anthropic-version", "2023-06-01");
                        }
                        else
                        {
                            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                        }
                    }

                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var response = _client.SendAsync(request, cts.Token).Result;
                    string resBody = response.Content.ReadAsStringAsync().Result;

                    if (response.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(resBody);
                        var data = json["data"] as JArray;
                        var models = new System.Collections.Generic.List<string>();
                        if (data != null)
                        {
                            foreach (var m in data)
                            {
                                string id = m["id"]?.ToString();
                                if (!string.IsNullOrEmpty(id))
                                {
                                    if (id.StartsWith("models/")) id = id.Substring(7);
                                    models.Add(id);
                                }
                            }
                            models.Sort();
                        }
                        RimSynapse.SynapseGameComponent.Enqueue(() => callback(true, models, "Success"));
                    }
                    else
                    {
                        string shortError = $"{(int)response.StatusCode} {response.ReasonPhrase}";
                        try
                        {
                            var errJson = JObject.Parse(resBody);
                            if (errJson["error"] != null)
                            {
                                shortError += $": {errJson["error"]["message"]?.ToString()}";
                            }
                        }
                        catch { }
                        
                        string lowerError = shortError.ToLowerInvariant();
                        if (lowerError.Contains("credit balance is too low") || lowerError.Contains("insufficient_quota") || lowerError.Contains("exceeded your current quota"))
                        {
                            shortError = "Credits Needed!";
                        }
                        
                        RimSynapse.SynapseGameComponent.Enqueue(() => callback(false, null, shortError));
                    }
                }
                catch (Exception ex)
                {
                    string error = ex.InnerException?.Message ?? ex.Message;
                    RimSynapse.SynapseGameComponent.Enqueue(() => callback(false, null, error));
                }
            });
        }

        public static void TestConnectionAsync(ApiProvider provider, string url, string apiKey, string overrideModel, Action<bool, string> callback)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    EnsureInitialized();
                    // Strip any zero-width spaces or hidden unicode characters that users might accidentally paste
                    url = System.Text.RegularExpressions.Regex.Replace(url ?? "", @"[^\u0020-\u007E]+", string.Empty);
                    apiKey = System.Text.RegularExpressions.Regex.Replace(apiKey ?? "", @"[^\u0020-\u007E]+", string.Empty);

                    string baseUrl = url.Trim().TrimEnd('/');
                    
                    // We'll use the models endpoint for the test if possible, as it's a cheap GET request
                    // that doesn't require a valid model name, except for Anthropic which doesn't have a standard /models endpoint in OpenAI compat mode.
                    // For safety and universal compatibility across our proxy setups, we will send a 1-token dummy chat completion.
                    
                    string endpoint;
                    if (provider == ApiProvider.Anthropic_Claude)
                    {
                        if (baseUrl.EndsWith("/v1") || baseUrl.EndsWith("/v1/messages")) endpoint = $"{baseUrl.Replace("/v1/messages", "/v1")}/messages";
                        else endpoint = $"{baseUrl}/v1/messages";
                    }
                    else
                    {
                        if (baseUrl.EndsWith("/v1") || baseUrl.EndsWith("/v1beta/openai") || baseUrl.EndsWith("/v1/messages")) endpoint = $"{baseUrl.Replace("/v1/messages", "/v1")}/chat/completions";
                        else endpoint = $"{baseUrl}/v1/chat/completions";
                    }

                    string testModel = "test";
                    if (!string.IsNullOrEmpty(overrideModel))
                    {
                        testModel = overrideModel;
                    }
                    else
                    {
                        var settings = RimSynapseMod.Instance.Settings;
                        if (provider == ApiProvider.OpenAI) testModel = !string.IsNullOrEmpty(settings.modelOpenAi) ? settings.modelOpenAi : "gpt-5-chat-latest";
                        else if (provider == ApiProvider.Google_Gemini) testModel = !string.IsNullOrEmpty(settings.modelGemini) ? settings.modelGemini : "gemini-flash-lite-latest";
                        else if (provider == ApiProvider.Anthropic_Claude) testModel = !string.IsNullOrEmpty(settings.modelClaude) ? settings.modelClaude : "claude-opus-4-6";
                        else if (provider == ApiProvider.Local_LMStudio) testModel = !string.IsNullOrEmpty(settings.modelLocal) ? settings.modelLocal : (RimSynapse.Internal.ModelManager.ActiveModel ?? "local-model");
                        else if (provider == ApiProvider.Custom) testModel = !string.IsNullOrEmpty(settings.modelCustom) ? settings.modelCustom : "test";
                    }


                    var body = new JObject();
                    body["model"] = testModel;
                    body["max_tokens"] = 1;

                    if (provider == ApiProvider.Anthropic_Claude)
                    {
                        body["messages"] = new JArray { new JObject { ["role"] = "user", ["content"] = "ping" } };
                    }
                    else
                    {
                        body["messages"] = new JArray { new JObject { ["role"] = "user", ["content"] = "ping" } };
                    }

                    var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
                    };

                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        if (provider == ApiProvider.Anthropic_Claude)
                        {
                            request.Headers.Add("x-api-key", apiKey);
                            request.Headers.Add("anthropic-version", "2023-06-01");
                        }
                        else
                        {
                            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                        }
                    }

                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var response = _client.SendAsync(request, cts.Token).Result;
                    string resBody = response.Content.ReadAsStringAsync().Result;

                    RimSynapse.SynapseGameComponent.Enqueue(() => Verse.Log.Message($"[API TEST] Endpoint: {endpoint}\nRequest Body: {body.ToString(Newtonsoft.Json.Formatting.Indented)}\nResponse: {(int)response.StatusCode} {response.ReasonPhrase}\n{resBody}"));

                    if (response.IsSuccessStatusCode)
                    {
                        RimSynapse.SynapseGameComponent.Enqueue(() => callback(true, "Success!"));
                    }
                    else
                    {
                        string shortError = $"{(int)response.StatusCode} {response.ReasonPhrase}";
                        try
                        {
                            var errJson = JObject.Parse(resBody);
                            if (errJson["error"] != null)
                            {
                                shortError += $": {errJson["error"]["message"]?.ToString()}";
                            }
                        }
                        catch { }
                        


                        RimSynapse.SynapseGameComponent.Enqueue(() => callback(false, shortError));
                    }
                }
                catch (Exception ex)
                {
                    string error = ex.InnerException?.Message ?? ex.Message;
                    RimSynapse.SynapseGameComponent.Enqueue(() => callback(false, error));
                }
            });
        }
    }
}
