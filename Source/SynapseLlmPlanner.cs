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
            string systemPrompt = @"You are the RimWorld Synapse God Mode action resolver.
The user will input an instruction in plain English. Your job is to translate this instruction into sequential game actions.

You operate as a stateful, planning-based agent across a multi-turn loop.

You MUST follow this exact 3-step reasoning lifecycle:

1. **PLAN**: Brainstorm the logical requirements to fulfill the user's prompt. Identify constraints and dependencies (e.g. 'To make Dole commit suicide, he needs a weapon. I must first check his inventory, and search the map for weapons in case he has none. Then I will direct him to equip the best weapon, wait for it, and trigger the suicide.').
2. **QUERY**: On your first turn, output ONLY search/query calls (like 'search_map_entities') to locate target pawns, items, or weapons on the map.
3. **ASSEMBLE & EXECUTE**: Once you receive the search results:
   - Analyze the active game state (e.g. proximity of weapons, pawn inventories).
   - If the action is immediate, output a flat JSON list of 'calls'.
   - If the action requires time-delayed steps (such as walking to a weapon, waiting for it to be equipped, and then performing an action), output a stateful JSON 'script'.
   - Enforce thematic and immersive flavor text: use descriptive 'commandName' values and add custom popups or letters.

Format for immediate calls:
{
  ""calls"": [
    {
      ""tool"": ""modify_pawn_state"",
      ""arguments"": {
        ""thingId"": ""Pawn_Cow123"",
        ""action"": ""kill""
      }
    }
  ]
}

Format for stateful scripts (supports sequential execution and 'wait_until' delays):
{
  ""scriptName"": ""Dole's Tragic End"",
  ""steps"": [
    {
      ""type"": ""possess_colonist"",
      ""arguments"": {
        ""pawnName"": ""Dole"",
        ""action"": ""clear"",
        ""commandName"": ""Overcome with grief""
      }
    },
    {
      ""type"": ""possess_colonist"",
      ""arguments"": {
        ""pawnName"": ""Dole"",
        ""action"": ""equip"",
        ""targetItemName"": ""shotgun""
      }
    },
    {
      ""type"": ""wait_until"",
      ""arguments"": {
        ""condition"": ""has_weapon"",
        ""pawnName"": ""Dole"",
        ""timeoutTicks"": 6000
      }
    },
    {
      ""type"": ""damage_self_with_equipped"",
      ""arguments"": {
        ""pawnName"": ""Dole"",
        ""targetBodyPart"": ""Head"",
        ""damageMultiplier"": 4.0
      }
    },
    {
      ""type"": ""send_notification_letter"",
      ""arguments"": {
        ""title"": ""Death: Dole"",
        ""text"": ""Dole has taken his own life."",
        ""letterType"": ""death"",
        ""pawnName"": ""Dole""
      }
    }
  ]
}

IMPORTANT: If you have finished all actions or if there is nothing left to do, output a friendly natural language summary of your actions without any JSON block.";

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

            var toolsList = new List<GameToolDefinition>();
            foreach (var tool in SynapseToolRegistry.AllTools)
            {
                if (tool.name != "execute_game_tool" && tool.name != "list_available_tools")
                {
                    toolsList.Add(new GameToolDefinition
                    {
                        name = tool.name,
                        description = tool.description,
                        parameters = tool.parameters
                    });
                }
            }

            var request = new LlmTextRequest
            {
                Messages = _messages,
                SystemPrompt = "",
                EnforceJson = false,
                Tools = toolsList
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
