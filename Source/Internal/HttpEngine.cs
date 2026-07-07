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

                _client = new HttpClient(handler);
                _client.DefaultRequestHeaders.Add("Accept", "application/json");

                SynapseLog.Debug("client", "HttpClient initialized.");
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
        /// Runs on a background thread.
        /// </summary>
        internal static void PostChatCompletion(
            List<ChatMessage> messages,
            ChatOptions options,
            Action<ChatResult> callback)
        {
            EnsureInitialized();
            var settings = RimSynapseMod.Instance?.Settings;
            if (settings == null)
            {
                SynapseGameComponent.Enqueue(() =>
                    callback(ChatResult.Failure("RimSynapse settings not loaded.")));
                return;
            }

            Task.Run(() =>
            {
                long startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                try
                {
                    string baseUrl = settings.lmStudioUrl.TrimEnd('/');
                    string url = $"{baseUrl}/v1/chat/completions";

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
                            SynapseLog.Debug("client",
                                $"Bumping max_tokens from {tokens} to 8192 for reasoning model headroom.");
                            tokens = 8192;
                        }
                        body["max_tokens"] = tokens;
                    }

                    if (options?.temperature.HasValue == true)
                    {
                        body["temperature"] = options.temperature.Value;
                    }

                    // LM Studio quirk: remove response_format.type=json_object to prevent 400
                    body.Remove("response_format");

                    string jsonBody = body.ToString(Formatting.None);

                    var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                    };

                    // Auth header
                    if (!string.IsNullOrEmpty(settings.lmStudioApiKey))
                    {
                        request.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue(
                                "Bearer", settings.lmStudioApiKey);
                    }

                    // Set timeout
                    var cts = new CancellationTokenSource(
                        TimeSpan.FromSeconds(settings.timeoutSeconds));

                    var response = _client.SendAsync(request, cts.Token).Result;
                    string responseBody = response.Content.ReadAsStringAsync().Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                        var error = $"LM Studio returned {(int)response.StatusCode}: {responseBody}";
                        SynapseLog.Error("client", error);
                        SynapseGameComponent.Enqueue(() =>
                            callback(ChatResult.Failure(error, dur)));
                        return;
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
                                SynapseLog.Warn("client",
                                    "Assistant content was empty but reasoning_content present. Using fallback.");
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

                    var chatResult = ChatResult.Success(
                        content, model, promptTokens, completionTokens, durationMs);

                    SynapseLog.Info("client",
                        $"Completion received in {durationMs}ms — {promptTokens}p/{completionTokens}c tokens.");

                    SynapseGameComponent.Enqueue(() => callback(chatResult));
                }
                catch (Exception ex)
                {
                    long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                    string error = ex.InnerException?.Message ?? ex.Message;
                    SynapseLog.Error("client", $"Request failed: {error}");
                    SynapseGameComponent.Enqueue(() =>
                        callback(ChatResult.Failure(error, dur)));
                }
            });
        }

        /// <summary>
        /// GET the loaded models list from LM Studio.
        /// </summary>
        internal static void GetModels(Action<ModelsResult> callback)
        {
            EnsureInitialized();
            var settings = RimSynapseMod.Instance?.Settings;
            if (settings == null)
            {
                SynapseGameComponent.Enqueue(() =>
                    callback(new ModelsResult { online = false, error = "Settings not loaded." }));
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    string baseUrl = settings.lmStudioUrl.TrimEnd('/');
                    var result = new ModelsResult();

                    // OpenAI-compatible endpoint
                    var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/models");
                    if (!string.IsNullOrEmpty(settings.lmStudioApiKey))
                    {
                        request.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue(
                                "Bearer", settings.lmStudioApiKey);
                    }

                    var response = _client.SendAsync(request).Result;
                    if (!response.IsSuccessStatusCode)
                    {
                        result.online = false;
                        result.error = $"HTTP {(int)response.StatusCode}";
                        SynapseGameComponent.Enqueue(() => callback(result));
                        return;
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

                    // Try LM Studio native API for context length
                    try
                    {
                        var nativeRequest = new HttpRequestMessage(
                            HttpMethod.Get, $"{baseUrl}/api/v1/models");
                        if (!string.IsNullOrEmpty(settings.lmStudioApiKey))
                        {
                            nativeRequest.Headers.Authorization =
                                new System.Net.Http.Headers.AuthenticationHeaderValue(
                                    "Bearer", settings.lmStudioApiKey);
                        }

                        var nativeResponse = _client.SendAsync(nativeRequest).Result;
                        if (nativeResponse.IsSuccessStatusCode)
                        {
                            string nativeBody = nativeResponse.Content.ReadAsStringAsync().Result;
                            var nativeJson = JObject.Parse(nativeBody);
                            var models = nativeJson["models"] as JArray;
                            if (models != null)
                            {
                                foreach (var m in models)
                                {
                                    if (m["type"]?.ToString() == "llm")
                                    {
                                        var instances = m["loaded_instances"] as JArray;
                                        if (instances != null && instances.Count > 0)
                                        {
                                            int? ctxLen = instances[0]?["config"]?["context_length"]
                                                ?.Value<int>();
                                            if (ctxLen.HasValue)
                                            {
                                                result.contextLength = ctxLen.Value;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Native API is optional — silently ignore failures
                    }

                    SynapseGameComponent.Enqueue(() => callback(result));
                }
                catch (Exception ex)
                {
                    string error = ex.InnerException?.Message ?? ex.Message;
                    SynapseGameComponent.Enqueue(() =>
                        callback(new ModelsResult { online = false, error = error }));
                }
            });
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

                    SynapseLog.Debug("keepalive", $"Keep-alive ping sent to \"{model}\".");
                }
                catch
                {
                    // Best-effort — silently ignore
                }
            });
        }
    }
}
