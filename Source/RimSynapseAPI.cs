using System;
using System.Collections.Generic;
using Verse;

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

        /// <summary>
        /// Programmatically executes a pre-built stateful Synapse script from JSON.
        /// </summary>
        /// <param name="scriptJson">The serialized JSON representation of a SynapseScript object.</param>
        /// <param name="logCallback">Optional callback to receive step execution and delay logs.</param>
        public static void ExecuteScript(string scriptJson, Action<string> logCallback = null)
        {
            if (string.IsNullOrEmpty(scriptJson)) return;
            try
            {
                var script = Newtonsoft.Json.JsonConvert.DeserializeObject<SynapseScript>(scriptJson);
                if (script != null)
                {
                    SynapseScriptRunner.StartScript(script, logCallback);
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"[API Error] Failed to execute script JSON: {ex.Message}");
            }
        }

        /// <summary>
        /// Registers a custom wait condition evaluator that can be checked by the script runner.
        /// </summary>
        /// <param name="conditionName">Unique identifier name for the condition (e.g. 'is_mentally_stable').</param>
        /// <param name="evaluator">The evaluation function returning true when condition is met.</param>
        public static void RegisterScriptWaitCondition(string conditionName, Func<Pawn, Dictionary<string, object>, bool> evaluator)
        {
            SynapseScriptRunner.RegisterWaitCondition(conditionName, evaluator);
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
