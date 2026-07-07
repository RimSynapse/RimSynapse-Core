using System;
using System.Collections.Generic;
using System.Linq;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Tracks registered consumer mods and their query budget allocations.
    /// Session-scoped — rebuilt each game load from mod registrations.
    /// </summary>
    internal static class ModRegistry
    {
        private static readonly Dictionary<string, SynapseModHandle> _mods =
            new Dictionary<string, SynapseModHandle>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new object();

        /// <summary>All registered mods (read-only snapshot).</summary>
        internal static IReadOnlyList<SynapseModHandle> All
        {
            get
            {
                lock (_lock)
                {
                    return _mods.Values.ToList().AsReadOnly();
                }
            }
        }

        /// <summary>Number of registered mods.</summary>
        internal static int Count
        {
            get
            {
                lock (_lock) { return _mods.Count; }
            }
        }

        /// <summary>
        /// Register a consumer mod. If already registered, returns existing handle.
        /// Auto-rebalances budget percentages on new registrations.
        /// </summary>
        internal static SynapseModHandle Register(string modId, string displayName)
        {
            if (string.IsNullOrEmpty(modId))
                throw new ArgumentException("modId cannot be null or empty.", nameof(modId));

            lock (_lock)
            {
                if (_mods.TryGetValue(modId, out var existing))
                {
                    SynapseLog.Debug("registry",
                        $"Mod \"{displayName}\" ({modId}) already registered. Returning existing handle.");
                    return existing;
                }

                var handle = new SynapseModHandle(modId, displayName ?? modId);
                _mods[modId] = handle;

                // Auto-rebalance: equal split across all registered mods
                RebalanceBudgets();

                SynapseLog.Info("registry",
                    $"Registered mod \"{displayName}\" ({modId}). " +
                    $"Total mods: {_mods.Count}. Budget: {handle.QueryBudgetPercent:F0}%.");

                return handle;
            }
        }

        /// <summary>
        /// Get a mod handle by ID.
        /// </summary>
        internal static SynapseModHandle Get(string modId)
        {
            lock (_lock)
            {
                _mods.TryGetValue(modId, out var handle);
                return handle;
            }
        }

        /// <summary>
        /// Reset all request window counters. Called periodically by the queue.
        /// </summary>
        internal static void ResetWindowCounters()
        {
            lock (_lock)
            {
                foreach (var handle in _mods.Values)
                {
                    handle.WindowRequestCount = 0;
                }
            }
        }

        /// <summary>
        /// Check if a mod is within its budget for the current window.
        /// </summary>
        internal static bool IsWithinBudget(SynapseModHandle handle, int maxRequestsPerWindow)
        {
            if (handle == null) return false;
            int modMax = Math.Max(1, (int)(maxRequestsPerWindow * handle.QueryBudgetPercent / 100f));
            return handle.WindowRequestCount < modMax;
        }

        /// <summary>
        /// Record a request against a mod's budget.
        /// </summary>
        internal static void RecordRequest(SynapseModHandle handle)
        {
            if (handle == null) return;
            handle.RequestCount++;
            handle.WindowRequestCount++;
        }

        /// <summary>
        /// Auto-rebalance budgets equally across all registered mods.
        /// Users can manually adjust via settings UI sliders afterward.
        /// </summary>
        private static void RebalanceBudgets()
        {
            if (_mods.Count == 0) return;
            float perMod = 100f / _mods.Count;
            foreach (var handle in _mods.Values)
            {
                handle.QueryBudgetPercent = perMod;
            }
        }

        /// <summary>
        /// Clear all registrations on shutdown.
        /// </summary>
        internal static void Shutdown()
        {
            lock (_lock)
            {
                _mods.Clear();
            }
        }
    }
}
