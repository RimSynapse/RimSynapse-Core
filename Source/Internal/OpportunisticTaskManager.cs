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
    /// Registration metadata for an opportunistic task.
    /// Companion mods pass this when registering to provide Core with all scheduling info.
    /// </summary>
    public class OpportunisticTaskConfig
    {
        /// <summary>Human-readable label shown in the Queue Monitor and Settings.</summary>
        public string Label = "Unknown Task";

        /// <summary>Tooltip description shown in Settings.</summary>
        public string Description = "";

        /// <summary>Higher priority tasks are checked first. Scale: 0 (disabled) to 10 (critical). Default: 5.</summary>
        public int Priority = 5;

        /// <summary>Base weight for weighted random selection among tasks at the same priority. Default: 1.0.</summary>
        public float Weight = 1.0f;

        /// <summary>Minimum in-game ticks between invocations. 60000 ≈ 1 day. Default: 15000 ≈ 6 hours.</summary>
        public int CooldownTicks = 15000;

        /// <summary>Whether this task starts enabled. Can be toggled in-game.</summary>
        public bool Enabled = true;
    }

    /// <summary>
    /// Centralized scheduler for low-priority background AI tasks.
    /// Companion mods register callbacks with metadata via C# — no Def system required.
    /// Core handles weighted selection, cooldown tracking, priority ordering,
    /// and throttle-aware scheduling.
    ///
    /// Third-party mods: just call SynapseClient.RegisterOpportunisticTask() with your
    /// callback and an OpportunisticTaskConfig. No XML or type dependencies needed.
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
            public string TaskId;
            public Action Callback;
            public Func<bool> CallbackBool;
            public OpportunisticTaskConfig Config;
            public int LastRunTick = -999999;
            public float EffectiveWeight;
            public int TimesRun;

            // Accessors through config
            public int Priority => Config?.Priority ?? 5;
            public float BaseWeight => Config?.Weight ?? 1.0f;
            public int CooldownTicks => Config?.CooldownTicks ?? 15000;
            public bool Enabled
            {
                get => Config?.Enabled ?? true;
                set { if (Config != null) Config.Enabled = value; }
            }
            public string Label => Config?.Label ?? TaskId;
            public string Description => Config?.Description ?? "";
        }

        private static readonly List<TaskEntry> _tasks = new List<TaskEntry>();
        private static readonly object _lock = new object();
        private static DateTime _lastIdleCheck = DateTime.MinValue;

        /// <summary>
        /// Read-only snapshot of all registered tasks for the Queue Monitor and Settings UI.
        /// </summary>
        public static List<TaskEntry> GetTaskSnapshot()
        {
            lock (_lock)
            {
                return _tasks.ToList();
            }
        }

        /// <summary>
        /// Registers an opportunistic task with full metadata that can return whether it performed work.
        /// If it returns true, it triggers a short burst cooldown to rapidly process remaining targets.
        /// If it returns false, it falls back to the full polling cooldown.
        /// </summary>
        public static void RegisterTask(SynapseModHandle mod, string taskId, Func<bool> callback, OpportunisticTaskConfig config = null)
        {
            lock (_lock)
            {
                var existing = _tasks.Find(t => t.TaskId == taskId);
                if (existing != null)
                {
                    existing.CallbackBool = callback;
                    existing.Callback = null;
                    existing.Mod = mod;
                    if (config != null) existing.Config = config;
                    return;
                }

                var entry = new TaskEntry
                {
                    Mod = mod,
                    TaskId = taskId,
                    CallbackBool = callback,
                    Config = config ?? new OpportunisticTaskConfig { Label = taskId },
                    EffectiveWeight = config?.Weight ?? 1.0f
                };

                _tasks.Add(entry);
                SynapseLogger.Info("core", $"Registered Opportunistic Task '{entry.Label}' for {mod.DisplayName} " +
                    $"(P{entry.Priority}, W{entry.BaseWeight:F1}, CD{entry.CooldownTicks}t, Dynamic)");
            }
        }

        /// <summary>
        /// Registers an opportunistic task with full metadata.
        /// This is the primary API — any mod can call this without needing XML Defs or type references.
        /// </summary>
        /// <param name="mod">Your mod handle from SynapseCore.Register().</param>
        /// <param name="taskId">Unique string ID for this task (e.g., "MyMod_BackstoryGen").</param>
        /// <param name="callback">The function to call when the queue is idle.</param>
        /// <param name="config">Scheduling metadata (priority, weight, cooldown, label). If null, sensible defaults are used.</param>
        public static void RegisterTask(SynapseModHandle mod, string taskId, Action callback, OpportunisticTaskConfig config = null)
        {
            lock (_lock)
            {
                var existing = _tasks.Find(t => t.TaskId == taskId);
                if (existing != null)
                {
                    existing.Callback = callback;
                    existing.CallbackBool = null;
                    existing.Mod = mod;
                    if (config != null) existing.Config = config;
                    return;
                }

                var entry = new TaskEntry
                {
                    Mod = mod,
                    TaskId = taskId,
                    Callback = callback,
                    Config = config ?? new OpportunisticTaskConfig { Label = taskId },
                    EffectiveWeight = config?.Weight ?? 1.0f
                };

                _tasks.Add(entry);
                SynapseLogger.Info("core", $"Registered Opportunistic Task '{entry.Label}' for {mod.DisplayName} " +
                    $"(P{entry.Priority}, W{entry.BaseWeight:F1}, CD{entry.CooldownTicks}t)");
            }
        }

        /// <summary>
        /// Legacy overload for backward compatibility.
        /// </summary>
        [Obsolete("Use RegisterTask(mod, taskId, callback, config) with an OpportunisticTaskConfig.")]
        public static void RegisterTask(SynapseModHandle mod, string taskId, Action callback, int cooldownTicks)
        {
            RegisterTask(mod, taskId, callback, new OpportunisticTaskConfig
            {
                Label = taskId,
                CooldownTicks = cooldownTicks
            });
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

                    // Apply token penalty
                    float avgTokens = 0f;
                    lock (RequestQueue.AvgTokensPerType)
                    {
                        if (RequestQueue.AvgTokensPerType.TryGetValue(task.Label, out float tokens))
                        {
                            avgTokens = tokens;
                        }
                    }
                    float tokenPenalty = Math.Max(1f, avgTokens / 100f);
                    task.EffectiveWeight /= tokenPenalty;
                }

                // Group eligible tasks by priority (highest first)
                var eligible = _tasks
                    .Where(t => t.Enabled && (t.Callback != null || t.CallbackBool != null) &&
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

                    SynapseLogger.Debug("queue", $"Opportunistic [{mode}]: Firing '{selected.Label}' " +
                        $"(P{selected.Priority}, W{selected.BaseWeight:F1}, #{selected.TimesRun})");

                    var callback = selected.Callback;
                    var callbackBool = selected.CallbackBool;
                    
                    SynapseGameComponent.Enqueue(() =>
                    {
                        try
                        {
                            bool didWork = false;
                            if (callbackBool != null)
                            {
                                didWork = callbackBool();
                            }
                            else if (callback != null)
                            {
                                callback();
                                didWork = true; // Legacy actions assume work was done
                            }
                            
                            if (didWork)
                            {
                                // The task found targets and performed work. It might have more targets.
                                // We reduce its cooldown significantly so it cycles to the next target quickly.
                                lock (_lock)
                                {
                                    int cooldown = (int)(selected.CooldownTicks * cooldownMultiplier);
                                    // Set LastRunTick so it expires in 250 ticks (4 seconds at 1x speed)
                                    selected.LastRunTick = currentTick - cooldown + 250;
                                }
                            }
                            // If !didWork, it didn't find any eligible pawns/targets. 
                            // It remains at currentTick, so it waits the full CooldownTicks before polling again.
                        }
                        catch (Exception ex)
                        {
                            SynapseLogger.Error($"Error executing opportunistic task '{selected.TaskId}': {ex.Message}");
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

        // ── Pause-time opportunistic task processing ──
        // Uses real-time (DateTime) cooldowns since game ticks are frozen while paused.

        private static readonly Dictionary<string, DateTime> _realTimeCooldowns = new Dictionary<string, DateTime>();

        /// <summary>
        /// Called by SynapseGameComponent during extended pauses.
        /// Uses real-time cooldowns instead of game ticks so tasks can fire while the game is paused.
        /// This lets the LLM generate backstories, faction histories, etc. during AFK time.
        /// </summary>
        internal static void TryRunPauseOpportunisticTask()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (RequestQueue.QueueDepth > 0) return; // Wait for queue to drain

            var settings = RimSynapseMod.Instance?.Settings;
            var mode = ResolveThrottleMode(settings);

            lock (_lock)
            {
                // Convert tick-based cooldowns to real-time: 60000 ticks ≈ 1 game day ≈ ~60 seconds at 1x
                // During pause, we use accelerated cooldowns: CooldownTicks / 1000 seconds (min 3s)
                var now = DateTime.UtcNow;

                var eligible = _tasks
                    .Where(t => t.Enabled && (t.Callback != null || t.CallbackBool != null))
                    .Where(t =>
                    {
                        if (_realTimeCooldowns.TryGetValue(t.TaskId, out DateTime lastRun))
                        {
                            float cooldownSeconds = Math.Max(3f, t.CooldownTicks / 1000f);
                            return (now - lastRun).TotalSeconds >= cooldownSeconds;
                        }
                        return true; // Never run during this pause session
                    })
                    .OrderByDescending(t => t.Priority)
                    .ToList();

                if (eligible.Count == 0) return;

                // Pick highest priority, then weighted random within that group
                int topPriority = eligible[0].Priority;
                var topGroup = eligible.Where(t => t.Priority == topPriority).ToList();

                // Calculate effective weights with token penalty
                var pauseWeights = new Dictionary<TaskEntry, float>();
                foreach (var task in topGroup)
                {
                    float avgTokens = 0f;
                    lock (RequestQueue.AvgTokensPerType)
                    {
                        if (RequestQueue.AvgTokensPerType.TryGetValue(task.Label, out float tokens))
                        {
                            avgTokens = tokens;
                        }
                    }
                    float tokenPenalty = Math.Max(1f, avgTokens / 100f);
                    pauseWeights[task] = task.BaseWeight / tokenPenalty;
                }

                float totalWeight = topGroup.Sum(t => Math.Max(0.01f, pauseWeights[t]));
                float roll = Rand.Range(0f, totalWeight);
                float cumulative = 0f;
                TaskEntry selected = topGroup.Last();

                foreach (var task in topGroup)
                {
                    cumulative += Math.Max(0.01f, pauseWeights[task]);
                    if (roll <= cumulative)
                    {
                        selected = task;
                        break;
                    }
                }

                _realTimeCooldowns[selected.TaskId] = now;
                selected.TimesRun++;

                SynapseLogger.Debug("queue", $"Pause-opportunistic [{mode}]: Firing '{selected.Label}' " +
                    $"(P{selected.Priority}, W{selected.BaseWeight:F1}, #{selected.TimesRun})");

                // During pause, we can execute directly on the main thread (we're in GameComponentUpdate)
                try
                {
                    bool didWork = false;
                    if (selected.CallbackBool != null)
                    {
                        didWork = selected.CallbackBool();
                    }
                    else if (selected.Callback != null)
                    {
                        selected.Callback();
                        didWork = true;
                    }

                    if (didWork)
                    {
                        // Accelerate next check for this task during pause (1 second)
                        _realTimeCooldowns[selected.TaskId] = now.AddSeconds(-Math.Max(3f, selected.CooldownTicks / 1000f) + 1.0);
                    }
                }
                catch (Exception ex)
                {
                    SynapseLogger.Error($"Error executing pause-opportunistic task '{selected.TaskId}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Clears real-time cooldowns. Called when the game is unpaused to reset pause-session state.
        /// </summary>
        internal static void ResetPauseCooldowns()
        {
            lock (_lock)
            {
                _realTimeCooldowns.Clear();
            }
        }
    }
}
