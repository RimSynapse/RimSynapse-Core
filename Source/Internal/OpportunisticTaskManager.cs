using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Throttle mode controls how aggressively the system fills idle GPU time.
    /// </summary>
    public enum OpportunisticThrottleMode
    {
        /// <summary>Auto-detect: Aggressive for localhost, Conservative for remote.</summary>
        Auto = -1,
        /// <summary>Local LLM: 0.5s idle delay, burst up to N tasks per check.</summary>
        Aggressive = 0,
        /// <summary>Middle ground: 3s idle delay, 1 task per check.</summary>
        Balanced = 1,
        /// <summary>Paid API: 15s idle delay, 1 task per check, doubled cooldowns.</summary>
        Conservative = 2
    }

    /// <summary>
    /// Centralized scheduler for low-priority background AI tasks.
    /// Companion mods register callbacks linked to XML-defined SynapseOpportunisticTaskDefs.
    /// The manager handles weighted selection, cooldown tracking, priority ordering,
    /// and throttle-aware scheduling.
    /// </summary>
    public static class OpportunisticTaskManager
    {
        /// <summary>
        /// Runtime state for a registered opportunistic task.
        /// Exposed publicly so the Queue Monitor can display it.
        /// </summary>
        public class TaskEntry
        {
            public SynapseModHandle Mod;
            public string DefName;
            public Action Callback;
            public int LastRunTick = -999999;
            public float EffectiveWeight;
            public int TimesRun;

            /// <summary>
            /// Linked Def (resolved after DefDatabase is ready). May be null
            /// if the companion mod registered before defs loaded.
            /// </summary>
            public SynapseOpportunisticTaskDef Def;

            // Derived properties (fall back to sensible defaults if Def is null)
            public int Priority => Def?.priority ?? 5;
            public float BaseWeight => Def?.weight ?? 1.0f;
            public int CooldownTicks => Def?.cooldownTicks ?? 15000;
            public bool Enabled => Def?.enabled ?? true;
            public string Label => Def?.label ?? DefName;
            public string Description => Def?.description ?? "";
        }

        private static readonly List<TaskEntry> _tasks = new List<TaskEntry>();
        private static readonly object _lock = new object();
        private static DateTime _lastIdleCheck = DateTime.MinValue;
        private static bool _defsResolved = false;

        /// <summary>
        /// Read-only snapshot of all registered tasks for the Queue Monitor UI.
        /// </summary>
        public static List<TaskEntry> GetTaskSnapshot()
        {
            lock (_lock)
            {
                return _tasks.ToList();
            }
        }

        /// <summary>
        /// Registers an opportunistic task linked to a SynapseOpportunisticTaskDef.
        /// The Def provides priority, weight, and cooldown. Only the callback is provided by C#.
        /// </summary>
        public static void RegisterTask(SynapseModHandle mod, string defName, Action callback)
        {
            lock (_lock)
            {
                var existing = _tasks.Find(t => t.DefName == defName);
                if (existing != null)
                {
                    existing.Callback = callback;
                    existing.Mod = mod;
                }
                else
                {
                    var entry = new TaskEntry
                    {
                        Mod = mod,
                        DefName = defName,
                        Callback = callback,
                        EffectiveWeight = 1.0f
                    };

                    // Try to resolve Def immediately (may fail if called before defs load)
                    TryResolveDef(entry);

                    _tasks.Add(entry);
                    SynapseLog.Info("core", $"Registered Opportunistic Task '{defName}' for {mod.DisplayName}");
                }
            }
        }

        /// <summary>
        /// Legacy overload for backward compatibility. Cooldown is used only if no Def exists.
        /// </summary>
        [Obsolete("Use RegisterTask(mod, defName, callback) instead. Cooldown should be defined in XML.")]
        public static void RegisterTask(SynapseModHandle mod, string taskId, Action callback, int cooldownTicks)
        {
            lock (_lock)
            {
                var existing = _tasks.Find(t => t.DefName == taskId);
                if (existing != null)
                {
                    existing.Callback = callback;
                    existing.Mod = mod;
                }
                else
                {
                    var entry = new TaskEntry
                    {
                        Mod = mod,
                        DefName = taskId,
                        Callback = callback,
                        EffectiveWeight = 1.0f
                    };

                    TryResolveDef(entry);

                    _tasks.Add(entry);
                    SynapseLog.Info("core", $"Registered Opportunistic Task '{taskId}' for {mod.DisplayName} (legacy, cooldown: {cooldownTicks})");
                }
            }
        }

        /// <summary>
        /// Called once after all Defs are loaded to link tasks to their XML definitions.
        /// </summary>
        public static void ResolveDefs()
        {
            lock (_lock)
            {
                foreach (var task in _tasks)
                {
                    TryResolveDef(task);
                }
                _defsResolved = true;
                SynapseLog.Info("core", $"Opportunistic Task Defs resolved. {_tasks.Count} tasks registered.");
            }
        }

        private static void TryResolveDef(TaskEntry entry)
        {
            if (entry.Def != null) return;
            entry.Def = DefDatabase<SynapseOpportunisticTaskDef>.GetNamedSilentFail(entry.DefName);
            if (entry.Def != null)
            {
                entry.EffectiveWeight = entry.Def.weight;
            }
        }

        /// <summary>
        /// Called by RequestQueue.WorkerLoop when the queue is completely empty.
        /// Selects and fires eligible tasks based on throttle mode, priority, and weight.
        /// </summary>
        internal static void TryRunOpportunisticTask()
        {
            var settings = RimSynapseMod.Instance?.Settings;
            var mode = ResolveThrottleMode(settings);

            // Adaptive idle delay based on throttle mode
            TimeSpan idleDelay;
            int burstSize;
            float cooldownMultiplier;

            switch (mode)
            {
                case OpportunisticThrottleMode.Aggressive:
                    idleDelay = TimeSpan.FromMilliseconds(500);
                    burstSize = settings?.opportunisticBurstSize ?? 3;
                    cooldownMultiplier = 1.0f;
                    break;
                case OpportunisticThrottleMode.Balanced:
                    idleDelay = TimeSpan.FromSeconds(3);
                    burstSize = 1;
                    cooldownMultiplier = 1.0f;
                    break;
                case OpportunisticThrottleMode.Conservative:
                    idleDelay = TimeSpan.FromSeconds(15);
                    burstSize = 1;
                    cooldownMultiplier = 2.0f;
                    break;
                default:
                    idleDelay = TimeSpan.FromSeconds(5);
                    burstSize = 1;
                    cooldownMultiplier = 1.0f;
                    break;
            }

            if (DateTime.UtcNow - _lastIdleCheck < idleDelay)
            {
                return;
            }
            _lastIdleCheck = DateTime.UtcNow;

            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return;
            }

            // Resolve defs if we haven't yet (handles late registration)
            if (!_defsResolved)
            {
                ResolveDefs();
            }

            int currentTick = Find.TickManager.TicksGame;
            int tasksFired = 0;

            lock (_lock)
            {
                // Update effective weights: recover linearly over cooldown period
                foreach (var task in _tasks)
                {
                    if (!task.Enabled) continue;

                    int ticksSinceRun = currentTick - task.LastRunTick;
                    int cooldown = (int)(task.CooldownTicks * cooldownMultiplier);

                    if (ticksSinceRun >= cooldown)
                    {
                        task.EffectiveWeight = task.BaseWeight;
                    }
                    else
                    {
                        // Linear recovery: 0 at fire time → BaseWeight at cooldown expiry
                        float recovery = (float)ticksSinceRun / Math.Max(1, cooldown);
                        task.EffectiveWeight = task.BaseWeight * recovery;
                    }
                }

                // Group eligible tasks by priority (highest first)
                var eligible = _tasks
                    .Where(t => t.Enabled && t.Callback != null &&
                           currentTick - t.LastRunTick >= (int)(t.CooldownTicks * cooldownMultiplier))
                    .OrderByDescending(t => t.Priority)
                    .ToList();

                if (eligible.Count == 0) return;

                // Fire up to burstSize tasks
                while (tasksFired < burstSize && eligible.Count > 0)
                {
                    // Among the highest priority group, select by weighted random
                    int topPriority = eligible[0].Priority;
                    var topGroup = eligible.Where(t => t.Priority == topPriority).ToList();

                    float totalWeight = topGroup.Sum(t => Math.Max(0.01f, t.EffectiveWeight));
                    float roll = Rand.Range(0f, totalWeight);
                    float cumulative = 0f;
                    TaskEntry selected = topGroup.Last();

                    foreach (var task in topGroup)
                    {
                        cumulative += Math.Max(0.01f, task.EffectiveWeight);
                        if (roll <= cumulative)
                        {
                            selected = task;
                            break;
                        }
                    }

                    // Fire it
                    selected.LastRunTick = currentTick;
                    selected.EffectiveWeight = 0f; // Decay to zero immediately
                    selected.TimesRun++;

                    SynapseLog.Debug("queue", $"Opportunistic [{mode}]: Firing '{selected.Label}' (P{selected.Priority}, W{selected.BaseWeight:F1}, #{selected.TimesRun})");

                    var callback = selected.Callback;
                    SynapseGameComponent.Enqueue(() =>
                    {
                        try
                        {
                            callback?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            SynapseLog.Error("core", $"Error executing opportunistic task '{selected.DefName}': {ex.Message}");
                        }
                    });

                    eligible.Remove(selected);
                    tasksFired++;
                }
            }
        }

        /// <summary>
        /// Resolves the effective throttle mode, handling Auto-detect.
        /// </summary>
        private static OpportunisticThrottleMode ResolveThrottleMode(RimSynapseSettings settings)
        {
            int modeInt = settings?.opportunisticThrottleMode ?? -1;

            if (modeInt >= 0 && modeInt <= 2)
            {
                return (OpportunisticThrottleMode)modeInt;
            }

            // Auto-detect: remote URL → Conservative, local → Aggressive
            if (settings != null && settings.IsRemoteUrl)
            {
                return OpportunisticThrottleMode.Conservative;
            }

            return OpportunisticThrottleMode.Aggressive;
        }
    }
}
