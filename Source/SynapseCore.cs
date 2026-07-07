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
        public static SynapseModHandle Register(string modId, string displayName)
        {
            return ModRegistry.Register(modId, displayName);
        }

        /// <summary>
        /// Called when a game is loaded. Starts background services.
        /// Triggered by <see cref="SynapseGameComponent.FinalizeInit"/>.
        /// </summary>
        internal static void OnGameLoaded()
        {
            if (_initialized) return;
            _initialized = true;

            HttpEngine.EnsureInitialized();
            KeepAlive.Start();

            // Initial model check
            ModelManager.GetModels(result =>
            {
                if (result.online)
                {
                    SynapseLog.Info("core",
                        $"LM Studio online. Models: [{string.Join(", ", result.modelIds)}]" +
                        (result.contextLength.HasValue
                            ? $" Context: {result.contextLength.Value} tokens"
                            : ""));
                }
                else
                {
                    SynapseLog.Warn("core",
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
            SynapseLog.Info("core", "RimSynapse Core shutting down.");

            KeepAlive.Shutdown();
            RequestQueue.Shutdown();
            ModelManager.Shutdown();
            HttpEngine.Shutdown();
            ModRegistry.Shutdown();

            _initialized = false;
        }
    }
}
