using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimSynapse.Internal.Providers
{
    public class AnthropicProvider : ILlmProvider
    {
        private readonly HttpClient _client;

        public AnthropicProvider(HttpClient client)
        {
            _client = client;
        }

        public ChatResult SendTextRequestSync(LlmTextRequest request, string baseUrl, string apiKey, string model)
        {
            try
            {
                baseUrl = baseUrl.TrimEnd('/');
                string url;
                if (baseUrl.EndsWith("/v1") || baseUrl.EndsWith("/v1/messages"))
                    url = $"{baseUrl.Replace("/v1/messages", "/v1")}/messages";
                else
                    url = $"{baseUrl}/v1/messages";

                var body = new JObject
                {
                    ["model"] = string.IsNullOrEmpty(model) ? "claude-opus-4-6" : model,
                    ["max_tokens"] = request.MaxTokens ?? 8192
                };

                if (request.Temperature.HasValue) body["temperature"] = request.Temperature.Value;

                if (!string.IsNullOrEmpty(request.SystemPrompt))
                {
                    body["system"] = request.SystemPrompt;
                }

                var messagesArray = new JArray();
                foreach (var msg in request.Messages)
                {
                    // Anthropic doesn't support "system" in the messages array
                    if (msg.role == "system") continue; 
                    messagesArray.Add(new JObject { ["role"] = msg.role, ["content"] = msg.content });
                }

                // If EnforceJson is true, Anthropic requires prefacing the assistant response with "{"
                if (request.EnforceJson)
                {
                    // Pre-fill assistant response to force JSON
                    messagesArray.Add(new JObject { ["role"] = "assistant", ["content"] = "{" });
                }

                body["messages"] = messagesArray;

                string jsonBody = body.ToString(Formatting.None);
                SynapseLogger.TraceContext(jsonBody, url);

                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                };

                if (!string.IsNullOrEmpty(apiKey))
                {
                    req.Headers.Add("x-api-key", apiKey);
                    req.Headers.Add("anthropic-version", "2023-06-01");
                }

                long startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                
                var response = _client.SendAsync(req, cts.Token).Result;
                string responseBody = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                {
                    long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                    var error = $"Anthropic returned {(int)response.StatusCode}: {responseBody}";
                    SynapseLogger.Error(error);
                    return ChatResult.Failure(error, dur);
                }

                var result = JObject.Parse(responseBody);
                string content = null;
                int promptTokens = 0;
                int completionTokens = 0;

                var contentArr = result["content"] as JArray;
                if (contentArr != null && contentArr.Count > 0)
                {
                    content = contentArr[0]?["text"]?.ToString();
                    if (request.EnforceJson && content != null)
                    {
                        // We forced it to start with {, so we need to add it back to the result
                        content = "{" + content;
                    }
                }

                var usage = result["usage"];
                if (usage != null)
                {
                    promptTokens = usage["input_tokens"]?.Value<int>() ?? 0;
                    completionTokens = usage["output_tokens"]?.Value<int>() ?? 0;
                }

                long durationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                return ChatResult.Success(content, model, promptTokens, completionTokens, durationMs);
            }
            catch (Exception ex)
            {
                string error = ex.InnerException?.Message ?? ex.Message;
                SynapseLogger.Error($"Anthropic Request failed: {error}");
                return ChatResult.Failure(error);
            }
        }

        public ChatResult SendVisionRequestSync(LlmVisionRequest request, string baseUrl, string apiKey, string model)
        {
            return ChatResult.Failure("Vision not fully implemented in AnthropicProvider yet.");
        }

        public ImageResult SendImageRequestSync(LlmImageRequest request, string baseUrl, string apiKey, string model)
        {
            return ImageResult.Failure("Anthropic does not support Image Generation.");
        }

        public AudioResult SendAudioRequestSync(LlmAudioRequest request, string baseUrl, string apiKey, string model)
        {
            return AudioResult.Failure("Anthropic does not support Audio Generation.");
        }
    }
}
