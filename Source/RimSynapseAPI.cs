using System;
using System.Collections.Generic;

namespace RimSynapse
{
    /// <summary>
    /// Public programmatic API for RimSynapse.
    /// Exposes natural language action planning, script execution, and tool execution features to other mods.
    /// </summary>
    public static class RimSynapseAPI
    {
        /// <summary>
        /// Programmatically executes a natural language command using the Synapse LLM resolver.
        /// Resolves the command into sequential tool calls or stateful scripts over a multi-turn agent loop.
        /// </summary>
        /// <param name="command">The command in plain English (e.g. 'Sarah equips a sniper rifle and defends the colony').</param>
        /// <param name="logCallback">Optional callback to receive status and execution logs.</param>
        /// <param name="onComplete">Optional callback triggered when the execution completes or fails. Returns (success, finalSummary).</param>
        public static void ExecuteNaturalLanguageCommand(string command, Action<string> logCallback = null, Action<bool, string> onComplete = null)
        {
            if (string.IsNullOrEmpty(command))
            {
                onComplete?.Invoke(false, "Command is null or empty.");
                return;
            }

            // Sanitize newlines and line breaks to prevent JSON serialization/provider exceptions
            command = command.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();

            logCallback?.Invoke($"> Request: {command}");

            var planner = new SynapseLlmPlanner(command, logCallback, onComplete);
            planner.Start();
        }
    }

    public class GodModeResponse
    {
        public List<GodModeCall> calls;
    }

    public class GodModeCall
    {
        public string tool;
        public Dictionary<string, object> arguments;
    }
}
