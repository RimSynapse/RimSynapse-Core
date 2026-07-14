using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimSynapse.Utils;
using RimSynapse.Models;
using Newtonsoft.Json;

namespace RimSynapse.Comps
{
    public class PacingAdjustmentResult
    {
        public float PacingMultiplier = 1.0f;
        public Dictionary<string, float> CategoryMultipliers = new Dictionary<string, float>();
    }

    public static class SynapseStorytellerOpportunistic
    {
        private static string GetColonyDetailedMetrics(Map map)
        {
            if (map == null) return "None.";

            float totalWealth = map.wealthWatcher?.WealthTotal ?? 0f;
            int freeColonists = map.mapPawns?.FreeColonistsCount ?? 0;
            float avgMood = map.mapPawns?.FreeColonists?.Any() == true
                ? map.mapPawns.FreeColonists.Average(p => p.needs?.mood?.CurLevelPercentage ?? 0.5f)
                : 0.5f;

            // Combat capability (compatible with vanilla and Combat Extended)
            int capablePawns = 0;
            int armedPawns = 0;
            int downedPawns = 0;
            foreach (var pawn in map.mapPawns.FreeColonists)
            {
                if (pawn.Downed)
                {
                    downedPawns++;
                }
                else if (!pawn.Dead && !pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    capablePawns++;
                    if (pawn.equipment?.Primary != null && pawn.equipment.Primary.def.IsWeapon)
                    {
                        armedPawns++;
                    }
                }
            }

            // Silver count on map
            int silverCount = 0;
            foreach (var thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
            {
                silverCount += thing.stackCount;
            }

            // Food reserves nutrition
            float totalNutrition = 0f;
            try
            {
                foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSource))
                {
                    if (thing.def.IsNutritionGivingIngestible && thing.def.ingestible != null)
                    {
                        totalNutrition += thing.stackCount * thing.def.ingestible.CachedNutrition;
                    }
                }
            }
            catch { }

            // Growing Season statistics
            float growablePercent = 1f;
            if (map.Tile >= 0)
            {
                int growableTwelfths = 0;
                for (int i = 0; i < 12; i++)
                {
                    Twelfth twelfth = (Twelfth)i;
                    if (GenTemperature.AverageTemperatureAtTileForTwelfth(map.Tile, twelfth) >= 6f)
                    {
                        growableTwelfths++;
                    }
                }
                growablePercent = (float)growableTwelfths / 12f;
            }
            float burdenMult = 1f / Math.Max(0.08f, growablePercent);

            // Group tame animals
            var animalGroups = new Dictionary<string, (int count, float wealth, int trainedSteps, float dailyHunger)>();
            int tameAnimalsCount = 0;
            float tameAnimalsWealth = 0f;

            try
            {
                if (map.mapPawns?.AllPawns != null)
                {
                    foreach (var p in map.mapPawns.AllPawns)
                    {
                        if (p.Faction == Faction.OfPlayer && p.RaceProps != null && p.RaceProps.Animal)
                        {
                            tameAnimalsCount++;
                            tameAnimalsWealth += p.MarketValue;

                            string label = p.def.label;
                            int trained = 0;
                            if (p.training != null)
                            {
                                foreach (var trainable in DefDatabase<TrainableDef>.AllDefs)
                                {
                                    if (p.training.HasLearned(trainable))
                                    {
                                        trained++;
                                    }
                                }
                            }

                            // Calculate daily hunger
                            float lifeStageHunger = p.ageTracker?.CurLifeStage?.hungerRateFactor ?? 1f;
                            float baseHunger = p.def?.race?.baseHungerRate ?? 1f;
                            float hungerMult = 1f;
                            var hungerStat = DefDatabase<StatDef>.GetNamed("HungerRateMultiplier", false);
                            if (hungerStat != null)
                            {
                                hungerMult = p.GetStatValue(hungerStat);
                            }
                            float dailyHunger = lifeStageHunger * baseHunger * hungerMult;

                            if (animalGroups.TryGetValue(label, out var tuple))
                            {
                                animalGroups[label] = (tuple.count + 1, tuple.wealth + p.MarketValue, tuple.trainedSteps + trained, tuple.dailyHunger + dailyHunger);
                            }
                            else
                            {
                                animalGroups[label] = (1, p.MarketValue, trained, dailyHunger);
                            }
                        }
                    }
                }
            }
            catch { }

            var groupLines = new List<string>();
            foreach (var kvp in animalGroups)
            {
                string species = kvp.Key;
                int count = kvp.Value.count;
                float wealth = kvp.Value.wealth;
                int steps = kvp.Value.trainedSteps;
                float hunger = kvp.Value.dailyHunger;

                // Daily nutrition cost per trained level, adjusted by winter duration
                float rawCostPerLevel = hunger / Math.Max(1, steps);
                float winterAdjustedCost = rawCostPerLevel * burdenMult;

                groupLines.Add($"{count}x {species} (Value: {wealth:F0} silver, Total Trained Steps: {steps}, Winter-Adjusted Nutrition Cost/Step: {winterAdjustedCost:F2})");
            }
            string animalReport = groupLines.Any() ? string.Join(", ", groupLines) : "None";

            // Greenhouse and Hydroponics capacity
            int activeHydroponics = 0;
            int activeSunLamps = 0;
            int activeSkylightsCount = 0;
            int greenhouseCells = 0;
            string trendText = "Stable";
            try
            {
                if (Find.World != null)
                {
                    var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
                    if (coreComp != null)
                    {
                        if (coreComp.mapGreenhouseCells != null)
                        {
                            coreComp.mapGreenhouseCells.TryGetValue(map.uniqueID, out greenhouseCells);
                        }

                        var history = coreComp.GetHistoryForMap(map.uniqueID);
                        if (history != null && history.Count >= 2)
                        {
                            int first = history[0];
                            int last = history[history.Count - 1];
                            if (last > first)
                            {
                                trendText = $"Growing (from {first} to {last} cells)";
                            }
                            else if (last < first)
                            {
                                trendText = $"Collapsing (from {first} down to {last} cells)";
                            }
                            else
                            {
                                trendText = $"Stable at {last} cells";
                            }
                        }
                    }
                }

                if (map.listerBuildings != null)
                {
                    var hydroDef = ThingDefOf.HydroponicsBasin;
                    if (hydroDef != null)
                    {
                        foreach (var b in map.listerBuildings.AllBuildingsColonistOfDef(hydroDef))
                        {
                            var power = b.TryGetComp<CompPowerTrader>();
                            if (power == null || power.PowerOn)
                            {
                                activeHydroponics++;
                            }
                        }
                    }

                    if (map.listerBuildings.allBuildingsColonist != null)
                    {
                        foreach (var b in map.listerBuildings.allBuildingsColonist)
                        {
                            if (b?.def?.defName == null) continue;

                            string defNameLower = b.def.defName.ToLowerInvariant();
                            bool isSunLamp = defNameLower.Contains("sunlamp");
                            bool isSkylight = defNameLower.Contains("skylight") || defNameLower.Contains("glassroof");

                            if (isSunLamp || isSkylight)
                            {
                                var power = b.TryGetComp<CompPowerTrader>();
                                if (power != null && !power.PowerOn) continue;

                                if (isSunLamp)
                                {
                                    activeSunLamps++;
                                }
                                else
                                {
                                    activeSkylightsCount++;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            int legendaryArtCount = 0;
            float legendaryArtValue = 0f;

            try
            {
                if (map.listerBuildings != null)
                {
                    if (map.listerBuildings.allBuildingsColonist != null)
                    {
                        foreach (var b in map.listerBuildings.allBuildingsColonist)
                        {
                            if (b != null)
                            {
                                if (b.def.IsArt)
                                {
                                    var quality = b.TryGetComp<CompQuality>();
                                    if (quality != null)
                                    {
                                        if (quality.Quality == QualityCategory.Legendary)
                                        {
                                            legendaryArtCount++;
                                            legendaryArtValue += b.MarketValue;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (map.listerThings != null)
                {
                    var haulables = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableAlways);
                    if (haulables != null)
                    {
                        foreach (var t in haulables)
                        {
                            if (t != null)
                            {
                                Thing innerThing = t;
                                if (t is MinifiedThing minified)
                                {
                                    innerThing = minified.InnerThing;
                                }

                                if (innerThing != null)
                                {
                                    if (innerThing.def.IsArt)
                                    {
                                        var quality = innerThing.TryGetComp<CompQuality>();
                                        if (quality != null)
                                        {
                                            if (quality.Quality == QualityCategory.Legendary)
                                            {
                                                legendaryArtCount++;
                                                legendaryArtValue += innerThing.MarketValue;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            int popDensity = 0;
            if (map != null && map.Tile >= 0)
            {
                if (SynapseCoreWorldComponent.GetPopulationDensityDelegate != null)
                {
                    popDensity = SynapseCoreWorldComponent.GetPopulationDensityDelegate(map.Tile);
                }
            }

            var auraComp = Find.Storyteller?.storytellerComps?.OfType<StorytellerComp_Aura>().FirstOrDefault();
            var props = auraComp?.props as StorytellerCompProperties_Aura;

            string metricsFormat = props?.metricsTemplate;
            if (string.IsNullOrEmpty(metricsFormat))
            {
                metricsFormat = @"Colony General and Resource Metrics:
- Overall Wealth: {wealth} (Items, buildings, and pawns)
- Available Silver: {silver} (Stored or mined on map)
- Food Reserves: {food} nutrition points
- Growing Season: {growingSeason} of the year growable (Winter Resource Burden Multiplier: {winterBurden}x)
- Greenhouse Capacity: {greenhouse}
- Population: {population} colonists
- Local Population Density (Pawn dwellings): {popDensity}
- Livestock: {livestock}
- Legendary Art: {legendaryArt} pieces (Total Value: {legendaryArtValue} silver)
- Combat Capability: {combat}
- Medical Status: {medical}
- Average Mood: {mood}";
            }

            string livestockFormat = props?.livestockTemplate;
            if (string.IsNullOrEmpty(livestockFormat))
            {
                livestockFormat = "{tameCount} tamed animals (Total Value: {tameWealth} silver)\n  - Detail: {animalReport}";
            }

            string greenhouseFormat = props?.greenhouseTemplate;
            if (string.IsNullOrEmpty(greenhouseFormat))
            {
                greenhouseFormat = "{greenhouseCells} active growable cells at midday (Hydroponics: {activeHydroponics} active basins, Sun Lamps: {activeSunLamps} powered, Skylights/Solar Roofs: {activeSkylightsCount}, Trend: {trend})";
            }

            string greenhouseText = greenhouseFormat
                .Replace("{greenhouseCells}", greenhouseCells.ToString())
                .Replace("{activeHydroponics}", activeHydroponics.ToString())
                .Replace("{activeSunLamps}", activeSunLamps.ToString())
                .Replace("{activeSkylightsCount}", activeSkylightsCount.ToString())
                .Replace("{trend}", trendText);

            string livestockText = livestockFormat
                .Replace("{tameCount}", tameAnimalsCount.ToString())
                .Replace("{tameWealth}", tameAnimalsWealth.ToString("F0"))
                .Replace("{animalReport}", animalReport);

            return metricsFormat
                .Replace("{wealth}", totalWealth.ToString("F0"))
                .Replace("{silver}", silverCount.ToString())
                .Replace("{food}", totalNutrition.ToString("F1"))
                .Replace("{growingSeason}", growablePercent.ToString("P0"))
                .Replace("{winterBurden}", burdenMult.ToString("F1"))
                .Replace("{greenhouse}", greenhouseText)
                .Replace("{population}", freeColonists.ToString())
                .Replace("{popDensity}", popDensity.ToString())
                .Replace("{livestock}", livestockText)
                .Replace("{legendaryArt}", legendaryArtCount.ToString())
                .Replace("{legendaryArtValue}", legendaryArtValue.ToString("F0"))
                .Replace("{combat}", $"{capablePawns} healthy colonists capable of violence ({armedPawns} currently armed)")
                .Replace("{medical}", $"{downedPawns} colonists currently downed/injured")
                .Replace("{mood}", avgMood.ToString("P0"));
        }

        public static bool TriggerPacingAdjustment()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return false;
            if (Find.Storyteller?.def?.defName != "Synapse") return false;

            var map = Find.CurrentMap;
            string metrics = GetColonyDetailedMetrics(map);
            
            var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            string recentEvents = "None recently.";
            if (coreComp != null)
            {
                var events = coreComp.GetRecentEvents(10);
                if (events.Any())
                {
                    recentEvents = string.Join("\n", events.Select(e =>
                    {
                        string desc = $"- {e.eventDescription}";
                        if (e.isResolved && !string.IsNullOrEmpty(e.outcomeDescription))
                        {
                            desc += $" (Resolution: {e.outcomeDescription} [Outcome: {e.outcome}])";
                        }
                        return desc;
                    }));
                }
            }

            var auraComp = Find.Storyteller?.storytellerComps?.OfType<StorytellerComp_Aura>().FirstOrDefault();
            string systemPrompt = (auraComp?.props as StorytellerCompProperties_Aura)?.pacingSystemPrompt;

            if (string.IsNullOrEmpty(systemPrompt))
            {
                systemPrompt = @"You are the Aura Storyteller Pacing and Weighting Coordinator.
Your role is to orchestrate the colony's challenge level and dynamic pacing based on its current successes, setbacks, and resources.

You must evaluate:
1. Successes/Triumphs (e.g. repelled raids, completed quests) -> Increase challenge (more ThreatBig/ThreatSmall, higher pacing).
2. Failures/Tragedies (e.g. dead colonists, burned buildings, kidnapped pawns) -> Soften the blow (lower pacing, decrease ThreatBig, increase Misc/FactionArrival for traders and helpers).
3. Resource state (low combat capability, low food, low silver) -> Trigger friendly events (traders, wanderers) or easy quests. High wealth but low defense -> motivated raids.
4. Legendary Art: Legendary art pieces are renowned world attractions. If the colony has legendary art pieces, it draws visitors and affluent guests. Increase the probability/likelihood of friendly visitors, trade caravans, and affluent travelers ('FactionArrival' and 'Misc') proportionally. Scale the visitor frequency and wealth based on the number and value of legendary art pieces and total colony wealth.
5. Local Population Density (Pawn dwellings): High population density indicates a civilized, protected region near city centers. In high density areas, favor pawn joins, travelers, and caravans (Misc, FactionArrival) and significantly reduce hostile raids/threats (ThreatBig, ThreatSmall). Low population density (remote frontier) is lawless and dangerous; in low density areas, increase the likelihood of raids (ThreatBig, ThreatSmall) and reduce positive join/wanderer events (Misc).

Return a JSON object containing:
- 'PacingMultiplier': Float. Standard is 1.0. Increase (>1.0) to speed up event frequency. Decrease (<1.0) to give the colony breathing room.
- 'CategoryMultipliers': Dictionary of category def names (e.g. 'ThreatBig', 'ThreatSmall', 'Misc', 'DiseaseHuman', 'FactionArrival') to float multipliers. Standard is 1.0. Increase to make that type of event more likely, decrease to make it less likely.

You MUST respond strictly in valid JSON format:
{
  ""PacingMultiplier"": 1.0,
  ""CategoryMultipliers"": {
    ""ThreatBig"": 1.0,
    ""ThreatSmall"": 1.0,
    ""Misc"": 1.0,
    ""DiseaseHuman"": 1.0,
    ""FactionArrival"": 1.0
  }
}";
            }

            string userMessage = $@"Colony Status:
{metrics}

Recent Events:
{recentEvents}

Analyze the situation and provide the PacingMultiplier and CategoryMultipliers.";

            var request = new LlmTextRequest
            {
                SystemPrompt = systemPrompt,
                Messages = new List<ChatMessage> { ChatMessage.User(userMessage) },
                EnforceJson = true
            };

            SynapseClient.SendTextAsync(
                RimSynapseMod.ModHandle,
                request,
                new ChatOptions { queryId = "aura_pacing", priority = 1, requestName = "Aura Pacing", targetName = "Colony" },
                result =>
                {
                    if (result.success)
                    {
                        try
                        {
                            string json = JsonHelper.ExtractJson(result.content);
                            if (json == null) return;

                            var parsed = JsonConvert.DeserializeObject<PacingAdjustmentResult>(json);
                            if (parsed != null)
                            {
                                var comp = Find.World.GetComponent<SynapseCoreWorldComponent>();
                                if (comp != null)
                                {
                                    comp.GlobalPacingMultiplier = UnityEngine.Mathf.Clamp(parsed.PacingMultiplier, 0.1f, 5.0f);
                                    if (parsed.CategoryMultipliers != null)
                                    {
                                        foreach (var kvp in parsed.CategoryMultipliers)
                                        {
                                            comp.categoryMultipliers[kvp.Key] = UnityEngine.Mathf.Clamp(kvp.Value, 0.01f, 10.0f);
                                        }
                                    }
                                    RimSynapse.SynapseLogger.Message($"[RimSynapse-Core] Aura pacing adjusted to {comp.GlobalPacingMultiplier} with category multipliers updated.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            RimSynapse.SynapseLogger.Warn("core", $"[RimSynapse-Core] Failed to parse pacing response: {ex.Message}");
                        }
                    }
                }
            );

            return true;
        }

        public static void TriggerEventSelection(IncidentCategoryDef category, IIncidentTarget target)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;

            var map = Find.CurrentMap;
            string metrics = GetColonyDetailedMetrics(map);
            
            var coreWorldComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            string recentEvents = "None recently.";
            if (coreWorldComp != null)
            {
                var events = coreWorldComp.GetRecentEvents(5);
                if (events.Any())
                {
                    recentEvents = string.Join("\n", events.Select(e =>
                    {
                        string desc = $"- {e.eventDescription}";
                        if (e.isResolved && !string.IsNullOrEmpty(e.outcomeDescription))
                        {
                            desc += $" (Resolution: {e.outcomeDescription} [Outcome: {e.outcome}])";
                        }
                        return desc;
                    }));
                }
            }

            var auraComp = Find.Storyteller?.storytellerComps?.OfType<StorytellerComp_Aura>().FirstOrDefault();
            string systemPrompt = (auraComp?.props as StorytellerCompProperties_Aura)?.selectionSystemPrompt;

            if (string.IsNullOrEmpty(systemPrompt))
            {
                systemPrompt = $@"You are the Aura Storyteller Event Selector.
An event trigger has occurred for category: {category.defName}.
You must pick the EXACT IncidentDefName from vanilla RimWorld that fits the current narrative best.
For example, if the category is ThreatBig, choose 'RaidEnemy', 'Infestation', 'ManhunterPack', etc.
If the category is FactionArrival, choose 'TraderCaravanArrival', 'VisitorGroup', etc.

Legendary Art Attraction: If the colony has legendary art pieces (reported in metrics), choose friendly visitors, guest groups, and affluent/wealthy traders more frequently to simulate them visiting to admire the art. If colony wealth is also high, attract more affluent or exotic traders.

Civilization and Population Density context: If local population density is high (civilized lands), choose peaceful, urban, or civilized incidents (e.g. wanderers, caravans, peace talks) and avoid wild threats like raw infestations or animal stampedes. If density is low (isolated frontier wilderness), favor rogue raiders, manhunters, or harsh environmental challenges fitting a lawless outpost.

You MUST respond strictly in valid JSON format:
{{
  ""IncidentDefName"": ""(The exact def name of the incident)""
}}";
            }

            string userMessage = $@"Colony Status:
{metrics}

Recent Events:
{recentEvents}

Provide the incident def name.";

            var request = new LlmTextRequest
            {
                SystemPrompt = systemPrompt,
                Messages = new List<ChatMessage> { ChatMessage.User(userMessage) },
                EnforceJson = true
            };

            SynapseClient.SendTextAsync(
                RimSynapseMod.ModHandle,
                request,
                new ChatOptions { queryId = "aura_event_selection", priority = 10, requestName = "Aura Event Selection", targetName = category.defName },
                result =>
                {
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
                }
            );
        }
    }
}
