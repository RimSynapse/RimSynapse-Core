using System;
using System.Collections.Generic;
using RimSynapse.Models;

namespace RimSynapse
{
    /// <summary>
    /// Handles the multi-turn conversation memory, prompt construction, and LLM requests for natural language actions.
    /// </summary>
    public class SynapseLlmPlanner
    {
        private const int MaxTurns = 5;
        private readonly string _command;
        private readonly Action<string> _logCallback;
        private readonly Action<bool, string> _onComplete;
        private readonly List<ChatMessage> _messages;
        private int _turnsCount = 0;

        public SynapseLlmPlanner(string command, Action<string> logCallback, Action<bool, string> onComplete)
        {
            _command = command;
            _logCallback = logCallback;
            _onComplete = onComplete;
            _messages = new List<ChatMessage>();
        }

        public void Start()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("You are the RimWorld Synapse God Mode action resolver.");
            sb.AppendLine("The user will input an instruction in plain English. Your job is to translate this instruction into sequential game actions.");
            sb.AppendLine();
            sb.AppendLine("You operate as a stateful, planning-based agent across a multi-turn loop.");
            sb.AppendLine();
            sb.AppendLine("You MUST follow this exact 3-step reasoning lifecycle:");
            sb.AppendLine("1. **PLAN**: Brainstorm the logical requirements to fulfill the user's prompt. Identify constraints and dependencies (e.g. 'To make Dole commit suicide, he needs a weapon. I must first check his inventory, and search the map for weapons in case he has none. Then I will direct him to equip the best weapon, wait for it, and trigger the suicide.').");
            sb.AppendLine("2. **QUERY**: On your first turn, output ONLY search/query calls (like 'search_map_entities') to locate target pawns, items, or weapons on the map.");
            sb.AppendLine("3. **ASSEMBLE & EXECUTE**: Once you receive the search results:");
            sb.AppendLine("   - Analyze the active game state (e.g. proximity of weapons, pawn inventories).");
            sb.AppendLine("   - If the action is immediate, output a flat JSON list of 'calls'.");
            sb.AppendLine("   - If the action requires time-delayed steps (such as walking to a weapon, waiting for it to be equipped, and then performing an action), output a stateful JSON 'script'.");
            sb.AppendLine("   - Enforce thematic and immersive flavor text: use descriptive 'commandName' values and add custom popups or letters.");
            sb.AppendLine();
            sb.AppendLine("Format for immediate calls:");
            sb.AppendLine("{");
            sb.AppendLine("  \"calls\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"tool\": \"modify_pawn_state\",");
            sb.AppendLine("      \"arguments\": {");
            sb.AppendLine("        \"thingId\": \"Pawn_Cow123\",");
            sb.AppendLine("        \"action\": \"kill\"");
            sb.AppendLine("      }");
            sb.AppendLine("    }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("Format for stateful scripts (supports sequential execution and 'wait_until' delays):");
            sb.AppendLine("{");
            sb.AppendLine("  \"scriptName\": \"Dole's Tragic End\",");
            sb.AppendLine("  \"steps\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"type\": \"possess_colonist\",");
            sb.AppendLine("      \"arguments\": {");
            sb.AppendLine("        \"pawnName\": \"Dole\",");
            sb.AppendLine("        \"action\": \"clear\",");
            sb.AppendLine("        \"commandName\": \"Overcome with grief\"");
            sb.AppendLine("      }");
            sb.AppendLine("    },");
            sb.AppendLine("    {");
            sb.AppendLine("      \"type\": \"possess_colonist\",");
            sb.AppendLine("      \"arguments\": {");
            sb.AppendLine("        \"pawnName\": \"Dole\",");
            sb.AppendLine("        \"action\": \"equip\",");
            sb.AppendLine("        \"targetItemName\": \"shotgun\"");
            sb.AppendLine("      }");
            sb.AppendLine("    },");
            sb.AppendLine("    {");
            sb.AppendLine("      \"type\": \"wait_until\",");
            sb.AppendLine("      \"arguments\": {");
            sb.AppendLine("        \"condition\": \"has_weapon\",");
            sb.AppendLine("        \"pawnName\": \"Dole\",");
            sb.AppendLine("        \"timeoutTicks\": 6000");
            sb.AppendLine("      }");
            sb.AppendLine("    },");
            sb.AppendLine("    {");
            sb.AppendLine("      \"type\": \"damage_self_with_equipped\",");
            sb.AppendLine("      \"arguments\": {");
            sb.AppendLine("        \"pawnName\": \"Dole\",");
            sb.AppendLine("        \"targetBodyPart\": \"Head\",");
            sb.AppendLine("        \"damageMultiplier\": 4.0");
            sb.AppendLine("      }");
            sb.AppendLine("    },");
            sb.AppendLine("    {");
            sb.AppendLine("      \"type\": \"send_notification_letter\",");
            sb.AppendLine("      \"arguments\": {");
            sb.AppendLine("        \"title\": \"Death: Dole\",");
            sb.AppendLine("        \"text\": \"Dole has taken his own life.\",");
            sb.AppendLine("        \"letterType\": \"death\",");
            sb.AppendLine("        \"pawnName\": \"Dole\"");
            sb.AppendLine("      }");
            sb.AppendLine("    }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: If you have finished all actions or if there is nothing left to do, output a friendly natural language summary of your actions without any JSON block.");
            sb.AppendLine();
            sb.AppendLine("Available Tools (output them inside your JSON calls or steps):");

            foreach (var tool in SynapseToolRegistry.AllTools)
            {
                if (tool.name != "execute_game_tool" && tool.name != "list_available_tools")
                {
                    sb.AppendLine($"- **{tool.name}**: {tool.description}");
                    try
                    {
                        var parametersJObj = Newtonsoft.Json.Linq.JObject.FromObject(tool.parameters);
                        if (parametersJObj != null && parametersJObj.TryGetValue("properties", out var propsToken) && propsToken is Newtonsoft.Json.Linq.JObject propsObj)
                        {
                            foreach (var prop in propsObj)
                            {
                                string pName = prop.Key;
                                string pType = "string";
                                string pDesc = "";
                                if (prop.Value is Newtonsoft.Json.Linq.JObject pDetail)
                                {
                                    if (pDetail.TryGetValue("type", out var tToken)) pType = tToken.ToString();
                                    if (pDetail.TryGetValue("description", out var dToken)) pDesc = dToken.ToString();
                                }
                                sb.AppendLine($"  * {pName} ({pType}): {pDesc}");
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Fallback to name if serialization fails
                    }
                }
            }

            string systemPrompt = sb.ToString();

            _messages.Add(ChatMessage.System(systemPrompt));
            _messages.Add(ChatMessage.User($"User Instruction: {_command}"));

            var options = new ChatOptions { priority = 10, requestName = "API Command Resolver" };
            RunAgentLoop(options);
        }

        public void RunAgentLoop(ChatOptions options)
        {
            _turnsCount++;
            if (_turnsCount > MaxTurns)
            {
                _logCallback?.Invoke("[Warning] Maximum execution turns reached. Terminating loop.");
                _onComplete?.Invoke(false, "Max turns reached without final summary.");
                return;
            }

            _logCallback?.Invoke($"[API Agent] Invoking LLM (Turn {_turnsCount})...");

            var request = new LlmTextRequest
            {
                Messages = _messages,
                SystemPrompt = "",
                EnforceJson = false,
                Tools = new List<GameToolDefinition>() // Clear native tool array to prevent local provider crashes/bloat
            };

            SynapseClient.SendTextAsync(
                RimSynapseMod.ModHandle,
                request,
                options,
                result =>
                {
                    if (!result.success)
                    {
                        _logCallback?.Invoke($"[Error] LLM request failed on turn {_turnsCount}: {result.error}");
                        _onComplete?.Invoke(false, $"LLM request failed: {result.error}");
                        return;
                    }

                    SynapseActionExecutor.ProcessResponse(this, _messages, result.content, options, _logCallback, _onComplete);
                }
            );
        }
    }
}
