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

        // Scrollable content
        private static Vector2 _scrollPosition;
        private static float _viewHeight = 1500f;

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
                
                ModHandle.RegisterQueryType("storyteller_pacing", "Storyteller: Pacing", LlmCapabilities.Text);
                ModHandle.RegisterQueryType("storyteller_event_selection", "Storyteller: Event Selection", LlmCapabilities.Text);

                SynapseToolRegistry.EnsureInitialized();
                SynapseTemplateRegistry.Initialize();
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
            var viewRect = new Rect(0, 0, inRect.width - 20f, _viewHeight);
            Widgets.BeginScrollView(inRect, ref _scrollPosition, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // ── Main UI Navigation ──────────────────────────────────
            var prevColor = GUI.color;
            GUI.color = new Color(0.9f, 0.45f, 0.15f); // Orange

            if (listing.ButtonText("Customize LLM Providers"))
            {
                Find.WindowStack.Add(new RimSynapse.UI.Dialog_ProviderSettings());
            }
            listing.Gap(4f);
            if (listing.ButtonText("Map Context to Models"))
            {
                Find.WindowStack.Add(new RimSynapse.UI.Dialog_QueryRouting());
            }

            GUI.color = prevColor;
            listing.Gap(12f);


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

            if (listing.ButtonText("Open Test Bench"))
            {
                Find.WindowStack.Add(new RimSynapse.UI.Dialog_TestBench());
            }

            if (listing.ButtonText("Open God Mode Window"))
            {
                Find.WindowStack.Add(new RimSynapse.UI.Dialog_GodMode());
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
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                Internal.HttpEngine.EnsureInitialized();
                                var result = Internal.HttpEngine.GetModelsSync();
                                if (result.online && result.modelIds.Count > 0)
                                {
                                    // Force cache update
                                    Internal.ModelManager.RefreshCache();
                                    Internal.ModelManager.GetModels(_ => { });
                                }
                                else
                                {
                                }
                            }
                            catch (Exception ex)
                            {
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
            
            Settings.audioBoost = listing.SliderLabeled(
                $"TTS Audio PCM Boost: {Settings.audioBoost:F1}x",
                Settings.audioBoost, 1.0f, 4.0f,
                tooltip: "Directly boosts the PCM waveform amplitude. Helpful for quiet AI-generated TTS voices.");

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
            _viewHeight = listing.CurHeight;
            Widgets.EndScrollView();
        }

    }
}







