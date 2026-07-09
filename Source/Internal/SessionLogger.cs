using System;
using System.IO;
using Verse;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Manages the physical log files for RimSynapse.
    /// Hooks into SynapseLog to write to disk if enabled in settings.
    /// </summary>
    public static class SessionLogger
    {
        private static string currentLogFilePath;
        private static string currentColonyName;
        private static readonly object fileLock = new object();

        public static void Initialize()
        {
            SynapseLog.OnLog += HandleLog;
        }

        public static void Shutdown()
        {
            SynapseLog.OnLog -= HandleLog;
        }

        private static void HandleLog(LogEntry entry)
        {
            if (RimSynapseMod.Instance?.Settings?.enableSessionLogging != true) return;

            string colonyName = "UnknownColony";
            if (Current.ProgramState == ProgramState.Playing && Find.World?.info != null)
            {
                colonyName = Find.World.info.name ?? "UnnamedColony";
            }
            else if (Current.ProgramState == ProgramState.MapInitializing)
            {
                colonyName = "GeneratingWorld";
            }
            else
            {
                colonyName = "MainMenu";
            }

            // Remove invalid path chars
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                colonyName = colonyName.Replace(c.ToString(), "");
            }

            lock (fileLock)
            {
                if (currentColonyName != colonyName || currentLogFilePath == null)
                {
                    TransitionLogFile(colonyName);
                }

                if (currentLogFilePath != null)
                {
                    try
                    {
                        File.AppendAllText(currentLogFilePath, entry.ToString() + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        Verse.Log.Warning($"[RimSynapse] Failed to write to session log: {ex.Message}");
                    }
                }
            }
        }

        private static void TransitionLogFile(string newColonyName)
        {
            string logDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimSynapse_Logs");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string newFileName = $"{newColonyName}_{timestamp}.log";
            string newFilePath = Path.Combine(logDir, newFileName);

            if (currentLogFilePath != null && File.Exists(currentLogFilePath))
            {
                try
                {
                    File.AppendAllText(currentLogFilePath, $"--- Logs continued in: {newFileName} ---" + Environment.NewLine);
                }
                catch { } // Ignore errors closing old file
            }

            currentColonyName = newColonyName;
            currentLogFilePath = newFilePath;

            try
            {
                File.WriteAllText(currentLogFilePath, $"--- RimSynapse Session Log Started: {DateTime.Now} ---" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Verse.Log.Warning($"[RimSynapse] Failed to create new session log file: {ex.Message}");
                currentLogFilePath = null;
            }
        }

        public static string GetCurrentLogContent()
        {
            lock (fileLock)
            {
                if (currentLogFilePath == null || !File.Exists(currentLogFilePath)) return "No active session log found.";
                try
                {
                    return File.ReadAllText(currentLogFilePath);
                }
                catch (Exception ex)
                {
                    return $"Error reading log file: {ex.Message}";
                }
            }
        }
    }
}
