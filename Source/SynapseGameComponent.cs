using System;
using System.Collections.Concurrent;
using Verse;

namespace RimSynapse
{
    /// <summary>
    /// GameComponent that bridges async background work back to Unity's main thread.
    /// Processes queued callbacks during the game tick loop.
    /// </summary>
    public class SynapseGameComponent : GameComponent
    {
        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        /// <summary>Max callbacks to process per tick to avoid frame drops.</summary>
        private const int MaxCallbacksPerTick = 5;

        public SynapseGameComponent(Game game) { }

        /// <summary>
        /// Enqueue an action to run on the main thread during the next game tick.
        /// Thread-safe — can be called from any background thread.
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;
            _mainThreadQueue.Enqueue(action);
        }

        /// <summary>
        /// Called every game tick on the main thread.
        /// Processes up to <see cref="MaxCallbacksPerTick"/> queued callbacks.
        /// </summary>
        public override void GameComponentTick()
        {
            int processed = 0;
            while (processed < MaxCallbacksPerTick && _mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimSynapse] Callback error: {ex}");
                }
                processed++;
            }
        }

        /// <summary>
        /// Called when a game is loaded.
        /// Background services are already running (started in mod constructor).
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            SynapseLog.Debug("core", "Game loaded. Main-thread dispatcher active.");
        }
    }
}
