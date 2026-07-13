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
        internal static object RouteRequestSync(
            SynapseModHandle mod,
            object payload,
            LlmCapabilities capability,
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
                
                if (!string.IsNullOrEmpty(options?.providerOverride))
                {
                    routingId = options.providerOverride;
                }
                else
                {
                    if ((capability & LlmCapabilities.Image) == LlmCapabilities.Image) routingId = settings.defaultRoutingImage;
                    else if ((capability & LlmCapabilities.Vision) == LlmCapabilities.Vision) routingId = settings.defaultRoutingVision;
                    else if ((capability & LlmCapabilities.Audio) == LlmCapabilities.Audio) routingId = settings.defaultRoutingAudio;
                    else routingId = settings.defaultRoutingText;
                }

                string baseUrl = settings.lmStudioUrl;
                string apiKey = settings.lmStudioApiKey;
                ApiProvider providerHit = ApiProvider.Local_LMStudio;
                string defaultProviderModel = settings.modelLocal;

                if (routingId == RoutingId.OpenAI)
                {
                    baseUrl = settings.openAiUrl;
                    apiKey = settings.openAiApiKey;
                    providerHit = ApiProvider.OpenAI;
                    defaultProviderModel = settings.modelOpenAi;
                }
                else if (routingId == RoutingId.Gemini)
                {
                    baseUrl = settings.geminiUrl;
                    apiKey = settings.geminiApiKey;
                    providerHit = ApiProvider.Google_Gemini;
                    defaultProviderModel = settings.modelGemini;
                }
                else if (routingId == RoutingId.Claude)
                {
                    baseUrl = settings.claudeUrl;
                    apiKey = settings.claudeApiKey;
                    providerHit = ApiProvider.Anthropic_Claude;
                    defaultProviderModel = settings.modelClaude;
                }
                else if (routingId == RoutingId.Pollinations)
                {
                    baseUrl = settings.pollinationsUrl;
                    apiKey = "";
                    providerHit = ApiProvider.Pollinations;
                    defaultProviderModel = settings.modelPollinations;
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
                        defaultProviderModel = custom.model;
                    }
                }

                // Resolve model override hierarchy
                string targetModel = options?.model;
                if (string.IsNullOrEmpty(targetModel))
                {
                    // 1. Capability default override
                    if (string.IsNullOrEmpty(targetModel))
                    {
                        string capKey = "default_text";
                        if ((capability & LlmCapabilities.Image) == LlmCapabilities.Image) capKey = "default_image";
                        else if ((capability & LlmCapabilities.Vision) == LlmCapabilities.Vision) capKey = "default_vision";
                        else if ((capability & LlmCapabilities.Audio) == LlmCapabilities.Audio) capKey = "default_audio";
                        
                        if (settings.queryRoutingModels.TryGetValue(capKey, out var capModel) && !string.IsNullOrEmpty(capModel))
                        {
                            targetModel = capModel;
                        }
                    }
                    // 2. Fallback to provider default / active model
                    if (string.IsNullOrEmpty(targetModel))
                    {
                        if (providerHit == ApiProvider.Local_LMStudio) targetModel = ModelManager.ResolveModel(options?.model);
                        else targetModel = defaultProviderModel;
                    }
                }

                // Instantiate specific provider
                Providers.ILlmProvider provider;
                if (providerHit == ApiProvider.Anthropic_Claude) provider = new Providers.AnthropicProvider(_client);
                else if (providerHit == ApiProvider.Google_Gemini) provider = new Providers.GeminiProvider(_client);
                else if (providerHit == ApiProvider.Pollinations) provider = new Providers.PollinationsProvider(_client);
                else provider = new Providers.OpenAiProvider(_client);

                // Route to the appropriate interface method based on Payload Type
                if (payload is LlmTextRequest textReq)
                {
                    if (capability == LlmCapabilities.Vision)
                    {
                        // Some legacy compatibility fallback if they passed TextRequest but marked as Vision.
                        return provider.SendTextRequestSync(textReq, baseUrl, apiKey, targetModel);
                    }
                    var chatResult = provider.SendTextRequestSync(textReq, baseUrl, apiKey, targetModel);
                    
                    // Sanitize if enabled
                    if (chatResult.success && options?.sanitize != false && settings.sanitizeResponse)
                    {
                        chatResult.content = Sanitizer.Clean(chatResult.content);
                    }
                    
                    // Record usage
                    if (chatResult.success)
                    {
                        if (providerHit == ApiProvider.Local_LMStudio) { settings.tokensPromptLocal += chatResult.promptTokens; settings.tokensCompletionLocal += chatResult.completionTokens; }
                        else if (providerHit == ApiProvider.OpenAI) { settings.tokensPromptOpenAi += chatResult.promptTokens; settings.tokensCompletionOpenAi += chatResult.completionTokens; }
                        else if (providerHit == ApiProvider.Google_Gemini) { settings.tokensPromptGemini += chatResult.promptTokens; settings.tokensCompletionGemini += chatResult.completionTokens; }
                        else if (providerHit == ApiProvider.Anthropic_Claude) { settings.tokensPromptClaude += chatResult.promptTokens; settings.tokensCompletionClaude += chatResult.completionTokens; }
                        else if (providerHit == ApiProvider.Custom) { settings.tokensPromptCustom += chatResult.promptTokens; settings.tokensCompletionCustom += chatResult.completionTokens; }
                    }
                    
                    return chatResult;
                }
                else if (payload is LlmVisionRequest visionReq)
                {
                    return provider.SendVisionRequestSync(visionReq, baseUrl, apiKey, targetModel);
                }
                else if (payload is LlmImageRequest imgReq)
                {
                    return provider.SendImageRequestSync(imgReq, baseUrl, apiKey, targetModel);
                }
                else if (payload is LlmAudioRequest audReq)
                {
                    return provider.SendAudioRequestSync(audReq, baseUrl, apiKey, targetModel);
                }
                else
                {
                    return ChatResult.Failure("Unknown payload type submitted to HttpEngine.");
                }
            }
            catch (Exception ex)
            {
                long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                string error = ex.InnerException?.Message ?? ex.Message;
                SynapseLogger.Error($"RouteRequestSync Exception: {error}");
                
                if ((capability & LlmCapabilities.Image) == LlmCapabilities.Image) return ImageResult.Failure(error, dur);
                if ((capability & LlmCapabilities.Audio) == LlmCapabilities.Audio) return AudioResult.Failure(error, dur);
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

                // Dummy request removed: /v1/models now correctly returns all loaded models in LM Studio.

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
                    if (provider == ApiProvider.Pollinations)
                    {
                        var models = new System.Collections.Generic.List<string> { "flux", "flux-realism", "flux-anime", "flux-3d", "any-dark", "turbo" };
                        RimSynapse.SynapseGameComponent.Enqueue(() => callback(true, models, "Success"));
                        return;
                    }

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
                    
                    if (provider == ApiProvider.Pollinations)
                    {
                        var polliReq = new HttpRequestMessage(HttpMethod.Get, "https://image.pollinations.ai/prompt/test");
                        var polliCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        var polliRes = _client.SendAsync(polliReq, polliCts.Token).Result;
                        if (polliRes.IsSuccessStatusCode)
                        {
                            RimSynapse.SynapseGameComponent.Enqueue(() => callback(true, "Success!"));
                        }
                        else
                        {
                            string err = $"{(int)polliRes.StatusCode} {polliRes.ReasonPhrase}";
                            RimSynapse.SynapseGameComponent.Enqueue(() => callback(false, err));
                        }
                        return;
                    }
                    
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
