using System;
using System.IO;
using Verse;

namespace RimSynapse.Utils
{
    public static class SynapseFileLogger
    {
        private static string GetLogDirectory(string moduleName)
        {
            return Path.Combine(GenFilePaths.SaveDataFolderPath, "RimSynapse_Logs", moduleName);
        }

        public static void LogEvent(string moduleName, Pawn pawn, string eventType, string details)
        {
            if (RimSynapseMod.Instance == null || RimSynapseMod.Instance.Settings == null || !RimSynapseMod.Instance.Settings.enableSessionLogging) return;

            try
            {
                string logDirectory = GetLogDirectory(moduleName);
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string safePawnName = pawn?.Name?.ToStringShort ?? "System";
                // Sanitize filename just in case
                safePawnName = string.Join("_", safePawnName.Split(Path.GetInvalidFileNameChars()));
                
                string filePath = Path.Combine(logDirectory, $"{safePawnName}_Log.txt");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                
                string logLine = $"[{timestamp}] [{safePawnName}] [{eventType}] - {details}\n";
                
                File.AppendAllText(filePath, logLine);
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLog.Error("core", $"[RimSynapse-{moduleName}] Failed to write debug log: {ex.Message}");
            }
        }

        public static void LogMetric(string moduleName, Pawn pawn, string processName, long elapsedMilliseconds)
        {
            if (RimSynapseMod.Instance == null || RimSynapseMod.Instance.Settings == null || !RimSynapseMod.Instance.Settings.enableSessionLogging) return;

            try
            {
                string logDirectory = GetLogDirectory(moduleName);
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string safePawnName = pawn != null ? (pawn.Name?.ToStringShort ?? "System") : "System";
                safePawnName = string.Join("_", safePawnName.Split(Path.GetInvalidFileNameChars()));
                
                string filePath = Path.Combine(logDirectory, $"{safePawnName}_PerformanceMetrics.txt");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                
                string logLine = $"[{timestamp}] [{processName}] Execution Time: {elapsedMilliseconds}ms\n";
                
                File.AppendAllText(filePath, logLine);
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLog.Error("core", $"[RimSynapse-{moduleName}] Failed to write metric log: {ex.Message}");
            }
        }
    }
}

