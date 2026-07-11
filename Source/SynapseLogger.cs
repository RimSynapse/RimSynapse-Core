using System;
using Verse;
using System.Text;

namespace RimSynapse
{
    /// <summary>
    /// A thin, thread-safe wrapper around Vanilla RimWorld logging (Verse.Log).
    /// Prevents Unity from crashing when background tasks attempt to log.
    /// </summary>
    public static class SynapseLogger
    {
        private const string Prefix = "[RimSynapse]";

        public static void Message(string msg, string category = null, string modId = null)
        {
            string prefix = category != null ? $"{Prefix}[{category}]" : Prefix;
            SafeExecute(() => Log.Message($"{prefix} {msg}"));
        }

        public static void Info(string msg, string category = null, string modId = null) => Message(msg, category, modId);
        public static void Debug(string msg, string category = null, string modId = null) => Message(msg, category, modId);

        public static void Warning(string msg, string category = null, string modId = null)
        {
            string prefix = category != null ? $"{Prefix}[{category}]" : Prefix;
            SafeExecute(() => Log.Warning($"{prefix} {msg}"));
        }

        public static void Warn(string msg, string category = null, string modId = null) => Warning(msg, category, modId);

        public static void Error(string msg, string category = null, string modId = null)
        {
            string prefix = category != null ? $"{Prefix}[{category}]" : Prefix;
            SafeExecute(() => Log.Error($"{prefix} {msg}"));
        }

        /// <summary>
        /// Dumps the raw LMStudio context to the RimWorld trace if Debug Mode is enabled.
        /// </summary>
        public static void TraceContext(string requestJson, string url)
        {
            if (RimSynapseMod.Instance == null) return;
            if (!RimSynapseMod.Instance.Settings.traceDebugMode) return;

            var sb = new StringBuilder();
            sb.AppendLine("========== LMSTUDIO TRACE DUMP ==========");
            sb.AppendLine($"URL: {url}");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine("----- Request Payload -----");
            sb.AppendLine(requestJson);
            sb.AppendLine("=========================================");

            SafeExecute(() => Log.Message($"{Prefix}\n{sb.ToString()}"));
        }

        private static void SafeExecute(Action action)
        {
            if (UnityData.IsInMainThread)
            {
                action();
            }
            else
            {
                // Enqueue to execute on the main thread next tick
                SynapseGameComponent.Enqueue(action);
            }
        }
    }

    [Obsolete("Use SynapseLogger instead.")]
    public static class SynapseLog
    {
        public static void Info(string category, string message) => SynapseLogger.Info(message, category);
        public static void Warn(string category, string message) => SynapseLogger.Warning(message, category);
        public static void Error(string category, string message) => SynapseLogger.Error(message, category);
    }
}
