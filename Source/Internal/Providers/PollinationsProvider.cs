using System;
using System.Net.Http;
using System.Threading;

namespace RimSynapse.Internal.Providers
{
    public class PollinationsProvider : ILlmProvider
    {
        private readonly HttpClient _client;

        public PollinationsProvider(HttpClient client)
        {
            _client = client;
        }

        public ChatResult SendTextRequestSync(LlmTextRequest request, string baseUrl, string apiKey, string model)
        {
            return ChatResult.Failure("Pollinations.ai does not support Text Generation.");
        }

        public ChatResult SendVisionRequestSync(LlmVisionRequest request, string baseUrl, string apiKey, string model)
        {
            return ChatResult.Failure("Pollinations.ai does not support Vision Analysis.");
        }

        public ImageResult SendImageRequestSync(LlmImageRequest request, string baseUrl, string apiKey, string model)
        {
            try
            {
                // Format: https://image.pollinations.ai/prompt/{prompt}?model={model}&nologo=true&seed={seed}
                string encodedPrompt = Uri.EscapeDataString(request.Prompt);
                string url = $"{baseUrl.TrimEnd('/')}/{encodedPrompt}?nologo=true";
                
                if (!string.IsNullOrEmpty(model)) url += $"&model={model}";
                if (request.Seed.HasValue) url += $"&seed={request.Seed.Value}";
                
                // Add width/height based on aspect ratio
                if (request.AspectRatio == "16:9") url += "&width=1024&height=576";
                else if (request.AspectRatio == "9:16") url += "&width=576&height=1024";
                else url += "&width=1024&height=1024";

                long startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                var response = _client.SendAsync(req, cts.Token).Result;
                
                if (!response.IsSuccessStatusCode)
                {
                    long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                    var error = $"Pollinations returned {(int)response.StatusCode}";
                    SynapseLogger.Error(error);
                    return ImageResult.Failure(error, dur);
                }

                byte[] imageBytes = response.Content.ReadAsByteArrayAsync().Result;
                string base64 = Convert.ToBase64String(imageBytes);
                
                long durationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                return ImageResult.Success(base64, model, durationMs);
            }
            catch (Exception ex)
            {
                string error = ex.InnerException?.Message ?? ex.Message;
                SynapseLogger.Error($"Pollinations Request failed: {error}");
                return ImageResult.Failure(error);
            }
        }

        public AudioResult SendAudioRequestSync(LlmAudioRequest request, string baseUrl, string apiKey, string model)
        {
            return AudioResult.Failure("Pollinations.ai does not support Audio Generation.");
        }
    }
}
