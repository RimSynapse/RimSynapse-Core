using System;
using System.Collections.Concurrent;
using Verse;

namespace RimSynapse
{
    /// <summary>
    /// GameComponent that bridges async background work back to Unity's main thread.
    /// Processes queued callbacks every Unity frame (including while paused) via GameComponentUpdate.
    /// Also detects pause state and triggers opportunistic tasks during extended pauses.
    /// </summary>
    public class SynapseGameComponent : GameComponent
    {
        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        /// <summary>Max callbacks to process per frame to avoid frame drops.</summary>
        private const int MaxCallbacksPerFrame = 5;

        /// <summary>Real-time tracking for pause-based opportunistic task firing.</summary>
        private static DateTime _pauseStartTime = DateTime.MinValue;
        private static bool _wasPaused = false;
        private static bool _pauseOpportunisticFired = false;
        private static DateTime _lastPauseOpportunisticCheck = DateTime.MinValue;

        /// <summary>How long the game must be paused before opportunistic tasks start firing (seconds).</summary>
        private const float PauseIdleThreshold = 5.0f;

        /// <summary>Interval between opportunistic task checks during pause (seconds).</summary>
        private const float PauseCheckInterval = 2.0f;

        public SynapseGameComponent(Game game) { }

        /// <summary>
        /// Enqueue an action to run on the main thread during the next frame.
        /// Thread-safe — can be called from any background thread.
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;
            _mainThreadQueue.Enqueue(action);
        }

        /// <summary>
        /// Clears the main thread callback queue.
        /// </summary>
        public static void ClearMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Clears both the background LLM queue and the main thread callback queue.
        /// </summary>
        public static void ClearAllQueues()
        {
            ClearMainThreadQueue();
            Internal.RequestQueue.Clear();
        }

        /// <summary>
        /// Called every Unity frame on the main thread — even while paused.
        /// Processes queued callbacks and handles pause-time opportunistic task firing.
        /// </summary>
        public override void GameComponentUpdate()
        {
            // Process callbacks from the queue (LLM results, log dispatch, etc.)
            int processed = 0;
            while (processed < MaxCallbacksPerFrame && _mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    RimSynapse.SynapseLogger.Error($"[RimSynapse] Callback error: {ex}");
                }
                processed++;
            }

            // ── Pause-time opportunistic task handling ──
            if (Find.TickManager == null) return;

            bool isPaused = Find.TickManager.Paused;

            if (isPaused)
            {
                if (!_wasPaused)
                {
                    // Just entered pause — start the timer
                    _pauseStartTime = DateTime.UtcNow;
                    _pauseOpportunisticFired = false;
                    _wasPaused = true;
                }

                double pausedSeconds = (DateTime.UtcNow - _pauseStartTime).TotalSeconds;
                if (pausedSeconds >= PauseIdleThreshold)
                {
                    // We've been paused long enough — fire opportunistic tasks periodically
                    if ((DateTime.UtcNow - _lastPauseOpportunisticCheck).TotalSeconds >= PauseCheckInterval)
                    {
                        _lastPauseOpportunisticCheck = DateTime.UtcNow;

                        if (!_pauseOpportunisticFired)
                        {
                            SynapseLogger.Message("Pause detected for 5+ seconds — enabling pause-time opportunistic processing.");
                            _pauseOpportunisticFired = true;
                        }

                        // Tell the task manager to run using real-time cooldowns
                        Internal.OpportunisticTaskManager.TryRunPauseOpportunisticTask();
                    }
                }
            }
            else
            {
                if (_wasPaused && _pauseOpportunisticFired)
                {
                    SynapseLogger.Message("Game unpaused — resuming tick-based opportunistic scheduling.");
                }
                _wasPaused = false;
                _pauseOpportunisticFired = false;
            }
        }

        private bool _vramChecked = false;

        /// <summary>
        /// Called every game tick on the main thread (only while unpaused).
        /// </summary>
        public override void GameComponentTick()
        {
            if (!_vramChecked)
            {
                _vramChecked = true;
                VramAdvisor.Check();
            }
        }

        /// <summary>
        /// Called when a game is loaded.
        /// Background services are already running (started in mod constructor).
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            SynapseLogger.Message("Game loaded. Main-thread dispatcher active (frame-based, pause-aware).");
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            ClearAllQueues();
            SynapseLogger.Message("Started new game. Queues cleared.");
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            ClearAllQueues();
            SynapseLogger.Message("Loaded game. Queues cleared.");
        }
    }
}

