using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSynapse.UI
{
    [StaticConstructorOnStartup]
    public partial class Dialog_TestBench : Window
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

    }
}

