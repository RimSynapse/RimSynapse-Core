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
    /// Provider discovery methods: fetch models, fetch voices, test connections.
    /// All methods run asynchronously and dispatch results to the main thread.
    /// </summary>
    internal static partial class HttpEngine
    {
        public static void FetchProviderModelsAsync(ApiProvider provider, string baseUrl, string apiKey, Action<bool, List<string>, string> callback)
        {
            EnsureInitialized();
            Task.Run(() =>
            {
                try
                {
                    if (provider == ApiProvider.Pollinations)
                    {
                        var models = new List<string> { "flux", "flux-realism", "flux-anime", "flux-3d", "any-dark", "turbo" };
                        RimSynapse.SynapseGameComponent.Enqueue(() => callback(true, models, "Success"));
                        return;
                    }

                    if (provider == ApiProvider.ElevenLabs)
                    {
                        FetchElevenLabsModels(apiKey, callback);
                        return;
                    }

                    if (provider == ApiProvider.Voicebox)
                    {
                        FetchVoiceboxProfiles(baseUrl, apiKey, callback);
                        return;
                    }

                    FetchOpenAiCompatModels(provider, baseUrl, apiKey, callback);
                }
                catch (Exception ex)
                {
                    string error = ex.InnerException?.Message ?? ex.Message;
                    RimSynapse.SynapseGameComponent.Enqueue(() => callback(false, null, error));
                }
            });
        }

        private static void FetchElevenLabsModels(string apiKey, Action<bool, List<string>, string> callback)
        {
            var models = new List<string> {
                "Multilingual v2|eleven_multilingual_v2",
                "Turbo v2.5|eleven_turbo_v2_5",
                "Flash v2.5|eleven_flash_v2_5",
                "Multilingual v1|eleven_multilingual_v1",
                "English v1|eleven_monolingual_v1"
            };
            
            try
            {
                var mReq = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/models");
                if (!string.IsNullOrEmpty(apiKey)) mReq.Headers.Add("xi-api-key", apiKey);
                var mCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var mRes = _client.SendAsync(mReq, mCts.Token).Result;
                if (mRes.IsSuccessStatusCode)
                {
                    string mBody = mRes.Content.ReadAsStringAsync().Result;
                    var mj = JArray.Parse(mBody);
                    if (mj != null)
                    {
                        models.Clear();
                        foreach (var cv in mj)
                        {
                            string mid = cv["model_id"]?.ToString();
                            string mName = cv["name"]?.ToString();
                            if (!string.IsNullOrEmpty(mid)) models.Add($"{mName}|{mid}");
                        }
                    }
                }
            }
            catch { } // Silently fallback to default list if unauthorized/fails

            RimSynapse.SynapseGameComponent.Enqueue(() => callback(true, models, "Success"));
        }

        private static void FetchVoiceboxProfiles(string baseUrl, string apiKey, Action<bool, List<string>, string> callback)
        {
            var profilesList = new List<string>();
            try
            {
                baseUrl = baseUrl.TrimEnd('/');
                var pReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/profiles");
                if (!string.IsNullOrEmpty(apiKey))
                {
                    pReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }
                var pCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var pRes = _client.SendAsync(pReq, pCts.Token).Result;
                if (pRes.IsSuccessStatusCode)
                {
                    string pBody = pRes.Content.ReadAsStringAsync().Result;
                    var pj = JArray.Parse(pBody);
                    if (pj != null)
                    {
                        foreach (var cp in pj)
                        {
                            string pid = cp["id"]?.ToString();
                            string pName = cp["name"]?.ToString();
                            string pEngine = cp["default_engine"]?.ToString() ?? cp["preset_engine"]?.ToString() ?? "kokoro";
                            if (!string.IsNullOrEmpty(pid) && !string.IsNullOrEmpty(pName))
                            {
                                profilesList.Add($"{pName} ({pEngine})|{pid}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SynapseLogger.Error($"Voicebox FetchProviderModelsAsync failed: {ex.Message}");
            }

            if (profilesList.Count == 0)
            {
                profilesList.AddRange(new List<string> {
                    "Kokoro|kokoro",
                    "Qwen TTS|qwen",
                    "Qwen CustomVoice|qwen_custom_voice",
                    "LuxTTS|luxtts",
                    "Chatterbox TTS|chatterbox",
                    "Chatterbox Turbo|chatterbox_turbo",
                    "TADA|tada"
                });
            }

            RimSynapse.SynapseGameComponent.Enqueue(() => callback(true, profilesList, "Success"));
        }

        private static void FetchOpenAiCompatModels(ApiProvider provider, string baseUrl, string apiKey, Action<bool, List<string>, string> callback)
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
                var models = new List<string>();
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

        public static void FetchProviderVoicesAsync(ApiProvider provider, string apiKey, Action<bool, List<string>, string> callback)
        {
            EnsureInitialized();
            Task.Run(() =>
            {
                if (provider == ApiProvider.ElevenLabs)
                {
                    FetchElevenLabsVoices(apiKey, callback);
                    return;
                }
                
                RimSynapse.SynapseGameComponent.Enqueue(() => callback(false, null, "Provider does not support fetching voices"));
            });
        }

        private static void FetchElevenLabsVoices(string apiKey, Action<bool, List<string>, string> callback)
        {
            var voices = new List<string> {
                "Adam|pNInz6obpgDQGcFmaJgB",
                "Rachel|21m00Tcm4TlvDq8ikWAM",
                "Domi|AZnzlk1XvdvUeBnXmlld",
                "Bella|EXAVITQu4vr4xnSDxMaL",
                "Antoni|ErXwobaYiN019PkySvjV",
                "Elli|MF3mGyEYCl7XYWbV9V6O",
                "Josh|TxGEqnHWrfWFTfGW9XjX",
            };
            
            try
            {
                var vReq = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/voices");
                if (!string.IsNullOrEmpty(apiKey)) vReq.Headers.Add("xi-api-key", apiKey);
                var vCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var vRes = _client.SendAsync(vReq, vCts.Token).Result;
                if (vRes.IsSuccessStatusCode)
                {
                    string vBody = vRes.Content.ReadAsStringAsync().Result;
                    var vj = JObject.Parse(vBody);
                    var customVoices = vj["voices"] as JArray;
                    if (customVoices != null)
                    {
                        voices.Clear();
                        foreach (var cv in customVoices)
                        {
                            string vid = cv["voice_id"]?.ToString();
                            string vName = cv["name"]?.ToString();
                            if (!string.IsNullOrEmpty(vid)) voices.Add($"{vName}|{vid}");
                        }
                    }
                }
            }
            catch { } // Silently fallback to default list if unauthorized/fails

            RimSynapse.SynapseGameComponent.Enqueue(() => callback(true, voices, "Success"));
        }

        public static void TestConnectionAsync(ApiProvider provider, string url, string apiKey, string overrideModel, Action<bool, string> callback)
        {
            Task.Run(() =>
            {
                try
                {
                    EnsureInitialized();
                    // Strip any zero-width spaces or hidden unicode characters
                    url = System.Text.RegularExpressions.Regex.Replace(url ?? "", @"[^\u0020-\u007E]+", string.Empty);
                    apiKey = System.Text.RegularExpressions.Regex.Replace(apiKey ?? "", @"[^\u0020-\u007E]+", string.Empty);

                    string baseUrl = url.Trim().TrimEnd('/');
                    
                    if (provider == ApiProvider.Pollinations)
                    {
                        TestPollinationsConnection(callback);
                        return;
                    }
                    
                    if (provider == ApiProvider.ElevenLabs)
                    {
                        TestElevenLabsConnection(apiKey, overrideModel, callback);
                        return;
                    }

                    if (provider == ApiProvider.Voicebox)
                    {
                        TestVoiceboxConnection(baseUrl, apiKey, callback);
                        return;
                    }

                    TestGenericConnection(provider, baseUrl, apiKey, overrideModel, callback);
                }
                catch (Exception ex)
                {
                    string error = ex.InnerException?.Message ?? ex.Message;
                    RimSynapse.SynapseGameComponent.Enqueue(() => callback(false, error));
                }
            });
        }

        private static void TestPollinationsConnection(Action<bool, string> callback)
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
        }

        private static void TestElevenLabsConnection(string apiKey, string overrideModel, Action<bool, string> callback)
        {
            string targetModel = string.IsNullOrEmpty(overrideModel) ? "eleven_multilingual_v2" : overrideModel;
            var elReq = new HttpRequestMessage(HttpMethod.Post, "https://api.elevenlabs.io/v1/text-to-speech/pNInz6obpgDQGcFmaJgB");
            if (!string.IsNullOrEmpty(apiKey)) elReq.Headers.Add("xi-api-key", apiKey);
            var elBody = new JObject { ["text"] = "ping", ["model_id"] = targetModel };
            elReq.Content = new StringContent(elBody.ToString(Formatting.None), Encoding.UTF8, "application/json");
            
            var elCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var elRes = _client.SendAsync(elReq, elCts.Token).Result;
            if (elRes.IsSuccessStatusCode)
            {
                RimSynapse.SynapseGameComponent.Enqueue(() => callback(true, "Success!"));
            }
            else
            {
                string err = $"{(int)elRes.StatusCode} {elRes.ReasonPhrase}";
                try {
                    string rBody = elRes.Content.ReadAsStringAsync().Result;
                    var ej = JObject.Parse(rBody);
                    if (ej["detail"] != null && ej["detail"]["message"] != null) err += $": {ej["detail"]["message"].ToString()}";
                } catch {}
                RimSynapse.SynapseGameComponent.Enqueue(() => callback(false, err));
            }
        }

        private static void TestVoiceboxConnection(string baseUrl, string apiKey, Action<bool, string> callback)
        {
            baseUrl = baseUrl.TrimEnd('/');
            var vbReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/profiles");
            if (!string.IsNullOrEmpty(apiKey))
            {
                vbReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }
            var vbCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var vbRes = _client.SendAsync(vbReq, vbCts.Token).Result;
            if (vbRes.IsSuccessStatusCode)
            {
                RimSynapse.SynapseGameComponent.Enqueue(() => callback(true, "Success!"));
            }
            else
            {
                string err = $"{(int)vbRes.StatusCode} {vbRes.ReasonPhrase}";
                RimSynapse.SynapseGameComponent.Enqueue(() => callback(false, err));
            }
        }

        private static void TestGenericConnection(ApiProvider provider, string baseUrl, string apiKey, string overrideModel, Action<bool, string> callback)
        {
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

            string testModel = ResolveTestModel(provider, overrideModel);

            var body = new JObject();
            body["model"] = testModel;
            body["max_tokens"] = 1;
            body["messages"] = new JArray { new JObject { ["role"] = "user", ["content"] = "ping" } };

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

            RimSynapse.SynapseGameComponent.Enqueue(() => Verse.Log.Message($"[API TEST] Endpoint: {endpoint}\nRequest Body: {body.ToString(Formatting.Indented)}\nResponse: {(int)response.StatusCode} {response.ReasonPhrase}\n{resBody}"));

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

        private static string ResolveTestModel(ApiProvider provider, string overrideModel)
        {
            if (!string.IsNullOrEmpty(overrideModel)) return overrideModel;

            var settings = RimSynapseMod.Instance.Settings;
            if (provider == ApiProvider.OpenAI) return !string.IsNullOrEmpty(settings.modelOpenAi) ? settings.modelOpenAi : "gpt-5-chat-latest";
            if (provider == ApiProvider.Google_Gemini) return !string.IsNullOrEmpty(settings.modelGemini) ? settings.modelGemini : "gemini-flash-lite-latest";
            if (provider == ApiProvider.Anthropic_Claude) return !string.IsNullOrEmpty(settings.modelClaude) ? settings.modelClaude : "claude-opus-4-6";
            if (provider == ApiProvider.Local_LMStudio) return !string.IsNullOrEmpty(settings.modelLocal) ? settings.modelLocal : (ModelManager.ActiveModel ?? "local-model");
            if (provider == ApiProvider.Local_Jan) return !string.IsNullOrEmpty(settings.modelJan) ? settings.modelJan : "jan-model";
            if (provider == ApiProvider.Custom) return !string.IsNullOrEmpty(settings.modelCustom) ? settings.modelCustom : "test";
            return "test";
        }
    }
}
