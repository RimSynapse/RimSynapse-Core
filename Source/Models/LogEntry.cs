using System;

namespace RimSynapse
{
    /// <summary>
    /// Log verbosity levels.
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
    }

    /// <summary>
    /// A structured log entry emitted by RimSynapse Core.
    /// Subscribe to <see cref="SynapseLog.OnLog"/> to receive these.
    /// </summary>
    public class LogEntry
    {
        /// <summary>Severity level.</summary>
        public LogLevel level;

        /// <summary>Component category: "client", "queue", "sanitize", "model", "keepalive", "registry".</summary>
        public string category;

        /// <summary>Human-readable message.</summary>
        public string message;

        /// <summary>Which registered mod triggered this (null for internal events).</summary>
        public string modId;

        /// <summary>When the log entry was created.</summary>
        public DateTime timestamp;

        public LogEntry(LogLevel level, string category, string message, string modId = null)
        {
            this.level = level;
            this.category = category;
            this.message = message;
            this.modId = modId;
            this.timestamp = DateTime.UtcNow;
        }

        public override string ToString()
        {
            string prefix = modId != null ? $"[{modId}]" : "";
            return $"[RimSynapse][{level}][{category}]{prefix} {message}";
        }
    }
}
