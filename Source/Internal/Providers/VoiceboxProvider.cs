using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimSynapse.Internal.Providers
{
    public class VoiceboxProvider : ILlmProvider
    {
        private readonly HttpClient _client;

        public VoiceboxProvider(HttpClient client)
        {
            _client = client;
        }

        public ChatResult SendTextRequestSync(LlmTextRequest request, string baseUrl, string apiKey, string model)
        {
            return ChatResult.Failure("Voicebox does not support text generation.");
        }

        public ChatResult SendVisionRequestSync(LlmVisionRequest request, string baseUrl, string apiKey, string model)
        {
            return ChatResult.Failure("Voicebox does not support vision.");
        }

        public ImageResult SendImageRequestSync(LlmImageRequest request, string baseUrl, string apiKey, string model)
        {
            return ImageResult.Failure("Voicebox does not support image generation.");
        }

        public AudioResult SendAudioRequestSync(LlmAudioRequest request, string baseUrl, string apiKey, string model)
        {
            long startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                baseUrl = baseUrl.TrimEnd('/');
                string profileId = request.Voice;

                // Resolve voice name/ID to profile UUID using Voicebox profiles listing
                if (!string.IsNullOrEmpty(profileId))
                {
                    try
                    {
                        var listReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/profiles");
                        if (!string.IsNullOrEmpty(apiKey))
                        {
                            listReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                        }
                        var listRes = _client.SendAsync(listReq).Result;
                        if (listRes.IsSuccessStatusCode)
                        {
                            string searchName = profileId;
                            if (searchName.Contains("/") || searchName.Contains("\\") || searchName.Contains("."))
                            {
                                try { searchName = System.IO.Path.GetFileNameWithoutExtension(searchName); } catch {}
                            }

                            var profiles = JArray.Parse(listRes.Content.ReadAsStringAsync().Result);
                            foreach (var p in profiles)
                            {
                                string pid = p["id"]?.ToString();
                                string pName = p["name"]?.ToString();
                                if (!string.IsNullOrEmpty(pName) && (pName.Equals(profileId, StringComparison.OrdinalIgnoreCase) || pName.Equals(searchName, StringComparison.OrdinalIgnoreCase) || pid.Equals(profileId, StringComparison.OrdinalIgnoreCase)))
                                {
                                    profileId = pid;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        SynapseLogger.Warning($"Failed to resolve Voicebox profile: {ex.Message}");
                    }
                }

                // If no voice profile matches or voice is empty, query profiles to use the first available one as default
                if (string.IsNullOrEmpty(profileId))
                {
                    try
                    {
                        var listReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/profiles");
                        if (!string.IsNullOrEmpty(apiKey))
                        {
                            listReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                        }
                        var listRes = _client.SendAsync(listReq).Result;
                        if (listRes.IsSuccessStatusCode)
                        {
                            var profiles = JArray.Parse(listRes.Content.ReadAsStringAsync().Result);
                            if (profiles.Count > 0)
                            {
                                profileId = profiles[0]["id"]?.ToString();
                            }
                        }
                    }
                    catch { }
                }

                // Resolve model and model size (e.g. qwen:1.7B -> engine "qwen", size "1.7B")
                string engine = "kokoro";
                string modelSize = null;
                if (!string.IsNullOrEmpty(model))
                {
                    int colonIndex = model.IndexOf(':');
                    if (colonIndex >= 0)
                    {
                        engine = model.Substring(0, colonIndex);
                        modelSize = model.Substring(colonIndex + 1);
                    }
                    else
                    {
                        engine = model;
                    }
                }

                // Create generation request
                var body = new JObject
                {
                    ["profile_id"] = profileId,
                    ["text"] = request.InputText,
                    ["engine"] = engine,
                    ["language"] = "en"
                };

                if (!string.IsNullOrEmpty(request.Instruct))
                {
                    body["instruct"] = request.Instruct;
                }
                if (!string.IsNullOrEmpty(modelSize))
                {
                    body["model_size"] = modelSize;
                }

                string jsonBody = body.ToString(Formatting.None);
                SynapseLogger.TraceContext(jsonBody, $"{baseUrl}/generate");

                var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/generate")
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };
                if (!string.IsNullOrEmpty(apiKey))
                {
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                var response = _client.SendAsync(req, cts.Token).Result;

                if (!response.IsSuccessStatusCode)
                {
                    string errBody = response.Content.ReadAsStringAsync().Result;
                    string err = $"Voicebox generate returned {(int)response.StatusCode}: {errBody}";
                    SynapseLogger.Error(err);
                    return AudioResult.Failure(err, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs);
                }

                var genRes = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                string generationId = genRes["id"]?.ToString();
                if (string.IsNullOrEmpty(generationId))
                {
                    string err = "Voicebox generate response missing generation ID";
                    SynapseLogger.Error(err);
                    return AudioResult.Failure(err, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs);
                }

                // Fetch generated audio
                var audioReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/audio/{generationId}");
                if (!string.IsNullOrEmpty(apiKey))
                {
                    audioReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }
                var audioRes = _client.SendAsync(audioReq, HttpCompletionOption.ResponseHeadersRead, cts.Token).Result;

                if (!audioRes.IsSuccessStatusCode)
                {
                    string errBody = audioRes.Content.ReadAsStringAsync().Result;
                    string err = $"Voicebox audio fetch returned {(int)audioRes.StatusCode}: {errBody}";
                    SynapseLogger.Error(err);
                    return AudioResult.Failure(err, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs);
                }

                byte[] wavBytes;
                using (var stream = audioRes.Content.ReadAsStreamAsync().Result)
                using (var ms = new System.IO.MemoryStream())
                {
                    stream.CopyTo(ms);
                    wavBytes = ms.ToArray();
                }

                byte[] pcmBytes = ExtractPcmFromWav(wavBytes);
                string base64Audio = Convert.ToBase64String(pcmBytes);

                long durationMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                return AudioResult.Success(base64Audio, model, durationMs);
            }
            catch (Exception ex)
            {
                string error = ex.InnerException?.Message ?? ex.Message;
                SynapseLogger.Error($"Voicebox request failed: {error}");
                return AudioResult.Failure(error, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs);
            }
        }

        private static byte[] ExtractPcmFromWav(byte[] wavBytes)
        {
            if (wavBytes == null || wavBytes.Length < 44) return wavBytes;

            if (wavBytes[0] != 'R' || wavBytes[1] != 'I' || wavBytes[2] != 'F' || wavBytes[3] != 'F' ||
                wavBytes[8] != 'W' || wavBytes[9] != 'A' || wavBytes[10] != 'V' || wavBytes[11] != 'E')
            {
                return wavBytes;
            }

            int index = 12;
            while (index < wavBytes.Length - 8)
            {
                string chunkId = "" + (char)wavBytes[index] + (char)wavBytes[index + 1] + (char)wavBytes[index + 2] + (char)wavBytes[index + 3];
                int chunkSize = BitConverter.ToInt32(wavBytes, index + 4);
                index += 8;

                if (chunkId == "data")
                {
                    int pcmLength = Math.Min(chunkSize, wavBytes.Length - index);
                    if (pcmLength <= 0) return new byte[0];
                    byte[] pcmBytes = new byte[pcmLength];
                    Array.Copy(wavBytes, index, pcmBytes, 0, pcmLength);
                    return pcmBytes;
                }

                index += chunkSize;
            }

            byte[] fallbackPcm = new byte[wavBytes.Length - 44];
            Array.Copy(wavBytes, 44, fallbackPcm, 0, fallbackPcm.Length);
            return fallbackPcm;
        }
    }
}
