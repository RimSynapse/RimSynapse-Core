using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Newtonsoft.Json;
using RimSynapse.Comps;
using RimSynapse.Models;

namespace RimSynapse
{
    public static class SynapseTriggerManager
    {
        public static void TriggerEvent(string eventName, Pawn pawn, Dictionary<string, string> extraContext = null)
        {
            try
            {
                var matchingDefs = DefDatabase<SynapseTriggerDef>.AllDefsListForReading
                    .Where(d => d.eventName != null && d.eventName.Equals(eventName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingDefs.Count == 0) return;

                foreach (var def in matchingDefs)
                {
                    ExecuteTrigger(def, pawn, extraContext);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimSynapse] Exception in TriggerEvent: {ex.Message}");
            }
        }

        private static void ExecuteTrigger(SynapseTriggerDef def, Pawn pawn, Dictionary<string, string> extraContext)
        {
            var context = new StringBuilder();
            if (pawn != null)
            {
                context.AppendLine($"Target Pawn: {pawn.LabelShort}");
                if (pawn.health != null && pawn.health.summaryHealth != null)
                {
                    context.AppendLine($"Pawn Health: Summary={(pawn.health.summaryHealth.SummaryHealthPercent * 100f):F0}%");
                }
                if (pawn.story != null)
                {
                    context.AppendLine($"Pawn Backstories: Childhood={pawn.story.Childhood?.titleShort}, Adulthood={pawn.story.Adulthood?.titleShort}");
                    context.AppendLine($"Pawn Traits: {string.Join(", ", pawn.story.traits.allTraits.Select(t => t.Label))}");
                }
            }
            if (extraContext != null)
            {
                foreach (var kvp in extraContext)
                {
                    context.AppendLine($"{kvp.Key}: {kvp.Value}");
                }
            }

            string systemPrompt = AssembleSystemPrompt();
            string userMessage = $"Situation Context:\n{context}\n\n" +
                                 (string.IsNullOrEmpty(def.condition) ? "" : $"Condition to check: {def.condition}\n") +
                                 $"Instruction to execute: {def.instruction}";

            SynapseClient.PromptAsync(
                RimSynapseMod.ModHandle,
                systemPrompt,
                userMessage,
                result =>
                {
                    if (result.success)
                    {
                        try
                        {
                            string json = RimSynapse.Utils.JsonHelper.ExtractJson(result.content);
                            if (string.IsNullOrEmpty(json)) return;

                            var response = JsonConvert.DeserializeObject<TriggerResponse>(json);
                            if (response != null && response.calls != null && response.calls.Count > 0)
                            {
                                SynapseGameComponent.Enqueue(() =>
                                {
                                    foreach (var call in response.calls)
                                    {
                                        try
                                        {
                                            string argsJson = JsonConvert.SerializeObject(call.arguments);
                                            SynapseToolRegistry.ExecuteTool(call.tool, argsJson);
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Warning($"[RimSynapse] Trigger Def tool execution failed: {ex.Message}");
                                        }
                                    }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[RimSynapse] Trigger parsing failed: {ex.Message}");
                        }
                    }
                    else
                    {
                        Log.Warning($"[RimSynapse] Trigger query failed: {result.error}");
                    }
                },
                new ChatOptions { priority = 3, requestName = $"Trigger resolver: {def.defName}" }
            );
        }

        private static string AssembleSystemPrompt()
        {
            var toolsSchema = new StringBuilder();
            foreach (var tool in SynapseToolRegistry.AllTools)
            {
                toolsSchema.AppendLine($"- Tool: \"{tool.name}\"");
                toolsSchema.AppendLine($"  Description: {tool.description}");
                toolsSchema.AppendLine($"  Arguments schema: {JsonConvert.SerializeObject(tool.parameters)}");
                toolsSchema.AppendLine();
            }

            return $@"You are the RimWorld Synapse Event Trigger resolver.
Your job is to read the situation, check the condition (if any), and determine if the instruction should be executed.
If the condition is met (or none is provided), generate a sequence of tool calls to satisfy the instruction. If the condition is not met, do not return any calls.
You have access to the following tools:

{toolsSchema}

Your output must be strictly valid JSON in the following format (no markdown, no other text):
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
        }
    }

    public class TriggerResponse
    {
        public List<TriggerCall> calls;
    }

    public class TriggerCall
    {
        public string tool;
        public Dictionary<string, object> arguments;
    }
}
