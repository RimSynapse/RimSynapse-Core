using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimSynapse.Internal.Providers
{
    public class OpenAiProvider : ILlmProvider
    {
        private readonly HttpClient _client;

        public OpenAiProvider(HttpClient client)
        {
            _client = client;
        }

        public ChatResult SendTextRequestSync(LlmTextRequest request, string baseUrl, string apiKey, string model)
        {
            try
            {
                baseUrl = baseUrl.TrimEnd('/');
                string url;
                if (baseUrl.EndsWith("/v1") || baseUrl.EndsWith("/v1beta/openai") || baseUrl.EndsWith("/v1/messages"))
                    url = $"{baseUrl.Replace("/v1/messages", "/v1")}/chat/completions";
                else
                    url = $"{baseUrl}/v1/chat/completions";

                var messagesArray = new JArray();
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                {
                    messagesArray.Add(new JObject { ["role"] = "system", ["content"] = request.SystemPrompt });
                }
                foreach (var msg in request.Messages)
                {
                    messagesArray.Add(new JObject { ["role"] = msg.role, ["content"] = msg.content });
                }

                var body = new JObject
                {
                    ["model"] = model,
                    ["messages"] = messagesArray
                };

                if (request.MaxTokens.HasValue) body["max_tokens"] = request.MaxTokens.Value;
                if (request.Temperature.HasValue) body["temperature"] = request.Temperature.Value;
                
                if (request.EnforceJson)
                {
                    body["response_format"] = new JObject { ["type"] = "json_object" };
                }

                string jsonBody = body.ToString(Formatting.None);
                SynapseLogger.TraceContext(jsonBody, url);

                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                };

                if (!string.IsNullOrEmpty(apiKey))
                {
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }

                long startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                
                var response = _client.SendAsync(req, cts.Token).Result;
                string responseBody = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                {
                    long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                    var error = $"OpenAI-compatible endpoint returned {(int)response.StatusCode}: {responseBody}";
                    SynapseLogger.Error(error);
                    return ChatResult.Failure(error, dur);
                }

                var result = JObject.Parse(responseBody);
                string content = null;
                int promptTokens = 0;
                int completionTokens = 0;

                var choices = result["choices"] as JArray;
                if (choices != null && choices.Count > 0)
                {
                    var message = choices[0]?["message"];
                    content = message?["content"]?.ToString();
                }

                var usage = result["usage"];
                if (usage != null)
                {
                    promptTokens = usage["prompt_tokens"]?.Value<int>() ?? 0;
                    completionTokens = usage["completion_tokens"]?.Value<int>() ?? 0;
                }

                long durationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                return ChatResult.Success(content, model, promptTokens, completionTokens, durationMs);
            }
            catch (Exception ex)
            {
                string error = ex.InnerException?.Message ?? ex.Message;
                SynapseLogger.Error($"OpenAI Request failed: {error}");
                return ChatResult.Failure(error);
            }
        }

        public ChatResult SendVisionRequestSync(LlmVisionRequest request, string baseUrl, string apiKey, string model)
        {
            // Similar to text, but with vision payload formatting.
            return ChatResult.Failure("Vision not fully implemented in OpenAiProvider yet.");
        }

        public ImageResult SendImageRequestSync(LlmImageRequest request, string baseUrl, string apiKey, string model)
        {
            return ImageResult.Failure("Image generation not fully implemented in OpenAiProvider yet.");
        }

        public AudioResult SendAudioRequestSync(LlmAudioRequest request, string baseUrl, string apiKey, string model)
        {
            try
            {
                baseUrl = baseUrl.TrimEnd('/');
                string url;
                if (baseUrl.EndsWith("/v1") || baseUrl.EndsWith("/v1beta/openai") || baseUrl.EndsWith("/v1/messages"))
                    url = $"{baseUrl.Replace("/v1/messages", "/v1")}/audio/speech";
                else
                    url = $"{baseUrl}/v1/audio/speech";

                var body = new JObject
                {
                    ["model"] = model,
                    ["input"] = request.InputText,
                    ["voice"] = string.IsNullOrEmpty(request.Voice) ? "alloy" : request.Voice,
                    ["response_format"] = string.IsNullOrEmpty(request.ResponseFormat) ? "pcm" : request.ResponseFormat
                };

                string jsonBody = body.ToString(Formatting.None);
                SynapseLogger.TraceContext(jsonBody, url);

                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                };

                if (!string.IsNullOrEmpty(apiKey))
                {
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }

                long startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                
                var response = _client.SendAsync(req, cts.Token).Result;
                
                if (!response.IsSuccessStatusCode)
                {
                    long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                    string responseBody = response.Content.ReadAsStringAsync().Result;
                    var error = $"OpenAI-compatible audio endpoint returned {(int)response.StatusCode}: {responseBody}";
                    SynapseLogger.Error(error);
                    return AudioResult.Failure(error, dur);
                }

                // Many local backends erroneously return 200 OK with a JSON error when the endpoint isn't supported.
                string mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType != null && (mediaType.Contains("json") || mediaType.Contains("text")))
                {
                    long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                    string responseBody = response.Content.ReadAsStringAsync().Result;
                    var error = $"Endpoint returned JSON/Text instead of audio bytes: {responseBody}";
                    SynapseLogger.Error(error);
                    return AudioResult.Failure(error, dur);
                }

                byte[] audioBytes = response.Content.ReadAsByteArrayAsync().Result;
                string base64Audio = Convert.ToBase64String(audioBytes);
                
                long durationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                return AudioResult.Success(base64Audio, model, durationMs);
            }
            catch (Exception ex)
            {
                string error = ex.InnerException?.Message ?? ex.Message;
                SynapseLogger.Error($"OpenAI Audio Request failed: {error}");
                return AudioResult.Failure(error);
            }
        }
    }
}
