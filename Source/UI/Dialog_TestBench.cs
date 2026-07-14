using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSynapse.UI
{
    [StaticConstructorOnStartup]
    public class Dialog_TestBench : Window
    {
        private static int _currentTab = 0;
        private static Dictionary<string, string> pendingModelSelections = new Dictionary<string, string>();

        // --- Text State ---
        private static SynapseModHandle _testHandleText;
        private static string _testStatusText = "";
        private static Color _testStatusColorText = Color.white;
        private static string _customPromptText = "What are three tips for surviving a RimWorld raid?";
        private static bool _testBusyText;
        private static string _selectedRoutingIdText = RimSynapse.RoutingId.LocalOnly;
        private static string _selectedModelText = "";

        // --- Audio State ---
        private static SynapseModHandle _testHandleAudio;
        private static string _testStatusAudio = "";
        private static Color _testStatusColorAudio = Color.white;
        private static string _customPromptAudio = "Hello! I am speaking to you from RimWorld.";
        private static bool _testBusyAudio;
        private static string _selectedRoutingIdAudio = RimSynapse.RoutingId.LocalOnly;
        private static string _selectedVoice = "alloy";
        private static string _customVoice = "";
        private static string _selectedModelAudio = "tts-1";
        private static bool _playAudioNextFrame = false;
        private static string _audioToPlayBase64 = null;
        private static readonly string[] StandardVoices = { "alloy", "echo", "fable", "onyx", "nova", "shimmer", "custom..." };

        // --- Image State ---
        private static SynapseModHandle _testHandleImage;
        private static string _testStatusImage = "";
        private static Color _testStatusColorImage = Color.white;
        private static string _customPromptImage = "A beautiful sunset over a rimworld colony, digital art masterpiece";
        private static bool _testBusyImage;
        private static string _selectedRoutingIdImage = RimSynapse.RoutingId.Pollinations;
        private static string _selectedModelImage = "flux";
        private static bool _renderNextFrame = false;
        private static string _imageToRenderBase64 = null;
        private static Texture2D _textureToRender = null;

        public Dialog_TestBench()
        {
            this.forcePause = true;
            this.doCloseX = true;
            this.doCloseButton = true;
            this.closeOnClickedOutside = true;
            this.absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(600f, 750f);

        public override void DoWindowContents(Rect inRect)
        {
            var settings = RimSynapseMod.Instance.Settings;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "Prompt Workbench");
            Text.Font = GameFont.Small;

            // Draw Tabs
            List<TabRecord> tabs = new List<TabRecord>();
            tabs.Add(new TabRecord("Text (LLM)", () => _currentTab = 0, _currentTab == 0));
            tabs.Add(new TabRecord("Audio (TTS)", () => _currentTab = 1, _currentTab == 1));
            tabs.Add(new TabRecord("Image (Gen)", () => _currentTab = 2, _currentTab == 2));
            
            // TabDrawer draws tabs *above* the rect.y we provide.
            // A rect.y of 65f gives 35f for the title, plus the tab height (usually ~30f).
            Rect tabRect = new Rect(0, 65f, inRect.width, 30f);
            TabDrawer.DrawTabs(tabRect, tabs);

            Rect contentRect = new Rect(0, 65f, inRect.width, inRect.height - 65f);
            var listing = new Listing_Standard();
            listing.Begin(contentRect);

            if (_currentTab == 0) DrawTextTab(listing, settings);
            else if (_currentTab == 1) DrawAudioTab(listing, settings, inRect);
            else if (_currentTab == 2) DrawImageTab(listing, settings, inRect);

            listing.End();
        }

        // ==========================================
        // TEXT TAB
        // ==========================================
        private void DrawTextTab(Listing_Standard listing, RimSynapseSettings settings)
        {
            if (listing.ButtonText($"Target Provider: {_selectedRoutingIdText}"))
            {
                var list = new List<FloatMenuOption>();
                list.Add(new FloatMenuOption(RoutingId.LocalOnly, () => _selectedRoutingIdText = RoutingId.LocalOnly));
                list.Add(new FloatMenuOption(RoutingId.Jan, () => _selectedRoutingIdText = RoutingId.Jan));
                list.Add(new FloatMenuOption("OpenAI", () => _selectedRoutingIdText = RoutingId.OpenAI));
                list.Add(new FloatMenuOption(RoutingId.Gemini, () => _selectedRoutingIdText = RoutingId.Gemini));
                list.Add(new FloatMenuOption(RoutingId.Claude, () => _selectedRoutingIdText = RoutingId.Claude));
                foreach(var custom in settings.customProviders)
                {
                    string id = RoutingId.CustomPrefix + custom.id;
                    list.Add(new FloatMenuOption($"Custom: {custom.name}", () => _selectedRoutingIdText = id));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }

            listing.Gap(10f);
            
            listing.Label("Target Model:");
            float selectBtnWidth = 80f;
            Rect modelRect = listing.GetRect(24f);
            Rect modelFieldRect = new Rect(modelRect.x, modelRect.y, modelRect.width - selectBtnWidth - 4f, modelRect.height);
            Rect selectBtnRect = new Rect(modelRect.xMax - selectBtnWidth, modelRect.y, selectBtnWidth, modelRect.height);
            
            if (pendingModelSelections.TryGetValue("Text", out string newM))
            {
                _selectedModelText = newM;
                pendingModelSelections.Remove("Text");
            }
            
            _selectedModelText = Widgets.TextField(modelFieldRect, _selectedModelText);
            
            if (Widgets.ButtonText(selectBtnRect, "Select..."))
            {
                ApiProvider? pEnum = null;
                if (_selectedRoutingIdText == RoutingId.LocalOnly) pEnum = ApiProvider.Local_LMStudio;
                else if (_selectedRoutingIdText == RoutingId.Jan) pEnum = ApiProvider.Local_Jan;
                else if (_selectedRoutingIdText == RoutingId.OpenAI) pEnum = ApiProvider.OpenAI;
                else if (_selectedRoutingIdText == RoutingId.Gemini) pEnum = ApiProvider.Google_Gemini;
                else if (_selectedRoutingIdText == RoutingId.Claude) pEnum = ApiProvider.Anthropic_Claude;
                else if (_selectedRoutingIdText == RoutingId.Pollinations) pEnum = ApiProvider.Pollinations;
                else if (_selectedRoutingIdText != null && _selectedRoutingIdText.StartsWith(RoutingId.CustomPrefix)) pEnum = ApiProvider.Custom;
                
                if (pEnum.HasValue)
                {
                    RimSynapse.Internal.ModelDefUtility.ShowModelSelector(pEnum.Value, LlmCapabilities.Text, (selectedModel) => {
                        pendingModelSelections["Text"] = selectedModel;
                    });
                }
            }

            listing.Gap(10f);

            if (listing.ButtonText(_testBusyText ? "Sending..." : "Quick Test: \"Tell me a joke\""))
            {
                if (!_testBusyText)
                    RunTestPrompt("You are a witty comedian. Reply with one short joke.", "Tell me a joke.");
            }

            listing.Gap(4f);
            listing.Label("Custom Prompt:");
            _customPromptText = listing.TextEntry(_customPromptText, 3);
            listing.Gap(4f);

            if (listing.ButtonText(_testBusyText ? "Sending..." : "Send Custom Prompt"))
            {
                if (!_testBusyText && !string.IsNullOrWhiteSpace(_customPromptText))
                    RunTestPrompt("You are a helpful assistant. Be concise.", _customPromptText);
            }

            if (!string.IsNullOrEmpty(_testStatusText))
            {
                listing.Gap(10f);
                var prevColor = GUI.color;
                GUI.color = _testStatusColorText;
                listing.Label(_testStatusText);
                GUI.color = prevColor;
            }
        }

        private void RunTestPrompt(string systemPrompt, string userMessage)
        {
            _testBusyText = true;
            string thinkingLabel = RimSynapseMod.Instance.Settings.disableThinking ? "OFF" : "ON";
            _testStatusText = $"Sending prompt to {_selectedRoutingIdText} (thinking: {thinkingLabel})...";
            _testStatusColorText = Color.yellow;

            if (_testHandleText == null)
                _testHandleText = SynapseCore.Register("rimsynapse.test", "RimSynapse Test");

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Internal.HttpEngine.EnsureInitialized();
                    var messages = new List<ChatMessage>
                    {
                        ChatMessage.System(systemPrompt),
                        ChatMessage.User(userMessage),
                    };

                    var options = ChatOptions.Default;
                    options.model = _selectedModelText;
                    options.providerOverride = _selectedRoutingIdText;
                    
                    var req = new LlmTextRequest { Messages = messages, SystemPrompt = "", EnforceJson = false };
                    var resultObj = Internal.HttpEngine.RouteRequestSync(_testHandleText, req, LlmCapabilities.Text, options);
                    var result = resultObj as ChatResult;

                    if (result.success)
                    {
                        string preview = result.content;
                        if (preview != null && preview.Length > 200)
                            preview = preview.Substring(0, 200) + "...";

                        _testStatusText = $"[{result.durationMs}ms | {result.model} | {result.promptTokens}p/{result.completionTokens}c tokens | thinking: {thinkingLabel}]\n{preview}";
                        _testStatusColorText = Color.green;
                    }
                    else
                    {
                        _testStatusText = $"Error: {result.error}";
                        _testStatusColorText = Color.red;
                    }
                }
                catch (Exception ex)
                {
                    _testStatusText = $"Error: {ex.Message}";
                    _testStatusColorText = Color.red;
                }
                finally
                {
                    _testBusyText = false;
                }
            });
        }

        // ==========================================
        // AUDIO TAB
        // ==========================================
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

        // AudioPlaybackManager redirects used here

        // ==========================================
        // IMAGE TAB
        // ==========================================
        private void DrawImageTab(Listing_Standard listing, RimSynapseSettings settings, Rect inRect)
        {
            if (_renderNextFrame && _imageToRenderBase64 != null)
            {
                _renderNextFrame = false;
                LoadTextureFromBase64(_imageToRenderBase64);
                _imageToRenderBase64 = null;
            }

            if (listing.ButtonText($"Target Provider: {_selectedRoutingIdImage}"))
            {
                var list = new List<FloatMenuOption>();
                list.Add(new FloatMenuOption(RoutingId.LocalOnly, () => _selectedRoutingIdImage = RoutingId.LocalOnly));
                list.Add(new FloatMenuOption(RoutingId.Jan, () => _selectedRoutingIdImage = RoutingId.Jan));
                list.Add(new FloatMenuOption("OpenAI", () => _selectedRoutingIdImage = RoutingId.OpenAI));
                list.Add(new FloatMenuOption(RoutingId.Pollinations, () => _selectedRoutingIdImage = RoutingId.Pollinations));
                foreach(var custom in settings.customProviders)
                {
                    string id = RoutingId.CustomPrefix + custom.id;
                    list.Add(new FloatMenuOption($"Custom: {custom.name}", () => _selectedRoutingIdImage = id));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }

            listing.Gap(10f);
            
            listing.Label("Target Model:");
            float selectBtnWidth = 80f;
            Rect modelRectImg = listing.GetRect(24f);
            Rect modelFieldRectImg = new Rect(modelRectImg.x, modelRectImg.y, modelRectImg.width - selectBtnWidth - 4f, modelRectImg.height);
            Rect selectBtnRectImg = new Rect(modelRectImg.xMax - selectBtnWidth, modelRectImg.y, selectBtnWidth, modelRectImg.height);
            
            if (pendingModelSelections.TryGetValue("Image", out string newImgM))
            {
                _selectedModelImage = newImgM;
                pendingModelSelections.Remove("Image");
            }
            
            _selectedModelImage = Widgets.TextField(modelFieldRectImg, _selectedModelImage);
            
            if (Widgets.ButtonText(selectBtnRectImg, "Select..."))
            {
                ApiProvider? pEnum = null;
                if (_selectedRoutingIdImage == RoutingId.LocalOnly) pEnum = ApiProvider.Local_LMStudio;
                else if (_selectedRoutingIdImage == RoutingId.Jan) pEnum = ApiProvider.Local_Jan;
                else if (_selectedRoutingIdImage == RoutingId.OpenAI) pEnum = ApiProvider.OpenAI;
                else if (_selectedRoutingIdImage == RoutingId.Gemini) pEnum = ApiProvider.Google_Gemini;
                else if (_selectedRoutingIdImage == RoutingId.Claude) pEnum = ApiProvider.Anthropic_Claude;
                else if (_selectedRoutingIdImage == RoutingId.Pollinations) pEnum = ApiProvider.Pollinations;
                else if (_selectedRoutingIdImage != null && _selectedRoutingIdImage.StartsWith(RoutingId.CustomPrefix)) pEnum = ApiProvider.Custom;
                
                if (pEnum.HasValue)
                {
                    RimSynapse.Internal.ModelDefUtility.ShowModelSelector(pEnum.Value, LlmCapabilities.Image, (selectedModel) => {
                        pendingModelSelections["Image"] = selectedModel;
                    });
                }
            }

            listing.Gap(10f);
            listing.Label("Image Prompt:");
            _customPromptImage = listing.TextEntry(_customPromptImage, 3);
            listing.Gap(4f);

            if (listing.ButtonText(_testBusyImage ? "Generating Image..." : "Generate Image"))
            {
                if (!_testBusyImage && !string.IsNullOrWhiteSpace(_customPromptImage))
                {
                    RunTestImage(_customPromptImage);
                }
            }

            if (!string.IsNullOrEmpty(_testStatusImage))
            {
                listing.Gap(10f);
                var prevColor = GUI.color;
                GUI.color = _testStatusColorImage;
                listing.Label(_testStatusImage);
                GUI.color = prevColor;
            }

            if (_textureToRender != null)
            {
                float imgSize = 400f;
                float startY = 320f;
                Rect imgRect = new Rect((inRect.width - imgSize) / 2f, startY, imgSize, imgSize);
                GUI.DrawTexture(imgRect, _textureToRender, ScaleMode.ScaleToFit);
            }
        }

        private void RunTestImage(string text)
        {
            _testBusyImage = true;
            _testStatusImage = $"Sending Image request to {_selectedRoutingIdImage} (model: {_selectedModelImage})...";
            _testStatusColorImage = Color.yellow;
            _textureToRender = null;

            if (_testHandleImage == null)
                _testHandleImage = SynapseCore.Register("rimsynapse.imagetest", "RimSynapse Image Test");

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Internal.HttpEngine.EnsureInitialized();
                    var options = ChatOptions.Default;
                    options.model = _selectedModelImage;
                    options.providerOverride = _selectedRoutingIdImage;
                    
                    RimSynapseMod.Instance.Settings.queryRoutingIds["rimsynapse.imagetest:default"] = _selectedRoutingIdImage;
                    
                    var req = new LlmImageRequest { Prompt = text };
                    var resultObj = Internal.HttpEngine.RouteRequestSync(_testHandleImage, req, LlmCapabilities.Image, options);
                    var result = resultObj as ImageResult;

                    if (result != null && result.success)
                    {
                        _testStatusImage = $"[{result.durationMs}ms | {result.model}]\nSuccess!";
                        _testStatusColorImage = Color.green;

                        _imageToRenderBase64 = result.base64Data;
                        _renderNextFrame = true;
                    }
                    else
                    {
                        string err = result != null ? result.error : "Unknown routing error";
                        _testStatusImage = $"Error: {err}";
                        _testStatusColorImage = Color.red;
                    }
                }
                catch (Exception ex)
                {
                    _testStatusImage = $"Error: {ex.Message}";
                    _testStatusColorImage = Color.red;
                }
                finally
                {
                    _testBusyImage = false;
                }
            });
        }

        private void LoadTextureFromBase64(string base64)
        {
            try
            {
                byte[] imgBytes = Convert.FromBase64String(base64);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(imgBytes);
                _textureToRender = tex;
            }
            catch (Exception ex)
            {
                SynapseLogger.Error($"Image render error: {ex.Message}");
                _testStatusImage = $"Render error: {ex.Message}";
                _testStatusColorImage = Color.red;
            }
        }
    }
}
