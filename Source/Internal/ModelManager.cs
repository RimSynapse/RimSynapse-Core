using System;
using System.Collections.Generic;
using System.Threading;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Manages LM Studio model discovery, caching, and auto-mapping.
    /// Caches model list for 30 seconds to avoid excessive API calls.
    /// </summary>
    internal static class ModelManager
    {
        private static ModelsResult _cachedResult;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
        private static readonly object _cacheLock = new object();

        /// <summary>Last known active model ID.</summary>
        internal static string ActiveModel { get; private set; }

        /// <summary>Last known context window size.</summary>
        internal static int? ContextLength { get; private set; }

        /// <summary>Whether LM Studio was reachable on the last check.</summary>
        internal static bool IsOnline => _cachedResult?.online ?? false;

        /// <summary>Cached model IDs from the last successful fetch.</summary>
        internal static List<string> CachedModelIds =>
            _cachedResult?.modelIds ?? new List<string>();

        /// <summary>
        /// Get the model ID to use for a request. If autoMapModel is enabled and
        /// no specific model is requested, returns the first loaded model.
        /// </summary>
        internal static string ResolveModel(string requestedModel)
        {
            // Explicit per-request model takes priority
            if (!string.IsNullOrEmpty(requestedModel))
                return requestedModel;

            var settings = RimSynapseMod.Instance?.Settings;
            if (settings == null)
                return requestedModel;

            if (settings.autoMapModel)
            {
                // Auto-map: use first loaded model
                if (!string.IsNullOrEmpty(ActiveModel))
                    return ActiveModel;
            }
            else
            {
                // Manual: use user-selected model
                if (!string.IsNullOrEmpty(settings.selectedModel))
                    return settings.selectedModel;
            }

            return requestedModel;
        }

        /// <summary>
        /// Fetch models from LM Studio (using cache if fresh).
        /// Callback runs on the main thread.
        /// </summary>
        internal static void GetModels(Action<ModelsResult> callback)
        {
            lock (_cacheLock)
            {
                if (_cachedResult != null && DateTime.UtcNow < _cacheExpiry)
                {
                    var cached = _cachedResult;
                    SynapseGameComponent.Enqueue(() => callback(cached));
                    return;
                }
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                var result = HttpEngine.GetModelsSync();

                lock (_cacheLock)
                {
                    UpdateCache(result);
                }

                SynapseGameComponent.Enqueue(() => callback(result));
            });
        }

        /// <summary>
        /// Update the cached model state from a synchronous HTTP result.
        /// Call this when performing synchronous model checks during startup or VRAM advisory.
        /// </summary>
        internal static void UpdateCache(ModelsResult result)
        {
            lock (_cacheLock)
            {
                _cachedResult = result;
                _cacheExpiry = DateTime.UtcNow + CacheDuration;

                if (result.online && result.modelIds.Count > 0)
                {
                    string previousModel = ActiveModel;
                    ActiveModel = result.modelIds[0];
                    ContextLength = result.contextLength;

                    if (previousModel != ActiveModel)
                    {
                        SynapseLogger.Info("model",
                            $"Active model: \"{ActiveModel}\"" +
                            (ContextLength.HasValue
                                ? $" (context: {ContextLength.Value} tokens)"
                                : ""));
                    }
                }
                else
                {
                    ActiveModel = null;
                    ContextLength = null;
                }
            }
        }

        /// <summary>
        /// Refresh the model cache. Called periodically by keep-alive.
        /// </summary>
        internal static void RefreshCache()
        {
            GetModels(_ => { });
        }

        /// <summary>
        /// Clear all cached state on shutdown.
        /// </summary>
        internal static void Shutdown()
        {
            lock (_cacheLock)
            {
                _cachedResult = null;
                _cacheExpiry = DateTime.MinValue;
                ActiveModel = null;
                ContextLength = null;
            }
        }
    }
}
