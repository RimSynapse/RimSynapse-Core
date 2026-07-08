using System;
using System.Collections.Generic;
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

        public RimSynapseMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RimSynapseSettings>();

            // Apply Harmony patches
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll();

            // Start background services (keep-alive, model discovery, HttpClient)
            // immediately — uses system timers, independent of game ticks.
            SynapseCore.Initialize();

            // Defer VRAM check until after all mods finish loading.
            // During the constructor, the HTTP client may not be ready and
            // ModelManager hasn't polled LM Studio yet.
            LongEventHandler.QueueLongEvent(() =>
            {
                VramAdvisor.Check();
            }, null, false, null);

            Log.Message("[RimSynapse] Core initialized. Harmony patches applied.");
        }

        public override string SettingsCategory() => "RimSynapse Core";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Scrollable container for all settings
            var viewRect = new Rect(0, 0, inRect.width - 20f, 900f);
            Widgets.BeginScrollView(inRect, ref _scrollPosition, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // ── Connection Settings ──────────────────────────────────
            listing.Label("Connection Settings", tooltip: "Configure your LM Studio connection.");
            listing.GapLine();

            Settings.lmStudioUrl = listing.TextEntryLabeled(
                "LM Studio URL:  ", Settings.lmStudioUrl);

            Settings.lmStudioApiKey = listing.TextEntryLabeled(
                "API Key (optional):  ", Settings.lmStudioApiKey);

            listing.Gap(6f);

            if (listing.ButtonText("Test Connection"))
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

            listing.Gap(12f);

            // ── Advanced ─────────────────────────────────────────────
            listing.Label("Advanced", tooltip: "Sanitization, keep-alive, and logging.");
            listing.GapLine();

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

            listing.Label($"Log Level: {Settings.logLevel}");
            if (listing.ButtonText($"Cycle: {Settings.logLevel}"))
            {
                int next = ((int)Settings.logLevel + 1) % 4;
                Settings.logLevel = (LogLevel)next;
            }

            listing.Gap(6f);
            listing.CheckboxLabeled("Enable file logging",
                ref Settings.enableFileLogging,
                "Dumps events and metrics to text files in the RimSynapse_Logs directory.");
            
            if (listing.ButtonText("Open Log Folder"))
            {
                string path = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "RimSynapse_Logs");
                if (!System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(path);
                }
                Application.OpenURL("file://" + path);
            }

            listing.Gap(12f);

            // ── Notifications ───────────────────────────────────────────
            listing.Label("Notifications", tooltip: "Control startup notifications.");
            listing.GapLine();

            listing.CheckboxLabeled("Show VRAM status on game load",
                ref Settings.showVramAdvisory,
                "Shows estimated GPU memory breakdown when the game starts.\n" +
                "Uncheck to disable (only shows if NVIDIA Tool is not installed).");

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
                        Log.Message($"[RimSynapse] Test: {result.content}");
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
