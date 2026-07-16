using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Newtonsoft.Json;
using RimSynapse.Comps;
using RimSynapse.Models;

namespace RimSynapse.UI
{
    public class Dialog_GodMode : Window
    {
        private static string _commandInput = "Make Fred have 20 Shooting, remove the Bipolar trait from Sarah, and start a fire on the solar generator.";
        private static string _statusText = "Ready";
        private static Color _statusColor = Color.white;
        private static bool _busy = false;
        private static string _selectedRoutingId = RimSynapse.RoutingId.LocalOnly;
        private static List<string> _executionLog = new List<string>();
        private Vector2 _logScrollPosition = Vector2.zero;

        public Dialog_GodMode()
        {
            this.forcePause = false;
            this.doCloseX = true;
            this.doCloseButton = true;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = false;
            this.resizeable = true;
            this.draggable = true;
            this.preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(650f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "Synapse God Mode (LLM Direct Action Console)");
            Text.Font = GameFont.Small;

            float curY = 40f;

            // Description
            Widgets.Label(new Rect(0f, curY, inRect.width, 40f), 
                "Type a command in plain English. The Storyteller LLM will translate it into tool calls to execute changes on pawns or objects directly in-game.");
            curY += 45f;

            // Target Provider Selector
            Rect providerRect = new Rect(0f, curY, 300f, 30f);
            if (Widgets.ButtonText(providerRect, $"Target Provider: {_selectedRoutingId}"))
            {
                var list = new List<FloatMenuOption>();
                list.Add(new FloatMenuOption(RoutingId.LocalOnly, () => _selectedRoutingId = RoutingId.LocalOnly));
                list.Add(new FloatMenuOption(RoutingId.Jan, () => _selectedRoutingId = RoutingId.Jan));
                list.Add(new FloatMenuOption("OpenAI", () => _selectedRoutingId = RoutingId.OpenAI));
                list.Add(new FloatMenuOption(RoutingId.Gemini, () => _selectedRoutingId = RoutingId.Gemini));
                Find.WindowStack.Add(new FloatMenu(list));
            }
            curY += 35f;

            // Text Area for input command
            Widgets.Label(new Rect(0f, curY, inRect.width, 20f), "Command Input (Press [Enter] to submit):");
            curY += 22f;
            
            Rect inputRect = new Rect(0f, curY, inRect.width, 40f);

            // Listen for Enter key press on the input box
            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                if (!_busy && !string.IsNullOrEmpty(_commandInput.Trim()))
                {
                    ExecuteCommand();
                    Event.current.Use(); // Consume the event to prevent adding a newline
                }
            }

            _commandInput = Widgets.TextArea(inputRect, _commandInput);
            curY += 45f;

            // Execute Button
            Rect execRect = new Rect(0f, curY, 150f, 35f);
            if (!_busy)
            {
                if (Widgets.ButtonText(execRect, "Execute Command"))
                {
                    ExecuteCommand();
                }
            }
            else
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(execRect, "Processing...", false, false, false);
                GUI.color = Color.white;
            }

            // Clear Log Button
            Rect clearLogRect = new Rect(165f, curY, 120f, 35f);
            if (Widgets.ButtonText(clearLogRect, "Clear Log"))
            {
                _executionLog.Clear();
            }

            // Status label next to buttons
            Rect statusRect = new Rect(295f, curY + 8f, inRect.width - 300f, 25f);
            GUI.color = _statusColor;
            Widgets.Label(statusRect, _statusText);
            GUI.color = Color.white;
            curY += 45f;

            // Scrollable execution log
            Widgets.Label(new Rect(0f, curY, inRect.width, 20f), "Execution Log:");
            curY += 22f;

            float remainingHeight = inRect.height - curY - 55f; // Leave space for close button
            Rect outRect = new Rect(0f, curY, inRect.width, remainingHeight);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, Math.Max(remainingHeight, _executionLog.Count * 22f + 10f));

            Widgets.BeginScrollView(outRect, ref _logScrollPosition, viewRect);
            float logY = 0f;
            foreach (var line in _executionLog)
            {
                Widgets.Label(new Rect(0f, logY, viewRect.width, 20f), line);
                logY += 22f;
            }
            Widgets.EndScrollView();
        }

        private void ExecuteCommand()
        {
            if (string.IsNullOrEmpty(_commandInput)) return;

            _busy = true;
            _statusText = "Analyzing command...";
            _statusColor = new Color(0.7f, 0.7f, 1f);
            _executionLog.Add($"> Request: {_commandInput}");

            // Assemble tool definitions schema
            var toolsSchema = new System.Text.StringBuilder();
            foreach (var tool in SynapseToolRegistry.AllTools)
            {
                toolsSchema.AppendLine($"- Tool: \"{tool.name}\"");
                toolsSchema.AppendLine($"  Description: {tool.description}");
                toolsSchema.AppendLine($"  Arguments schema: {JsonConvert.SerializeObject(tool.parameters)}");
                toolsSchema.AppendLine();
            }

            string systemPrompt = $@"You are the RimWorld Synapse God Mode action resolver.
The user will input an instruction in plain English. Your job is to translate this instruction into a sequence of tool/C# function calls to change the game state.
You have access to the following tools:

{toolsSchema}

You MUST choose the correct tools to satisfy the user's intent. If the user asks for multiple modifications or actions, you can return a list of multiple tool calls to be executed in sequence.

For each tool call, specify the name of the tool and the arguments in JSON format.
Your output must be strictly valid JSON in the following format (no markdown, no other text, no code blocks):
{{
  ""calls"": [
    {{
      ""tool"": ""modify_pawn_state"",
      ""arguments"": {{
        ""pawnName"": ""Fred"",
        ""action"": ""set_skill"",
        ""skillName"": ""Shooting"",
        ""level"": 20
      }}
    }}
  ]
}}";

            var options = new ChatOptions { priority = 9, requestName = "God Mode Resolver" };
            if (!string.IsNullOrEmpty(_selectedRoutingId))
            {
                options.priority = 10;
            }

            SynapseClient.PromptAsync(
                RimSynapseMod.ModHandle,
                systemPrompt,
                $"User Instruction: {_commandInput}",
                result =>
                {
                    _busy = false;
                    if (result.success)
                    {
                        try
                        {
                            string json = RimSynapse.Utils.JsonHelper.ExtractJson(result.content);
                            if (string.IsNullOrEmpty(json))
                            {
                                _statusText = "Error: Invalid JSON response";
                                _statusColor = Color.red;
                                _executionLog.Add($"[Error] Raw Response: {result.content}");
                                return;
                            }

                            var response = JsonConvert.DeserializeObject<GodModeResponse>(json);
                            if (response == null || response.calls == null || response.calls.Count == 0)
                            {
                                _statusText = "No actions executed";
                                _statusColor = Color.yellow;
                                _executionLog.Add("[System] No actions were resolved by the LLM.");
                                return;
                            }

                            _statusText = $"Successfully executed {response.calls.Count} actions";
                            _statusColor = Color.green;

                            // Queue the tool calls onto the main thread to run safely
                            SynapseGameComponent.Enqueue(() =>
                            {
                                foreach (var call in response.calls)
                                {
                                    _executionLog.Add($"[Executing] Call: {call.tool} with args: {JsonConvert.SerializeObject(call.arguments)}");
                                    try
                                    {
                                        string argsJson = JsonConvert.SerializeObject(call.arguments);
                                        string outcome = SynapseToolRegistry.ExecuteTool(call.tool, argsJson);
                                        _executionLog.Add($"[Result] {outcome}");
                                    }
                                    catch (Exception ex)
                                    {
                                        _executionLog.Add($"[Error] Execution failed: {ex.Message}");
                                    }
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            _statusText = "Error parsing response";
                            _statusColor = Color.red;
                            _executionLog.Add($"[Error] Exception: {ex.Message}");
                        }
                    }
                    else
                    {
                        _statusText = "Request failed";
                        _statusColor = Color.red;
                        _executionLog.Add($"[Error] LLM Request failed: {result.error}");
                    }
                },
                options
            );
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
