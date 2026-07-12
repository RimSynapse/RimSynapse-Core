using System;
using System.Threading;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Background keep-alive ping timer. Sends a minimal 1-token request
    /// to LM Studio every 4 minutes to prevent model unloading.
    /// </summary>
    internal static class KeepAlive
    {
        private static Timer _timer;
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(4);

        /// <summary>
        /// Start the keep-alive timer. Safe to call multiple times.
        /// </summary>
        internal static void Start()
        {
            var settings = RimSynapseMod.Instance?.Settings;
            if (settings == null || !settings.enableKeepAlive) return;

            if (_timer != null) return;

            _timer = new Timer(OnTick, null, Interval, Interval);
            SynapseLogger.Message("Keep-alive timer started (every 4 minutes).");
        }

        /// <summary>
        /// Stop the keep-alive timer.
        /// </summary>
        internal static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private static void OnTick(object state)
        {
            var settings = RimSynapseMod.Instance?.Settings;
            if (settings == null || !settings.enableKeepAlive)
            {
                Stop();
                return;
            }

            string model = ModelManager.ActiveModel;
            if (string.IsNullOrEmpty(model))
            {
                // No active model — try refreshing
                ModelManager.RefreshCache();
                return;
            }

            HttpEngine.SendKeepAlivePing(model);
        }

        /// <summary>
        /// Shutdown alias.
        /// </summary>
        internal static void Shutdown() => Stop();
    }
}
