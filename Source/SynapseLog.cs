using System;

namespace RimSynapse
{
    /// <summary>
    /// Structured logging API for RimSynapse Core.
    /// Consumer mods and developers can subscribe to <see cref="OnLog"/>
    /// to receive all internal log events.
    /// </summary>
    public static class SynapseLog
    {
        /// <summary>
        /// Subscribe to receive all RimSynapse log events.
        /// Useful for developer tools, debug overlays, or custom dashboards.
        /// </summary>
        public static event Action<LogEntry> OnLog;

        /// <summary>
        /// Current log level. Messages below this level are discarded.
        /// Configurable in mod settings.
        /// </summary>
        public static LogLevel Level
        {
            get
            {
                var settings = RimSynapseMod.Instance?.Settings;
                return settings?.logLevel ?? LogLevel.Info;
            }
            set
            {
                var settings = RimSynapseMod.Instance?.Settings;
                if (settings != null) settings.logLevel = value;
            }
        }

        public static void Debug(string category, string message, string modId = null)
            => Emit(LogLevel.Debug, category, message, modId);

        public static void Info(string category, string message, string modId = null)
            => Emit(LogLevel.Info, category, message, modId);

        public static void Warn(string category, string message, string modId = null)
            => Emit(LogLevel.Warn, category, message, modId);

        public static void Error(string category, string message, string modId = null)
            => Emit(LogLevel.Error, category, message, modId);

        private static void Emit(LogLevel level, string category, string message, string modId)
        {
            if (level < Level) return;

            var entry = new LogEntry(level, category, message, modId);

            if (Verse.UnityData.IsInMainThread)
            {
                DispatchAndNotify(level, entry);
            }
            else
            {
                SynapseGameComponent.Enqueue(() => DispatchAndNotify(level, entry));
            }
        }

        private static void DispatchAndNotify(LogLevel level, LogEntry entry)
        {
            // Always log to RimWorld's dev console
            switch (level)
            {
                case LogLevel.Error:
                    Verse.Log.Error(entry.ToString());
                    break;
                case LogLevel.Warn:
                    Verse.Log.Warning(entry.ToString());
                    break;
                default:
                    Verse.Log.Message(entry.ToString());
                    break;
            }

            // Notify subscribers
            try
            {
                OnLog?.Invoke(entry);
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[RimSynapse] Log subscriber threw: {ex}");
            }
        }
    }
}
