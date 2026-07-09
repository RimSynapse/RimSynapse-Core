using System;
using System.Collections.Generic;
using Verse;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Manages low-priority background AI tasks registered by companion mods.
    /// Tracks cooldowns globally (in real-time or ticks) and triggers them when the RequestQueue is idle.
    /// </summary>
    public static class OpportunisticTaskManager
    {
        private class OpportunisticTask
        {
            public SynapseModHandle Mod;
            public string TaskId;
            public Action Callback;
            public int CooldownTicks;
            public int LastRunTick = -999999;
        }

        private static readonly List<OpportunisticTask> _tasks = new List<OpportunisticTask>();
        private static readonly object _lock = new object();
        
        // Only attempt opportunistic runs every 5 real-time seconds when idle
        private static DateTime _lastIdleCheck = DateTime.MinValue;

        /// <summary>
        /// Registers a new opportunistic task.
        /// </summary>
        /// <param name="mod">The mod handle.</param>
        /// <param name="taskId">Unique ID for this task.</param>
        /// <param name="callback">The function to call when idle time is available.</param>
        /// <param name="cooldownTicks">How many in-game ticks must pass between invocations (e.g., 30000 for 12 hours).</param>
        public static void RegisterTask(SynapseModHandle mod, string taskId, Action callback, int cooldownTicks)
        {
            lock (_lock)
            {
                var existing = _tasks.Find(t => t.Mod == mod && t.TaskId == taskId);
                if (existing != null)
                {
                    existing.Callback = callback;
                    existing.CooldownTicks = cooldownTicks;
                }
                else
                {
                    _tasks.Add(new OpportunisticTask
                    {
                        Mod = mod,
                        TaskId = taskId,
                        Callback = callback,
                        CooldownTicks = cooldownTicks
                    });
                    SynapseLog.Info("core", $"Registered Opportunistic Task '{taskId}' for {mod.DisplayName} (Cooldown: {cooldownTicks} ticks)");
                }
            }
        }

        /// <summary>
        /// Called by RequestQueue.WorkerLoop when the queue is completely empty.
        /// Attempts to fire exactly one opportunistic task if cooldowns permit.
        /// </summary>
        internal static void TryRunOpportunisticTask()
        {
            if (DateTime.UtcNow - _lastIdleCheck < TimeSpan.FromSeconds(5))
            {
                return; // Don't spam checks
            }
            _lastIdleCheck = DateTime.UtcNow;

            // Ticks require RimWorld to be running and a map to exist
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            OpportunisticTask taskToRun = null;

            lock (_lock)
            {
                // Shuffle to prevent the first registered mod from hogging all idle time
                _tasks.Shuffle();

                foreach (var task in _tasks)
                {
                    if (currentTick - task.LastRunTick >= task.CooldownTicks)
                    {
                        taskToRun = task;
                        break;
                    }
                }
            }

            if (taskToRun != null)
            {
                SynapseLog.Debug("queue", $"Queue Idle. Triggering Opportunistic Task '{taskToRun.TaskId}' for {taskToRun.Mod.DisplayName}.");
                taskToRun.LastRunTick = currentTick;
                
                // Execute on main thread since it might touch game state to generate prompts
                SynapseGameComponent.Enqueue(() =>
                {
                    try
                    {
                        taskToRun.Callback?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        SynapseLog.Error("core", $"Error executing opportunistic task '{taskToRun.TaskId}': {ex.Message}");
                    }
                });
            }
        }
    }
}
