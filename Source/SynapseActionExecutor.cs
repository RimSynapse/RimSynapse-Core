using System;
using System.Collections.Generic;
using Verse;
using Newtonsoft.Json;
using RimSynapse.Models;

namespace RimSynapse
{
    /// <summary>
    /// Processes LLM responses, extracts JSON payloads, and schedules script or tool call runs on RimWorld's main loop.
    /// </summary>
    public static class SynapseActionExecutor
    {
        public static void ProcessResponse(
            SynapseLlmPlanner planner, 
            List<ChatMessage> messages, 
            string responseContent, 
            ChatOptions options, 
            Action<string> logCallback, 
            Action<bool, string> onComplete)
        {
            string json = RimSynapse.Utils.JsonHelper.ExtractJson(responseContent);

            if (string.IsNullOrEmpty(json))
            {
                logCallback?.Invoke($"[Assistant] {responseContent}");
                onComplete?.Invoke(true, responseContent);
                return;
            }

            messages.Add(ChatMessage.Assistant(responseContent));

            // 1. Try to run as a stateful script
            if (TryExecuteScript(planner, messages, json, options, logCallback, onComplete))
            {
                return;
            }

            // 2. Fallback: Parse and run flat calls sequentially
            ExecuteFlatCalls(planner, messages, json, options, logCallback, onComplete);
        }

        private static bool TryExecuteScript(
            SynapseLlmPlanner planner, 
            List<ChatMessage> messages, 
            string json, 
            ChatOptions options, 
            Action<string> logCallback, 
            Action<bool, string> onComplete)
        {
            try
            {
                var script = JsonConvert.DeserializeObject<SynapseScript>(json);
                if (script != null && !string.IsNullOrEmpty(script.scriptName) && script.steps != null && script.steps.Count > 0)
                {
                    SynapseGameComponent.Enqueue(() =>
                    {
                        var scriptLog = new List<string>();
                        SynapseScriptRunner.StartScript(script, msg =>
                        {
                            logCallback?.Invoke(msg);
                            scriptLog.Add(msg);
                        }, () =>
                        {
                            string scriptOutcome = string.Join("\n", scriptLog);
                            messages.Add(ChatMessage.User($"Script execution finished. Logs:\n{scriptOutcome}\n\nPlease review and either generate next tool calls/script, or respond with a final summary if done."));
                            planner.RunAgentLoop(options);
                        });
                    });
                    return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        private static void ExecuteFlatCalls(
            SynapseLlmPlanner planner, 
            List<ChatMessage> messages, 
            string json, 
            ChatOptions options, 
            Action<string> logCallback, 
            Action<bool, string> onComplete)
        {
            GodModeResponse response = null;
            try
            {
                response = JsonConvert.DeserializeObject<GodModeResponse>(json);
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"[Error] Failed to deserialize calls: {ex.Message}");
                onComplete?.Invoke(false, $"Failed to deserialize calls: {ex.Message}");
                return;
            }

            if (response == null || response.calls == null || response.calls.Count == 0)
            {
                messages.Add(ChatMessage.User("Execution outcomes of the action plan:\n[] (No actions resolved)\n\nPlease review and either generate next tool calls, or respond with a final summary if done."));
                planner.RunAgentLoop(options);
                return;
            }

            SynapseGameComponent.Enqueue(() =>
            {
                var outcomes = new List<object>();

                foreach (var call in response.calls)
                {
                    logCallback?.Invoke($"[Executing] Call: {call.tool} with args: {JsonConvert.SerializeObject(call.arguments)}");
                    string outcome = "";
                    try
                    {
                        string argsJson = JsonConvert.SerializeObject(call.arguments);
                        outcome = SynapseToolRegistry.ExecuteTool(call.tool, argsJson);
                        logCallback?.Invoke($"[Result] {outcome}");
                    }
                    catch (Exception ex)
                    {
                        outcome = $"{{\"success\": false, \"reason\": \"Execution threw exception: {ex.Message}\"}}";
                        logCallback?.Invoke($"[Error] {outcome}");
                    }

                    outcomes.Add(new
                    {
                        tool = call.tool,
                        arguments = call.arguments,
                        result = outcome
                    });
                }

                string outcomesJson = JsonConvert.SerializeObject(outcomes);
                string followUpPrompt = $@"Execution outcomes of the action plan:
{outcomesJson}

Please review the results. If you need to perform more actions, output a new JSON calls block or script.
If everything is done, output your final summary.";

                messages.Add(ChatMessage.User(followUpPrompt));
                planner.RunAgentLoop(options);
            });
        }
    }
}
