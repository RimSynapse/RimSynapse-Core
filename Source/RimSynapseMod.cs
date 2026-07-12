using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimSynapse
{
    /// <summary>
    /// RimWorld mod entry point. Initializes Harmony patches and provides
    /// the mod settings UI.
    /// </summary>
    public class RimSynapseMod : Mod
    {
        public static RimSynapseMod Instance { get; private set; }
        public RimSynapseSettings Settings { get; }

        private const string HarmonyId = "RimSynapse.Core";

        // Internal test state
        private static SynapseModHandle _testHandle;
        private static string _testStatus = "";
        private static Color _testStatusColor = Color.white;
        private static string _customPrompt = "What are three tips for surviving a RimWorld raid?";
        private static bool _testBusy;

        // Scrollable content
        private static Vector2 _scrollPosition;

        public static SynapseModHandle ModHandle { get; private set; }

        public RimSynapseMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RimSynapseSettings>();

            RimSynapse.SynapseLogger.InitMainThread();

            // Apply Harmony patches
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll();

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                ModHandle = SynapseCore.Register(
                    "rimsynapse.core",
                    "RimSynapse Core"
                );
            });

            // Start background services (keep-alive, model discovery, HttpClient, SessionLogger)
            // immediately — uses system timers, independent of game ticks.
            SynapseCore.Initialize();

            RimSynapse.SynapseLogger.Message("[RimSynapse] Core initialized. Harmony patches applied.");
        }

        public override string SettingsCategory() => "RimSynapse Core";
        private void DrawCapabilitiesRow(Listing_Standard listing, ref LlmCapabilities caps)
        {
            Rect rect = listing.GetRect(24f);
            rect.x += 10f; // indent slightly
            rect.width -= 10f;
            float width = rect.width / 4f;
            
            bool hasText = (caps & LlmCapabilities.Text) == LlmCapabilities.Text;
            bool hasImage = (caps & LlmCapabilities.Image) == LlmCapabilities.Image;
            bool hasVision = (caps & LlmCapabilities.Vision) == LlmCapabilities.Vision;
            bool hasAudio = (caps & LlmCapabilities.Audio) == LlmCapabilities.Audio;

            Widgets.CheckboxLabeled(new Rect(rect.x, rect.y, width - 10f, rect.height), "Text", ref hasText);
            Widgets.CheckboxLabeled(new Rect(rect.x + width, rect.y, width - 10f, rect.height), "Image", ref hasImage);
            Widgets.CheckboxLabeled(new Rect(rect.x + width * 2, rect.y, width - 10f, rect.height), "Vision", ref hasVision);
            Widgets.CheckboxLabeled(new Rect(rect.x + width * 3, rect.y, width - 10f, rect.height), "Audio", ref hasAudio);

            caps = LlmCapabilities.None;
            if (hasText) caps |= LlmCapabilities.Text;
            if (hasImage) caps |= LlmCapabilities.Image;
            if (hasVision) caps |= LlmCapabilities.Vision;
            if (hasAudio) caps |= LlmCapabilities.Audio;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Scrollable container for all settings
            var viewRect = new Rect(0, 0, inRect.width - 20f, 1200f);
            Widgets.BeginScrollView(inRect, ref _scrollPosition, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // ── Connection Settings ──────────────────────────────────
            listing.Label("Connection Settings", tooltip: "Configure your local and cloud LLM providers.");
            listing.GapLine();

            if (listing.ButtonText("Open Query Routing Window"))
            {
                Find.WindowStack.Add(new RimSynapse.UI.Dialog_QueryRouting());
            }
            listing.Gap(6f);

            // Local
            listing.Label("Local LM Studio");
            Settings.lmStudioUrl = listing.TextEntryLabeled("  URL:  ", Settings.lmStudioUrl);
            Settings.lmStudioApiKey = listing.TextEntryLabeled("  Key:  ", Settings.lmStudioApiKey);
            DrawCapabilitiesRow(listing, ref Settings.capsLocal);
            listing.Gap(4f);

            // OpenAI
            listing.Label("OpenAI");
            Settings.openAiUrl = listing.TextEntryLabeled("  URL:  ", Settings.openAiUrl);
            Settings.openAiApiKey = listing.TextEntryLabeled("  Key:  ", Settings.openAiApiKey);
            DrawCapabilitiesRow(listing, ref Settings.capsOpenAi);
            listing.Gap(4f);

            // Gemini
            listing.Label("Google Gemini");
            Settings.geminiUrl = listing.TextEntryLabeled("  URL:  ", Settings.geminiUrl);
            Settings.geminiApiKey = listing.TextEntryLabeled("  Key:  ", Settings.geminiApiKey);
            DrawCapabilitiesRow(listing, ref Settings.capsGemini);
            listing.Gap(4f);

            // Claude
            listing.Label("Anthropic Claude");
            Settings.claudeUrl = listing.TextEntryLabeled("  URL:  ", Settings.claudeUrl);
            Settings.claudeApiKey = listing.TextEntryLabeled("  Key:  ", Settings.claudeApiKey);
            DrawCapabilitiesRow(listing, ref Settings.capsClaude);
            listing.Gap(4f);

            // Custom
            listing.Label("Custom / Proxy");
            Settings.customUrl = listing.TextEntryLabeled("  URL:  ", Settings.customUrl);
            Settings.customApiKey = listing.TextEntryLabeled("  Key:  ", Settings.customApiKey);
            DrawCapabilitiesRow(listing, ref Settings.capsCustom);
            listing.Gap(6f);

            // Default Provider for testing
            string currentProviderName = Settings.apiProvider.ToString().Replace("_", " ");
            listing.Label($"Default Provider: {currentProviderName}", tooltip: "This provider is used as a fallback for unrouted queries and for testing below.");
            if (listing.ButtonText("Change Default Provider"))
            {
                var list = new System.Collections.Generic.List<FloatMenuOption>();
                foreach (ApiProvider provider in System.Enum.GetValues(typeof(ApiProvider)))
                {
                    ApiProvider localProvider = provider; // capture
                    string label = localProvider.ToString().Replace("_", " ");
                    list.Add(new FloatMenuOption(label, () =>
                    {
                        Settings.apiProvider = localProvider;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }

            listing.Gap(6f);

            if (listing.ButtonText("Test Connection (Default Provider)"))
            {
                RunTestConnection();
            }

            listing.Gap(6f);

            // ── Prompt Bench ─────────────────────────────────────────
            listing.Label("Prompt Bench",
                tooltip: "Test prompts against LM Studio. Use to tune speed and compare thinking on/off.");
            listing.GapLine();

            // Quick test: tell me a joke
            if (listing.ButtonText(_testBusy ? "Sending..." : "Quick Test: \"Tell me a joke\""))
            {
                if (!_testBusy)
                {
                    RunTestPrompt(
                        "You are a witty comedian. Reply with one short joke.",
                        "Tell me a joke.");
                }
            }

            // Custom prompt
            listing.Gap(4f);
            listing.Label("Custom Prompt:");
            _customPrompt = listing.TextEntry(_customPrompt, 2);
            listing.Gap(4f);

            if (listing.ButtonText(_testBusy ? "Sending..." : "Send Custom Prompt"))
            {
                if (!_testBusy && !string.IsNullOrWhiteSpace(_customPrompt))
                {
                    RunTestPrompt(
                        "You are a helpful assistant. Be concise.",
                        _customPrompt);
                }
            }

            // Status display
            if (!string.IsNullOrEmpty(_testStatus))
            {
                listing.Gap(6f);
                var prevColor = GUI.color;
                GUI.color = _testStatusColor;
                listing.Label(_testStatus);
                GUI.color = prevColor;
            }


            // ── Context Embedding ───────────────────────────────────
            listing.Label("Context Embedding",
                tooltip: "Inject game state (pawn data, colony, factions) into LLM requests. " +
                    "Configure prompts and weights via XML files in Defs/.");
            listing.GapLine();

            listing.CheckboxLabeled("Enable context embedding",
                ref Settings.enableContextEmbedding,
                "When enabled, Core assembles game state into a structured context block " +
                "and injects it into the system message of LLM requests. " +
                "Configure prompts in Defs/SynapsePrompts/, weights in Defs/SynapseWeights/, " +
                "and profiles in Defs/SynapseProfiles/.");

            if (Settings.enableContextEmbedding)
            {
                listing.Gap(4f);
                listing.Label("  Context is active. Edit XML files in the mod's Defs/ folder " +
                    "to customize prompts, weights, and event profiles.");
                listing.Label($"  Token budget adapts to LM Studio context window " +
                    $"({Internal.ModelManager.ContextLength?.ToString() ?? "unknown"} tokens).");
            }
            
            listing.Gap(4f);
            Settings.shortTermMemoryHours = listing.SliderLabeled(
                $"Short-Term Memory Window: {Settings.shortTermMemoryHours:F0} hours",
                Settings.shortTermMemoryHours, 24f, 168f,
                tooltip: "How long recent social interactions and events are kept in the LLM's context window.");

            listing.Gap(12f);

            // ── Advanced ─────────────────────────────────────────────
            listing.Label("Advanced", tooltip: "Sanitization, keep-alive, and logging.");
            listing.GapLine();

            if (listing.ButtonText("Open LLM Queue Monitor"))
            {
                Find.WindowStack.Add(new RimSynapse.UI.Dialog_QueueMonitor());
            }

            listing.Gap(6f);

            listing.CheckboxLabeled("Auto-map to active model",
                ref Settings.autoMapModel,
                "Automatically use the first loaded model in LM Studio.");

            // When auto-map is off, show model selector dropdown
            if (!Settings.autoMapModel)
            {
                string currentModel = string.IsNullOrEmpty(Settings.selectedModel)
                    ? "(none selected)" : Settings.selectedModel;

                if (listing.ButtonText($"Model: {currentModel}"))
                {
                    var modelIds = Internal.ModelManager.CachedModelIds;
                    if (modelIds.Count == 0)
                    {
                        // No cached models — trigger a refresh
                        _testStatus = "Fetching model list...";
                        _testStatusColor = Color.yellow;
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                Internal.HttpEngine.EnsureInitialized();
                                var result = Internal.HttpEngine.GetModelsSync();
                                if (result.online && result.modelIds.Count > 0)
                                {
                                    _testStatus = "Models loaded. Click the selector again.";
                                    _testStatusColor = Color.green;
                                    // Force cache update
                                    Internal.ModelManager.RefreshCache();
                                    Internal.ModelManager.GetModels(_ => { });
                                }
                                else
                                {
                                    _testStatus = "No models loaded in LM Studio.";
                                    _testStatusColor = Color.red;
                                }
                            }
                            catch (Exception ex)
                            {
                                _testStatus = $"Error: {ex.Message}";
                                _testStatusColor = Color.red;
                            }
                        });
                    }
                    else
                    {
                        // Build FloatMenu with available models
                        var options = new List<FloatMenuOption>();
                        foreach (var id in modelIds)
                        {
                            string modelId = id; // capture for closure
                            options.Add(new FloatMenuOption(modelId, () =>
                            {
                                Settings.selectedModel = modelId;
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                }
            }

            listing.CheckboxLabeled("Sanitize responses",
                ref Settings.sanitizeResponse,
                "Strip <think> blocks and repair broken JSON from LLM output.");

            listing.CheckboxLabeled("Enable keep-alive pings",
                ref Settings.enableKeepAlive,
                "Ping LM Studio every 4 minutes to prevent model unloading.");

            listing.CheckboxLabeled("Disable thinking/reasoning",
                ref Settings.disableThinking,
                "Prevent reasoning models from using chain-of-thought. Saves tokens and reduces latency.");

            listing.Gap(6f);

            listing.Gap(6f);
            listing.CheckboxLabeled("Enable LM Studio Trace Debug Mode",
                ref Settings.traceDebugMode,
                "Dumps the full JSON context sent to LM Studio into the standard developer console for troubleshooting.");

            listing.Gap(12f);
            listing.Label("DLC Context Testing", tooltip: "Simulate disabling DLCs for LLM context generation while they are physically loaded.");
            listing.GapLine();
            if (ModsConfig.IdeologyActive) listing.CheckboxLabeled("Include Ideology Context", ref Settings.testIdeologyActive);
            if (ModsConfig.RoyaltyActive) listing.CheckboxLabeled("Include Royalty Context", ref Settings.testRoyaltyActive);
            if (ModsConfig.BiotechActive) listing.CheckboxLabeled("Include Biotech Context", ref Settings.testBiotechActive);
            if (ModsConfig.AnomalyActive) listing.CheckboxLabeled("Include Anomaly Context", ref Settings.testAnomalyActive);

            listing.Gap(12f);

            // ── Opportunistic Tasks ─────────────────────────────────────
            listing.Label("Opportunistic Tasks",
                tooltip: "Controls how aggressively the mod fills idle GPU time with background AI tasks.\n" +
                    "Aggressive: Maximizes local LLM usage.\nConservative: Minimizes API costs.");
            listing.GapLine();

            // Throttle mode selector
            string[] modeLabels = { "Auto-Detect", "Aggressive (Local)", "Balanced", "Conservative (Paid API)" };
            int modeIndex = Settings.opportunisticThrottleMode + 1; // -1→0, 0→1, 1→2, 2→3
            listing.Label($"Throttle Mode: {modeLabels[Math.Max(0, Math.Min(modeIndex, 3))]}");
            if (listing.ButtonText("Cycle Throttle Mode"))
            {
                Settings.opportunisticThrottleMode++;
                if (Settings.opportunisticThrottleMode > 2) Settings.opportunisticThrottleMode = -1;
            }

            // Burst size (only relevant for Aggressive)
            listing.Label($"Burst Size (Aggressive mode): {Settings.opportunisticBurstSize}",
                tooltip: "How many background tasks can fire per idle check. Higher = more GPU usage.");
            Settings.opportunisticBurstSize = (int)listing.Slider(Settings.opportunisticBurstSize, 1, 5);

            // Per-task controls
            var tasks = Internal.OpportunisticTaskManager.GetTaskSnapshot();
            if (tasks.Count > 0)
            {
                listing.Gap(6f);
                listing.Label("Registered Tasks:");

                foreach (var task in tasks.OrderByDescending(t => t.Priority))
                {
                    listing.Gap(4f);
                    string enabledStr = task.Enabled ? "ON" : "OFF";
                    listing.Label($"  {task.Label}  [P{task.Priority}]  W:{task.BaseWeight:F1}  CD:{task.CooldownTicks}t  ({enabledStr})",
                        tooltip: task.Description);
                }
            }

            listing.Gap(12f);

            // ── Notifications ───────────────────────────────────────────
            listing.Label("Notifications", tooltip: "Control startup notifications.");
            listing.GapLine();

            listing.CheckboxLabeled("Show VRAM status on game load",
                ref Settings.showVramAdvisory,
                "Shows estimated GPU memory breakdown when the game starts.\n" +
                "Uncheck to disable (only shows if NVIDIA Tool is not installed).");

            listing.CheckboxLabeled("Show LLM Queue Monitor icon on toolbar",
                ref Settings.showQueueMonitorIcon,
                "Shows the AI queue monitor icon in the bottom right play settings toolbar.");

            listing.End();
            Widgets.EndScrollView();
        }

        // ── Helper Methods ───────────────────────────────────────────

        private void RunTestConnection()
        {
            _testStatus = "Connecting to LM Studio...";
            _testStatusColor = Color.yellow;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Internal.HttpEngine.EnsureInitialized();
                    var result = Internal.HttpEngine.GetModelsSync();

                    if (result.online)
                    {
                        string models = string.Join(", ", result.modelIds);
                        _testStatus = $"Online! Models: [{models}]";
                        if (result.contextLength.HasValue)
                            _testStatus += $" | Context: {result.contextLength.Value} tokens";
                        _testStatusColor = Color.green;
                    }
                    else
                    {
                        _testStatus = $"Offline: {result.error ?? "Could not reach LM Studio."}";
                        _testStatusColor = Color.red;
                    }
                }
                catch (Exception ex)
                {
                    _testStatus = $"Error: {ex.Message}";
                    _testStatusColor = Color.red;
                }
            });
        }

        private void RunTestPrompt(string systemPrompt, string userMessage)
        {
            _testBusy = true;
            string thinkingLabel = Settings.disableThinking ? "OFF" : "ON";
            _testStatus = $"Sending prompt (thinking: {thinkingLabel})...";
            _testStatusColor = Color.yellow;

            if (_testHandle == null)
                _testHandle = SynapseCore.Register("rimsynapse.test", "RimSynapse Test");

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

                    var result = Internal.HttpEngine.PostChatCompletionSync(
                        messages, ChatOptions.Default);

                    if (result.success)
                    {
                        string preview = result.content;
                        if (preview != null && preview.Length > 200)
                            preview = preview.Substring(0, 200) + "...";

                        _testStatus = $"[{result.durationMs}ms | {result.model} | " +
                            $"{result.promptTokens}p/{result.completionTokens}c tokens | " +
                            $"thinking: {thinkingLabel}]\n{preview}";
                        _testStatusColor = Color.green;
                        RimSynapse.SynapseLogger.Message($"[RimSynapse] Test: {result.content}");
                    }
                    else
                    {
                        _testStatus = $"Error: {result.error}";
                        _testStatusColor = Color.red;
                    }
                }
                catch (Exception ex)
                {
                    _testStatus = $"Error: {ex.Message}";
                    _testStatusColor = Color.red;
                }
                finally
                {
                    _testBusy = false;
                }
            });
        }
    }
}

