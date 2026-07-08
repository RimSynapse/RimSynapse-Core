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
        /// <summary>Minimum recommended free VRAM in GB (higher than NVIDIA Tool
        /// since our estimates are less precise than real NVML data).</summary>
        private const float MinFreeGb = 4.0f;

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
            // Windows 11 with Chrome/Discord easily uses 2-4 GB
            float systemEstimateGb = 2.5f;

            float estimatedUsedGb = lmEstimateGb + rwEstimateGb + systemEstimateGb;
            float estimatedFreeGb = totalGpuGb - estimatedUsedGb;

            SynapseLog.Info("core",
                $"VRAM estimate: {totalGpuGb:F1} GB total, " +
                $"~{lmEstimateGb:F1} GB model ({modelName ?? "none"}), " +
                $"~{rwEstimateGb:F1} GB RimWorld, " +
                $"~{systemEstimateGb:F1} GB system. " +
                $"Est. free: ~{estimatedFreeGb:F1} GB.");

            // Check user preference — default is to always show
            bool showNotify = RimSynapseMod.Instance?.Settings?.showVramAdvisory ?? true;

            if (!showNotify)
            {
                SynapseLog.Info("core", "VRAM advisory disabled in settings.");
                return;
            }

            ShowAdvisory(totalGpuGb, lmEstimateGb, estimatedFreeGb, modelName);
        }

        /// <summary>
        /// Show the VRAM status dialog. Informational by default,
        /// escalates to warning language if headroom is tight.
        /// </summary>
        private static void ShowAdvisory(float totalGb, float lmGb,
            float estFreeGb, string modelName)
        {
            bool isTight = estFreeGb < MinFreeGb && lmGb > 0f;

            string status = isTight
                ? $"⚠  Estimated {estFreeGb:F1} GB free — this may be tight for late-game."
                : lmGb > 0f
                    ? $"✓  Estimated {estFreeGb:F1} GB free — you should be fine."
                    : "No LLM model detected — VRAM estimate unavailable.";

            string suggestions = isTight
                ? "\n\nSuggestions:\n" +
                  "  • Use a smaller model in LM Studio (e.g., 7B instead of 12B)\n" +
                  "  • Reduce the context window size in LM Studio\n" +
                  "  • Close GPU-heavy background apps before playing"
                : "";

            string msg =
                "RimSynapse — GPU Memory Status\n\n" +
                $"GPU: {UnityEngine.SystemInfo.graphicsDeviceName}\n" +
                $"VRAM: {totalGb:F0} GB total\n\n" +
                (lmGb > 0f
                    ? $"  • LM Studio model ({modelName}):  ~{lmGb:F1} GB\n"
                    : "") +
                $"  • RimWorld (estimate):  ~1.0 GB\n" +
                $"  • System (estimate):    ~2.5 GB\n\n" +
                status +
                suggestions +
                "\n\nFor real-time GPU monitoring, install: RimSynapse NVIDIA Tool" +
                "\nDisable this notification in Mod Settings → RimSynapse Core.";

            LongEventHandler.QueueLongEvent(() =>
            {
                Find.WindowStack?.Add(new Dialog_MessageBox(
                    msg,
                    "OK",
                    null,
                    null,
                    null,
                    null,
                    false,
                    null,
                    null));
            }, null, false, null);

            if (isTight)
            {
                SynapseLog.Warn("core",
                    $"VRAM advisory: ~{estFreeGb:F1} GB estimated free " +
                    $"with {modelName ?? "unknown"} loaded on {totalGb:F0} GB GPU.");
            }
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
