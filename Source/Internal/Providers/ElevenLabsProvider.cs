using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimSynapse.Internal.Providers
{
    public class ElevenLabsProvider : ILlmProvider
    {
        private readonly HttpClient _client;

        public ElevenLabsProvider(HttpClient client)
        {
            _client = client;
        }

        public ChatResult SendTextRequestSync(LlmTextRequest request, string baseUrl, string apiKey, string model)
        {
            return ChatResult.Failure("ElevenLabs does not support text generation.");
        }

        public ChatResult SendVisionRequestSync(LlmVisionRequest request, string baseUrl, string apiKey, string model)
        {
            return ChatResult.Failure("ElevenLabs does not support vision.");
        }

        public ImageResult SendImageRequestSync(LlmImageRequest request, string baseUrl, string apiKey, string model)
        {
            return ImageResult.Failure("ElevenLabs does not support image generation.");
        }

        public AudioResult SendAudioRequestSync(LlmAudioRequest request, string baseUrl, string apiKey, string model)
        {
            try
            {
                baseUrl = baseUrl.TrimEnd('/');
                string voiceId = string.IsNullOrEmpty(request.Voice) ? "pNInz6obpgDQGcFmaJgB" : request.Voice; // default to Adam if empty
                
                // We must use pcm_24000 because our game engine assumes 24000hz for PCM playback
                string url = $"{baseUrl}/v1/text-to-speech/{voiceId}?output_format=pcm_24000";

                var body = new JObject
                {
                    ["text"] = request.InputText,
                    ["model_id"] = string.IsNullOrEmpty(model) ? "eleven_monolingual_v1" : model,
                    ["voice_settings"] = new JObject
                    {
                        ["stability"] = 0.5f,
                        ["similarity_boost"] = 0.75f,
                        ["style"] = 0.0f,
                        ["use_speaker_boost"] = true
                    }
                };

                string jsonBody = body.ToString(Formatting.None);
                SynapseLogger.TraceContext(jsonBody, url);

                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                };

                if (!string.IsNullOrEmpty(apiKey))
                {
                    req.Headers.Add("xi-api-key", apiKey);
                }

                long startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                
                var response = _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).Result;
                
                if (!response.IsSuccessStatusCode)
                {
                    long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                    string responseBody = response.Content.ReadAsStringAsync().Result;
                    var error = $"ElevenLabs endpoint returned {(int)response.StatusCode}: {responseBody}";
                    SynapseLogger.Error(error);
                    return AudioResult.Failure(error, dur);
                }

                string mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType != null && (mediaType.Contains("json") || mediaType.Contains("text")))
                {
                    long dur = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                    string responseBody = response.Content.ReadAsStringAsync().Result;
                    var error = $"Endpoint returned JSON/Text instead of audio bytes: {responseBody}";
                    SynapseLogger.Error(error);
                    return AudioResult.Failure(error, dur);
                }

                byte[] audioBytes;
                using (var stream = response.Content.ReadAsStreamAsync().Result)
                using (var ms = new System.IO.MemoryStream())
                {
                    stream.CopyTo(ms);
                    audioBytes = ms.ToArray();
                }
                
                string base64Audio = Convert.ToBase64String(audioBytes);
                
                long durationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                return AudioResult.Success(base64Audio, model, durationMs);
            }
            catch (Exception ex)
            {
                string error = ex.InnerException?.Message ?? ex.Message;
                SynapseLogger.Error($"ElevenLabs Audio Request failed: {error}");
                return AudioResult.Failure(error);
            }
        }
    }
}
