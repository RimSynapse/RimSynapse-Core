using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSynapse.UI
{
    /// <summary>
    /// Test bench audio tab: TTS voice testing with provider selection.
    /// </summary>
    public partial class Dialog_TestBench
    {
        private void DrawAudioTab(Listing_Standard listing, RimSynapseSettings settings, Rect inRect)
        {
            if (_playAudioNextFrame && _audioToPlayBase64 != null)
            {
                _playAudioNextFrame = false;
                RimSynapse.Utils.AudioPlaybackManager.PlayBase64Pcm(_audioToPlayBase64);
                _audioToPlayBase64 = null;
            }

            if (listing.ButtonText($"Target Provider: {_selectedRoutingIdAudio}"))
            {
                var list = new List<FloatMenuOption>();
                list.Add(new FloatMenuOption(RoutingId.LocalOnly, () => _selectedRoutingIdAudio = RoutingId.LocalOnly));
                list.Add(new FloatMenuOption(RoutingId.Jan, () => _selectedRoutingIdAudio = RoutingId.Jan));
                list.Add(new FloatMenuOption("OpenAI", () => _selectedRoutingIdAudio = RoutingId.OpenAI));
                list.Add(new FloatMenuOption("ElevenLabs", () => _selectedRoutingIdAudio = RoutingId.ElevenLabs));
                list.Add(new FloatMenuOption("Voicebox", () => _selectedRoutingIdAudio = RoutingId.Voicebox));
                foreach(var custom in settings.customProviders)
                {
                    string id = RoutingId.CustomPrefix + custom.id;
                    list.Add(new FloatMenuOption($"Custom: {custom.name}", () => _selectedRoutingIdAudio = id));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }

            listing.Gap(10f);
            
            listing.Label("Target Model:");
            float selectBtnWidth = 80f;
            Rect modelRect = listing.GetRect(24f);
            Rect modelFieldRect = new Rect(modelRect.x, modelRect.y, modelRect.width - selectBtnWidth - 4f, modelRect.height);
            Rect selectBtnRect = new Rect(modelRect.xMax - selectBtnWidth, modelRect.y, selectBtnWidth, modelRect.height);
            
            if (pendingModelSelections.TryGetValue("Audio", out string newM))
            {
                _selectedModelAudio = newM;
                pendingModelSelections.Remove("Audio");
            }
            
            _selectedModelAudio = Widgets.TextField(modelFieldRect, _selectedModelAudio);
            
            if (Widgets.ButtonText(selectBtnRect, "Select..."))
            {
                ApiProvider? pEnum = null;
                if (_selectedRoutingIdAudio == RoutingId.LocalOnly) pEnum = ApiProvider.Local_LMStudio;
                else if (_selectedRoutingIdAudio == RoutingId.Jan) pEnum = ApiProvider.Local_Jan;
                else if (_selectedRoutingIdAudio == RoutingId.OpenAI) pEnum = ApiProvider.OpenAI;
                else if (_selectedRoutingIdAudio == RoutingId.Gemini) pEnum = ApiProvider.Google_Gemini;
                else if (_selectedRoutingIdAudio == RoutingId.Claude) pEnum = ApiProvider.Anthropic_Claude;
                else if (_selectedRoutingIdAudio == RoutingId.Pollinations) pEnum = ApiProvider.Pollinations;
                else if (_selectedRoutingIdAudio == RoutingId.ElevenLabs) pEnum = ApiProvider.ElevenLabs;
                else if (_selectedRoutingIdAudio == RoutingId.Voicebox) pEnum = ApiProvider.Voicebox;
                else if (_selectedRoutingIdAudio != null && _selectedRoutingIdAudio.StartsWith(RoutingId.CustomPrefix)) pEnum = ApiProvider.Custom;
                
                if (pEnum.HasValue)
                {
                    RimSynapse.Internal.ModelDefUtility.ShowModelSelector(pEnum.Value, LlmCapabilities.Audio, (selectedModel) => {
                        pendingModelSelections["Audio"] = selectedModel;
                    });
                }
            }

            listing.Gap(10f);
            
            if (listing.ButtonText($"Select Voice: {_selectedVoice}"))
            {
                if (_selectedRoutingIdAudio == RoutingId.ElevenLabs)
                {
                    string apiKey = RimSynapseMod.Instance.Settings.elevenLabsApiKey;
                    _testBusyAudio = true;
                    Internal.HttpEngine.FetchProviderVoicesAsync(ApiProvider.ElevenLabs, apiKey, (success, voices, err) => {
                        _testBusyAudio = false;
                        if (success && voices != null)
                        {
                            var list = new List<FloatMenuOption>();
                            foreach (var v in voices)
                            {
                                string label = v;
                                string val = v;
                                if (v.Contains("|"))
                                {
                                    var split = v.Split('|');
                                    label = split[0];
                                    val = split[1];
                                }
                                list.Add(new FloatMenuOption(label, () => _selectedVoice = val));
                            }
                            list.Add(new FloatMenuOption("custom...", () => _selectedVoice = "custom..."));
                            Find.WindowStack.Add(new FloatMenu(list));
                        }
                    });
                }
                else if (_selectedRoutingIdAudio == RoutingId.Voicebox)
                {
                    string url = RimSynapseMod.Instance.Settings.voiceboxUrl;
                    string apiKey = RimSynapseMod.Instance.Settings.voiceboxApiKey;
                    _testBusyAudio = true;
                    Internal.HttpEngine.FetchProviderModelsAsync(ApiProvider.Voicebox, url, apiKey, (success, profiles, err) => {
                        _testBusyAudio = false;
                        if (success && profiles != null)
                        {
                            var list = new List<FloatMenuOption>();
                            foreach (var p in profiles)
                            {
                                string label = p;
                                string val = p;
                                if (p.Contains("|"))
                                {
                                    var split = p.Split('|');
                                    label = split[0];
                                    val = split[1];
                                }
                                list.Add(new FloatMenuOption(label, () => _selectedVoice = val));
                            }
                            list.Add(new FloatMenuOption("custom...", () => _selectedVoice = "custom..."));
                            Find.WindowStack.Add(new FloatMenu(list));
                        }
                    });
                }
                else
                {
                    var list = new List<FloatMenuOption>();
                    foreach(var voice in StandardVoices)
                    {
                        string v = voice;
                        list.Add(new FloatMenuOption(v, () => _selectedVoice = v));
                    }
                    Find.WindowStack.Add(new FloatMenu(list));
                }
            }

            if (_selectedVoice == "custom...")
            {
                listing.Label("Custom Voice ID:");
                _customVoice = listing.TextEntry(_customVoice, 1);
            }

            listing.Gap(10f);
            listing.Label("TTS Input Text:");
            _customPromptAudio = listing.TextEntry(_customPromptAudio, 3);
            listing.Gap(4f);

            if (listing.ButtonText(_testBusyAudio ? "Generating Audio..." : "Generate and Play Audio"))
            {
                if (!_testBusyAudio && !string.IsNullOrWhiteSpace(_customPromptAudio))
                {
                    string activeVoice = _selectedVoice == "custom..." ? _customVoice : _selectedVoice;
                    RunTestAudio(activeVoice, _customPromptAudio);
                }
            }

            if (RimSynapse.Utils.AudioPlaybackManager.IsPlaying)
            {
                if (listing.ButtonText("Stop Playback"))
                {
                    RimSynapse.Utils.AudioPlaybackManager.StopPlayback();
                }
            }

            if (!string.IsNullOrEmpty(_testStatusAudio))
            {
                listing.Gap(10f);
                var prevColor = GUI.color;
                GUI.color = _testStatusColorAudio;
                listing.Label(_testStatusAudio);
                GUI.color = prevColor;
            }
        }

        private void RunTestAudio(string voice, string text)
        {
            _testBusyAudio = true;
            _testStatusAudio = $"Sending TTS request to {_selectedRoutingIdAudio} (voice: {voice})...";
            _testStatusColorAudio = Color.yellow;

            if (_testHandleAudio == null)
                _testHandleAudio = SynapseCore.Register("rimsynapse.audiotest", "RimSynapse Audio Test");

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Internal.HttpEngine.EnsureInitialized();
                    var options = ChatOptions.Default;
                    options.model = _selectedModelAudio;
                    options.providerOverride = _selectedRoutingIdAudio;
                    RimSynapseMod.Instance.Settings.queryRoutingIds["rimsynapse.audiotest:default"] = _selectedRoutingIdAudio;
                    
                    var req = new LlmAudioRequest { InputText = text, Voice = voice, ResponseFormat = "pcm" };
                    var resultObj = Internal.HttpEngine.RouteRequestSync(_testHandleAudio, req, LlmCapabilities.Audio, options);
                    var result = resultObj as AudioResult;

                    if (result != null && result.success)
                    {
                        _testStatusAudio = $"[{result.durationMs}ms | {result.model}]\nSuccess! Audio size: {result.base64Audio.Length} chars base64";
                        _testStatusColorAudio = Color.green;

                        _audioToPlayBase64 = result.base64Audio;
                        _playAudioNextFrame = true;
                    }
                    else
                    {
                        string err = result != null ? result.error : "Unknown routing error";
                        _testStatusAudio = $"Error: {err}";
                        _testStatusColorAudio = Color.red;
                    }
                }
                catch (Exception ex)
                {
                    _testStatusAudio = $"Error: {ex.Message}";
                    _testStatusColorAudio = Color.red;
                }
                finally
                {
                    _testBusyAudio = false;
                }
            });
        }

    }
}
