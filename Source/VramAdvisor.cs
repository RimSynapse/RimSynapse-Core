using System;
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
        /// <summary>Max retries if LM Studio doesn't return a model.</summary>
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 3000;
        private const int InitialDelayMs = 5000;

        /// <summary>
        /// Whether the NVIDIA Tool companion mod handles VRAM breakdown.
        /// The "no model" check still runs from Core regardless.
        /// </summary>
        private static bool _nvidiaToolHandlesVram;

        internal static void Check()
        {
            if (_hasChecked) return;
            _hasChecked = true;

            // Track whether NVIDIA Tool handles the VRAM advisory —
            // but we ALWAYS check for LM Studio connectivity (no model = mod broken)
            _nvidiaToolHandlesVram = ModsConfig.IsActive("RimSynapse.NvidiaTool");

            if (_nvidiaToolHandlesVram)
            {
                SynapseLogger.Info("core",
                    "NVIDIA Tool mod detected — Core will defer VRAM breakdown " +
                    "but still check LM Studio connectivity.");
            }

            // Run the model query on a background thread with delay.
            // At startup, HttpEngine and LM Studio need a few seconds to be ready.
            System.Threading.Tasks.Task.Run(() =>
            {
                // Wait for things to settle
                System.Threading.Thread.Sleep(InitialDelayMs);
                RunCheck(0);
            });
        }

        /// <summary>
        /// Query LM Studio for the loaded model, with retry logic.
        /// Runs on a background thread to avoid blocking the main thread.
        /// </summary>
        private static void RunCheck(int attempt)
        {
            // SystemInfo must be read on the main thread — cache it early
            // Actually SystemInfo.graphicsMemorySize is thread-safe in Unity
            int totalGpuMb = SystemInfo.graphicsMemorySize;
            if (totalGpuMb <= 0) return;

            float totalGpuGb = totalGpuMb / 1024f;

            // Query LM Studio for the actual loaded model
            string modelName = null;
            try
            {
                Internal.HttpEngine.EnsureInitialized();
                var modelsResult = Internal.HttpEngine.GetModelsSync();
                Internal.ModelManager.UpdateCache(modelsResult);

                SynapseLogger.Message($"VRAM advisor (attempt {attempt + 1}): " +
                    $"online={modelsResult.online}, " +
                    $"models={modelsResult.modelIds.Count}, " +
                    $"error={modelsResult.error ?? "none"}");

                if (modelsResult.online && modelsResult.modelIds.Count > 0)
                {
                    modelName = modelsResult.modelIds[0];
                    SynapseLogger.Message($"VRAM advisor: live model from LM Studio: \"{modelName}\"");
                }
            }
            catch (Exception ex)
            {
                SynapseLogger.Warning($"VRAM advisor: could not query LM Studio (attempt {attempt + 1}): {ex.Message}");
            }

            // If no model found and retries remain, wait and try again
            if (string.IsNullOrEmpty(modelName) && attempt < MaxRetries)
            {
                SynapseLogger.Message($"VRAM advisor: no model found, retrying in {RetryDelayMs}ms...");
                System.Threading.Thread.Sleep(RetryDelayMs);
                RunCheck(attempt + 1);
                return;
            }

            bool showNotify = RimSynapseMod.Instance?.Settings?.showVramAdvisory ?? true;

            if (string.IsNullOrEmpty(modelName))
            {
                bool isRemote = RimSynapseMod.Instance?.Settings?.IsRemoteUrl ?? false;
                SynapseLogger.Warning("VRAM Advisor (Logging): No LLM model detected. LM Studio may not be running or has no model loaded.");
                
                if (!isRemote && showNotify)
                {
                    ShowNoModelWarning(totalGpuGb);
                }
                else if (isRemote)
                {
                    SynapseLogger.Warning("Remote LM Studio host is unreachable or has no model loaded. Popup suppressed.");
                }
                return;
            }
            // ── Model found — LM Studio is alive ──
            // Now decide whether to show the VRAM breakdown.
            // If NVIDIA Tool handles VRAM, or user disabled notifications, skip it.
            bool isRemoteHost = RimSynapseMod.Instance?.Settings?.IsRemoteUrl ?? false;

            float lmEstimateGb = EstimateModelVramGb(modelName);

            // RimWorld itself typically uses 0.5-1.5 GB VRAM
            float rwEstimateGb = 1.0f;

            // System/desktop overhead (DWM, compositor, background apps)
            // Windows 11 with Chrome/Discord easily uses 2-4 GB
            float systemEstimateGb = 2.5f;

            float estimatedUsedGb = lmEstimateGb + rwEstimateGb + systemEstimateGb;
            float estimatedFreeGb = totalGpuGb - estimatedUsedGb;

            SynapseLogger.Warning(
                $"VRAM Advisor (Logging): {totalGpuGb:F1} GB total, " +
                $"~{lmEstimateGb:F1} GB model ({modelName ?? "none"}), " +
                $"~{rwEstimateGb:F1} GB RimWorld, " +
                $"~{systemEstimateGb:F1} GB system. " +
                $"Est. free: ~{estimatedFreeGb:F1} GB.");

            if (!showNotify || _nvidiaToolHandlesVram || isRemoteHost)
            {
                return;
            }

            ShowAdvisory(totalGpuGb, lmEstimateGb, estimatedFreeGb, modelName);
        }

        /// <summary>
        /// Always shown if no model is detected — this is critical since
        /// the entire mod depends on LM Studio having a model loaded.
        /// Ignores the "show VRAM advisory" setting.
        /// </summary>
        private static void ShowNoModelWarning(float totalGpuGb)
        {
            string gpuName = SystemInfo.graphicsDeviceName ?? "Unknown GPU";

            string msg =
                "RimSynapse — No LLM Model Detected\n\n" +
                "RimSynapse could not connect to LM Studio or no model is loaded.\n" +
                "AI features (chat, psychology, storytelling) will not function.\n\n" +
                "To fix this:\n" +
                "  1. Download LM Studio: https://lmstudio.ai\n" +
                "  2. Install and open LM Studio\n" +
                "  3. Search for and download a model (recommended: gemma-4-12b)\n" +
                "  4. Go to the Local Server tab (left sidebar)\n" +
                "  5. Click \"Start Server\" — it should say Ready on port 1234\n\n" +
                "Quickstart guide:\n" +
                "  https://lmstudio.ai/docs/basics/server\n\n" +
                $"GPU: {gpuName}  •  VRAM: {totalGpuGb:F0} GB total\n\n" +
                "You can disable this popup in Mod Settings -> RimSynapse Core.";

            SynapseLogger.Warning("No LLM model detected. LM Studio may not be running or has no model loaded.");

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
        }

        /// <summary>
        /// Show the VRAM status dialog. Adapts messaging based on headroom
        /// and only suggests NVIDIA Tool for compatible GPU series.
        /// </summary>
        private static void ShowAdvisory(float totalGb, float lmGb,
            float estFreeGb, string modelName)
        {
            string gpuName = UnityEngine.SystemInfo.graphicsDeviceName ?? "Unknown GPU";
            bool isCritical = estFreeGb < 2.0f && lmGb > 0f;

            // ── Status line ──
            string status;
            if (lmGb <= 0f && string.IsNullOrEmpty(modelName))
                status = "No LLM model detected — VRAM estimate unavailable.";
            else if (lmGb <= 0f)
                status =
                    $"LM Studio model detected: {modelName}\n" +
                    "Could not estimate VRAM usage for this model.";
            else if (isCritical)
                status =
                    $"⚠  Estimated {estFreeGb:F1} GB free — below recommended 2 GB.\n" +
                    "Consider adjusting your system settings before loading a late-game save.";
            else
                status =
                    $"✓  Estimated {estFreeGb:F1} GB free — your system should be stable.\n" +
                    "See suggestions below if you experience VRAM-related issues.";

            // ── Suggestions ──
            string suggestions =
                "\n\nSuggestions if you experience issues:\n" +
                "  • Use a smaller model in LM Studio (e.g., 7B instead of 12B)\n" +
                "  • Reduce the context window size in LM Studio\n" +
                "  • Close GPU-heavy background apps (Chrome, Discord)";

            // ── NVIDIA Tool recommendation — only for compatible GPUs ──
            string nvToolLine = "";
            bool isRtx40or50 = IsRtx40or50Series(gpuName);

            if (isRtx40or50 && !ModsConfig.IsActive("RimSynapse.NvidiaTool"))
            {
                nvToolLine =
                    "\n\nYour " + gpuName + " supports the RimSynapse NVIDIA Tool\n" +
                    "companion mod for real-time GPU monitoring and detailed VRAM breakdown.";
            }
            else if (!isRtx40or50)
            {
                // Non-NVIDIA or older GPU — don't mention the tool at all
            }

            string msg =
                "RimSynapse — GPU Memory Status\n\n" +
                $"GPU: {gpuName}\n" +
                $"VRAM: {totalGb:F0} GB total\n\n" +
                (lmGb > 0f
                    ? $"  • LM Studio model ({modelName}):  ~{lmGb:F1} GB\n"
                    : "") +
                $"  • RimWorld (estimate):  ~1.0 GB\n" +
                $"  • System (estimate):    ~2.5 GB\n\n" +
                status +
                suggestions +
                nvToolLine +
                "\n\nDisable this notification in Mod Settings → RimSynapse Core.";

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

            if (isCritical)
            {
                SynapseLogger.Warn("core",
                    $"VRAM advisory: ~{estFreeGb:F1} GB estimated free " +
                    $"with {modelName ?? "unknown"} loaded on {totalGb:F0} GB GPU.");
            }
        }

        /// <summary>
        /// Detect if the GPU is an NVIDIA RTX 4000 or 5000 series
        /// (the supported cards for RimSynapse NVIDIA Tool).
        /// Uses Unity's SystemInfo.graphicsDeviceName which returns
        /// strings like "NVIDIA GeForce RTX 5070 Ti".
        /// </summary>
        private static bool IsRtx40or50Series(string gpuName)
        {
            if (string.IsNullOrEmpty(gpuName)) return false;

            string upper = gpuName.ToUpperInvariant();

            // Check for RTX 40xx series: 4060, 4070, 4080, 4090, etc.
            // Check for RTX 50xx series: 5060, 5070, 5080, 5090, etc.
            // Also covers Ti/Super variants since we just check the model number prefix
            if (!upper.Contains("NVIDIA") && !upper.Contains("GEFORCE"))
                return false;

            return Regex.IsMatch(upper, @"RTX\s*[45]0[5-9]0");
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
            // Match standard patterns: "12b", "7b", "70b", "3.8b", "0.5b"
            // Skip quantization markers: "q4b", "q8b"
            var match = Regex.Match(name, @"(?<![a-z])(\d+\.?\d*)b(?!\w)");
            if (match.Success)
            {
                float val;
                if (float.TryParse(match.Groups[1].Value, out val) && val > 0f)
                    return val;
            }

            // Handle MoE/expert notation: "e4b" = expert 4B, "a4b" = active 4B
            // These indicate the active parameter count for mixture-of-experts models
            // (e.g., gemma-4-e4b = 4B active expert parameters)
            var moeMatch = Regex.Match(name, @"[ea](\d+\.?\d*)b(?!\w)");
            if (moeMatch.Success)
            {
                float val;
                if (float.TryParse(moeMatch.Groups[1].Value, out val) && val > 0f)
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
