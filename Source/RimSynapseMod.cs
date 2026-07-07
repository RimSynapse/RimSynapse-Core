using System;
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

        private const string HarmonyId = "archDukeJim.rimsynapseCore";

        public RimSynapseMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RimSynapseSettings>();

            // Apply Harmony patches
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll();

            Log.Message("[RimSynapse] Core initialized. Harmony patches applied.");
        }

        public override string SettingsCategory() => "RimSynapse Core";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // --- Connection Section ---
            listing.Label("Connection Settings", tooltip: "Configure your LM Studio connection.");
            listing.GapLine();

            Settings.lmStudioUrl = listing.TextEntryLabeled(
                "LM Studio URL:  ", Settings.lmStudioUrl);

            Settings.lmStudioApiKey = listing.TextEntryLabeled(
                "API Key (optional):  ", Settings.lmStudioApiKey);

            listing.Gap(6f);

            // --- Performance Section ---
            listing.Label("Performance", tooltip: "Control request rate and timeouts.");
            listing.GapLine();

            listing.Label($"Timeout: {Settings.timeoutSeconds}s");
            Settings.timeoutSeconds = (int)listing.Slider(
                Settings.timeoutSeconds, 10f, 300f);

            listing.Label($"Max Requests Per Minute: {Settings.maxRequestsPerMinute}");
            Settings.maxRequestsPerMinute = (int)listing.Slider(
                Settings.maxRequestsPerMinute, 1f, 120f);

            listing.Gap(6f);

            // --- Mod Budget Sliders ---
            var registeredMods = SynapseCore.RegisteredMods;
            if (registeredMods != null && registeredMods.Count > 0)
            {
                listing.Label("Mod Query Budgets",
                    tooltip: "Percentage of total query capacity allocated to each mod.");
                listing.GapLine();

                for (int i = 0; i < registeredMods.Count; i++)
                {
                    var handle = registeredMods[i];
                    listing.Label($"{handle.DisplayName}: {handle.QueryBudgetPercent:F0}%");
                    handle.QueryBudgetPercent = listing.Slider(
                        handle.QueryBudgetPercent, 0f, 100f);
                }

                listing.Gap(6f);
            }

            // --- Advanced Section ---
            listing.Label("Advanced", tooltip: "Sanitization, keep-alive, and logging.");
            listing.GapLine();

            listing.CheckboxLabeled("Auto-map to active model",
                ref Settings.autoMapModel,
                "Automatically use the first loaded model in LM Studio.");

            listing.CheckboxLabeled("Sanitize responses",
                ref Settings.sanitizeResponse,
                "Strip <think> blocks and repair broken JSON from LLM output.");

            listing.CheckboxLabeled("Enable keep-alive pings",
                ref Settings.enableKeepAlive,
                "Ping LM Studio every 4 minutes to prevent model unloading.");

            listing.Gap(6f);

            listing.Label($"Log Level: {Settings.logLevel}");
            if (listing.ButtonText($"Cycle: {Settings.logLevel}"))
            {
                int next = ((int)Settings.logLevel + 1) % 4;
                Settings.logLevel = (LogLevel)next;
            }

            listing.End();
        }
    }
}
