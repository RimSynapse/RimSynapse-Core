using System.Collections.Generic;
using RimSynapse.Internal;

namespace RimSynapse
{
    /// <summary>
    /// Core lifecycle and mod registration API.
    /// Consumer mods call <see cref="Register"/> during initialization.
    /// </summary>
    public static class SynapseCore
    {
        private static bool _initialized;

        /// <summary>All registered consumer mods.</summary>
        public static IReadOnlyList<SynapseModHandle> RegisteredMods => ModRegistry.All;

        /// <summary>
        /// Register a consumer mod with RimSynapse Core.
        /// Call once during your mod's initialization (e.g., in StaticConstructorOnStartup).
        /// Returns a handle used for all subsequent API calls.
        /// </summary>
        /// <param name="modId">Unique mod identifier (e.g., "rimsynapse.chat")</param>
        /// <param name="displayName">Human-readable name for settings UI (e.g., "RimSynapse Chat")</param>
        /// <param name="systemPrompt">Optional default system prompt for this mod.
        /// If null, Core will resolve a prompt from SynapsePromptDef XML based on event type.
        /// Can also be set/changed at runtime via SynapseModHandle.SystemPrompt.</param>
        public static SynapseModHandle Register(string modId, string displayName,
            string systemPrompt = null)
        {
            var handle = ModRegistry.Register(modId, displayName);
            if (!string.IsNullOrEmpty(systemPrompt))
                handle.SystemPrompt = systemPrompt;
            return handle;
        }

        /// <summary>
        /// Check if a companion mod is currently loaded and registered with Core.
        /// Use this for cross-mod coordination without direct type dependencies.
        /// E.g., StoryTeller checks if Psychology is loaded to decide whether to wait for leader backstories.
        /// </summary>
        /// <param name="modId">The mod's registration ID (e.g., "RimSynapsePsychology").</param>
        public static bool IsModLoaded(string modId)
        {
            return ModRegistry.Get(modId) != null;
        }

        /// <summary>
        /// Initialize all background services.
        /// Called from RimSynapseMod constructor — runs on mod load, NOT
        /// on game load, so keep-alive and model discovery work even on
        /// the main menu.
        /// </summary>
        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            HttpEngine.EnsureInitialized();
            KeepAlive.Start();

            // Initial model check and asset cleanup on a background thread
            System.Threading.Tasks.Task.Run(() =>
            {
                try { SynapseImageClient.CleanupOrphanedAssets(); } catch (System.Exception ex) { SynapseLogger.Error($"Cleanup failed: {ex}"); }

                var result = HttpEngine.GetModelsSync();
                ModelManager.UpdateCache(result);

                if (result.online)
                {
                    SynapseLogger.Message($"LM Studio online. Models: [{string.Join(", ", result.modelIds)}]" +
                        (result.contextLength.HasValue
                            ? $" Context: {result.contextLength.Value} tokens"
                            : ""));
                }
                else
                {
                    SynapseLogger.Warn("core",
                        $"LM Studio offline: {result.error ?? "unknown error"}. " +
                        $"Ensure LM Studio is running at {RimSynapseMod.Instance?.Settings?.lmStudioUrl ?? "http://127.0.0.1:1234"}.");
                }
            });
        }

        /// <summary>
        /// Clean shutdown. Disposes HTTP client, stops timers, flushes queue.
        /// Called by Harmony patch on Root.OnDestroy.
        /// </summary>
        internal static void Shutdown()
        {
            SynapseLogger.Message("RimSynapse Core shutting down.");

            KeepAlive.Shutdown();
            RequestQueue.Shutdown();
            ModelManager.Shutdown();
            HttpEngine.Shutdown();
            ModRegistry.Shutdown();

            _initialized = false;
        }
    }
}
