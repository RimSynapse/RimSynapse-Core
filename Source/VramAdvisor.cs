using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace RimSynapse
{
    /// <summary>
    /// Lightweight VRAM awareness check for the Core mod.
    /// Uses only Unity's built-in APIs — no NVIDIA dependency, fully vendor-agnostic.
    ///
    /// On first game load, estimates whether the player has enough GPU memory
    /// headroom for both RimWorld and their loaded LLM model. If headroom is
    /// tight, shows a non-blocking notification with suggestions.
    ///
    /// For detailed real-time GPU monitoring, points users to the companion
    /// mod RimSynapse NVIDIA Tool.
    ///
    /// NOT a GameComponent — zero save-file footprint. Safe to add/remove.
    /// </summary>
    internal static class VramAdvisor
    {
        /// <summary>Minimum recommended free VRAM in GB.</summary>
        private const float MinFreeGb = 2.0f;

        /// <summary>Only check once per game session.</summary>
        private static bool _hasChecked;

        /// <summary>
        /// Called once during mod startup (StaticConstructorOnStartup).
        /// Uses Unity's SystemInfo to estimate VRAM headroom.
        /// </summary>
        internal static void Check()
        {
            if (_hasChecked) return;
            _hasChecked = true;

            // If the NVIDIA Tool companion mod is installed, it has a much
            // better VRAM warning system with real-time NVML data. Skip ours.
            if (ModsConfig.IsActive("archDukeJim.rimsynapseNvidiaTool"))
            {
                SynapseLog.Info("core",
                    "NVIDIA Tool mod detected — skipping Core VRAM advisory " +
                    "(detailed diagnostics available through NVIDIA Tool).");
                return;
            }

            // SystemInfo.graphicsMemorySize = total GPU memory in MB (all vendors)
            int totalGpuMb = SystemInfo.graphicsMemorySize;
            if (totalGpuMb <= 0) return;

            float totalGpuGb = totalGpuMb / 1024f;

            // Estimate LLM VRAM from selected model name
            string modelName = RimSynapseMod.Instance?.Settings?.selectedModel;
            float lmEstimateGb = EstimateModelVramGb(modelName);

            // RimWorld itself typically uses 0.5-1.5 GB VRAM
            float rwEstimateGb = 1.0f;

            // System/desktop overhead (DWM, compositor, background apps)
            float systemEstimateGb = 1.5f;

            float estimatedUsedGb = lmEstimateGb + rwEstimateGb + systemEstimateGb;
            float estimatedFreeGb = totalGpuGb - estimatedUsedGb;

            SynapseLog.Info("core",
                $"VRAM estimate: {totalGpuGb:F1} GB total, " +
                $"~{lmEstimateGb:F1} GB model ({modelName ?? "none"}), " +
                $"~{rwEstimateGb:F1} GB RimWorld, " +
                $"~{systemEstimateGb:F1} GB system. " +
                $"Est. free: ~{estimatedFreeGb:F1} GB.");

            if (estimatedFreeGb < MinFreeGb && lmEstimateGb > 0f)
            {
                ShowAdvisory(totalGpuGb, lmEstimateGb, estimatedFreeGb, modelName);
            }
        }

        /// <summary>
        /// Show a non-blocking advisory dialog about VRAM headroom.
        /// </summary>
        private static void ShowAdvisory(float totalGb, float lmGb,
            float estFreeGb, string modelName)
        {
            string msg =
                "RimSynapse — GPU Memory Advisory\n\n" +
                $"Your GPU has {totalGb:F0} GB total VRAM.\n" +
                $"Your loaded model ({modelName ?? "unknown"}) is estimated to use ~{lmGb:F1} GB.\n\n" +
                $"Estimated free VRAM for late-game: ~{estFreeGb:F1} GB\n" +
                $"Recommended minimum: {MinFreeGb:F0} GB\n\n" +
                "This may cause stuttering in late-game with large colonies.\n\n" +
                "Suggestions:\n" +
                "  • Use a smaller model in LM Studio (e.g., 7B instead of 12B)\n" +
                "  • Reduce the context window size in LM Studio\n" +
                "  • Close GPU-heavy background apps before playing\n\n" +
                "For real-time GPU monitoring, install the companion mod:\n" +
                "  RimSynapse NVIDIA Tool";

            // Queue it for when the game UI is ready
            LongEventHandler.QueueLongEvent(() =>
            {
                Find.WindowStack?.Add(new Dialog_MessageBox(
                    msg,
                    "Got it",
                    null,
                    null,
                    null,
                    null,
                    false,
                    null,
                    null));
            }, null, false, null);

            SynapseLog.Warn("core",
                $"VRAM advisory: ~{estFreeGb:F1} GB estimated free " +
                $"with {modelName ?? "unknown"} loaded on {totalGb:F0} GB GPU. " +
                $"Consider a smaller model or reducing context window.");
        }

        /// <summary>
        /// Estimate VRAM usage for an LLM model based on its name.
        /// Parses parameter count (e.g., "12b", "7b") and applies
        /// Q4_K_M quantization estimate.
        /// Returns 0 if no model is selected or name can't be parsed.
        /// </summary>
        internal static float EstimateModelVramGb(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return 0f;

            float billions = ParseBillionParams(modelName.ToLowerInvariant());
            if (billions <= 0f) return 0f;

            // Q4_K_M quantization: ~0.65 GB per billion params
            // + ~0.5 GB KV cache / runtime overhead
            return (billions * 0.65f) + 0.5f;
        }

        /// <summary>
        /// Parse billion parameter count from model name string.
        /// </summary>
        private static float ParseBillionParams(string name)
        {
            // Match: "12b", "7b", "70b", "3.8b", "0.5b"
            // Skip: "q4b", "a4b", "e4b" (quantization/expert markers)
            var match = Regex.Match(name, @"(?<![a-z])(\d+\.?\d*)b(?!\w)");
            if (match.Success)
            {
                float val;
                if (float.TryParse(match.Groups[1].Value, out val) && val > 0f)
                    return val;
            }

            // Fallback by name keywords
            if (name.Contains("mini")) return 3.8f;
            if (name.Contains("small")) return 7f;
            if (name.Contains("medium")) return 13f;
            if (name.Contains("large")) return 34f;

            return 0f;
        }
    }
}
