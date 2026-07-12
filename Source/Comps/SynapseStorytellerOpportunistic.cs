using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimSynapse.Utils;
using Newtonsoft.Json;

namespace RimSynapse.Comps
{
    public static class SynapseStorytellerOpportunistic
    {
        public static bool TriggerPacingAdjustment()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return false;
            if (Find.Storyteller?.def?.defName != "Synapse") return false;

            var map = Find.CurrentMap;
            
            string wealth = map.wealthWatcher.WealthTotal.ToString("F0");
            string pop = map.mapPawns.FreeColonistsCount.ToString();
            string mood = map.mapPawns.FreeColonists.Average(p => p.needs?.mood?.CurLevelPercentage ?? 0.5f).ToString("P0");
            
            string systemPrompt = @"You are the Aura Algorithm Pacing Adjuster.
You run every 6 hours. Based on the colony's raw status, you must decide if the game should speed up event generation (harass the player more) or slow it down.
Return a 'PacingMultiplier' (float).
- 1.0 is standard vanilla pacing.
- > 1.0 means MORE frequent events (e.g., 1.5 = 50% more frequent).
- < 1.0 means LESS frequent events (e.g., 0.5 = half as frequent).

You MUST respond strictly in valid JSON format:
{
  ""PacingMultiplier"": 1.0
}";

            string userMessage = $@"Colony Status:
- Wealth: {wealth}
- Population: {pop}
- Average Mood: {mood}

Analyze the situation and provide the PacingMultiplier.";

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
                            string json = JsonHelper.ExtractJson(result.content);
                            if (json == null) return;

                            var parsed = JsonConvert.DeserializeObject<Dictionary<string, float>>(json);
                            if (parsed != null && parsed.TryGetValue("PacingMultiplier", out float mult))
                            {
                                var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
                                if (coreComp != null)
                                {
                                    coreComp.GlobalPacingMultiplier = UnityEngine.Mathf.Clamp(mult, 0.1f, 5.0f);
                                    RimSynapse.SynapseLogger.Message($"[RimSynapse-Core] Aura pacing adjusted to {coreComp.GlobalPacingMultiplier}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            RimSynapse.SynapseLogger.Warn("core", $"[RimSynapse-Core] Failed to parse pacing response: {ex.Message}");
                        }
                    }
                },
                new ChatOptions { queryId = "aura_pacing", priority = 1, requestName = "Aura Pacing", targetName = "Colony" }
            );

            return true;
        }

        public static void TriggerEventSelection(IncidentCategoryDef category, IIncidentTarget target)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;

            var map = Find.CurrentMap;
            string wealth = map.wealthWatcher.WealthTotal.ToString("F0");
            string pop = map.mapPawns.FreeColonistsCount.ToString();
            string mood = map.mapPawns.FreeColonists.Average(p => p.needs?.mood?.CurLevelPercentage ?? 0.5f).ToString("P0");
            
            var coreWorldComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            string recentEvents = "None recently.";
            if (coreWorldComp != null)
            {
                var events = coreWorldComp.GetRecentEvents(5);
                if (events.Any())
                {
                    recentEvents = string.Join("\n", events.Select(e => $"- {e.eventDescription}"));
                }
            }

            string systemPrompt = $@"You are the Aura Algorithm Event Selector.
An event trigger has occurred for category: {category.defName}.
You must pick the EXACT IncidentDefName from vanilla RimWorld that fits the current narrative best.
For example, if the category is ThreatBig, choose 'RaidEnemy', 'Infestation', 'ManhunterPack', etc.
If the category is FactionArrival, choose 'TraderCaravanArrival', 'VisitorGroup', etc.

You MUST respond strictly in valid JSON format:
{{
  ""IncidentDefName"": ""(The exact def name of the incident)""
}}";

            string userMessage = $@"Colony Status:
- Wealth: {wealth}
- Population: {pop}
- Average Mood: {mood}

Recent Events:
{recentEvents}

Provide the incident def name.";

            SynapseClient.PromptAsync(
                RimSynapseMod.ModHandle,
                systemPrompt,
                userMessage,
                result =>
                {
                    // Restore pacing immediately upon return so the game isn't permanently paused for events
                    if (coreWorldComp != null)
                    {
                        coreWorldComp.GlobalPacingMultiplier = coreWorldComp.BasePacingMultiplier;
                    }

                    if (result.success)
                    {
                        try
                        {
                            string json = JsonHelper.ExtractJson(result.content);
                            if (json == null) return;

                            var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                            if (parsed != null && parsed.TryGetValue("IncidentDefName", out string defName))
                            {
                                IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
                                if (def != null)
                                {
                                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, target);
                                    if (def.Worker.CanFireNow(parms))
                                    {
                                        Find.Storyteller.incidentQueue.Add(def, Find.TickManager.TicksGame, parms);
                                        RimSynapse.SynapseLogger.Message($"[RimSynapse-Core] Aura Event Selection chose: {defName}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            RimSynapse.SynapseLogger.Warn("core", $"[RimSynapse-Core] Failed to parse event selection: {ex.Message}");
                        }
                    }
                },
                new ChatOptions { queryId = "aura_event_selection", priority = 10, requestName = "Aura Event Selection", targetName = category.defName }
            );
        }
    }
}
