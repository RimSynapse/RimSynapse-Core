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
        public List<string> RequestTools = null;
    }

    public class EventSelectionResult
    {
        public string IncidentDefName;
        public List<string> RequestTools = null;
    }

    public static class SynapseStorytellerOpportunistic
    {
        private static string GetToolsTextList()
        {
            int maxBudget = RimSynapseMod.Instance?.Settings?.maxPacingContextTokens ?? 4096;
            bool limitTools = maxBudget <= 8192;

            var allowedTools = new HashSet<string>
            {
                "get_colonists_profile",
                "get_stockpile_details",
                "get_active_threats",
                "get_colony_moods",
                "get_faction_relations_history"
            };

            var list = new List<string>();
            foreach (var tool in SynapseToolRegistry.NonDebugTools)
            {
                if (!limitTools || allowedTools.Contains(tool.name))
                {
                    list.Add($"- '{tool.name}': {tool.description}");
                }
            }
            return string.Join("\n", list);
        }

        private static void ApplyPacingAdjustment(PacingAdjustmentResult parsed)
        {
            if (parsed == null) return;
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
                RimSynapse.SynapseLogger.Message($"[RimSynapse-Core] Storyteller pacing adjusted to {comp.GlobalPacingMultiplier} with category multipliers updated.");
            }
        }

        private static void ApplyEventSelection(EventSelectionResult parsed, IIncidentTarget target)
        {
            if (parsed != null && !string.IsNullOrEmpty(parsed.IncidentDefName))
            {
                IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(parsed.IncidentDefName);
                if (def != null)
                {
                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, target);
                    if (def.Worker.CanFireNow(parms))
                    {
                        Find.Storyteller.incidentQueue.Add(def, Find.TickManager.TicksGame, parms);
                        RimSynapse.SynapseLogger.Message($"[RimSynapse-Core] Storyteller Event Selection chose: {parsed.IncidentDefName}");
                    }
                }
            }
        }

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

            var storytellerComp = Find.Storyteller?.storytellerComps?.OfType<StorytellerComp_Storyteller>().FirstOrDefault();
            var props = storytellerComp?.props as StorytellerCompProperties_Storyteller;

            string metricsFormat = props?.metricsTemplate;
            if (string.IsNullOrEmpty(metricsFormat))
            {
                metricsFormat = @"Colony General Metrics:
- Wealth: {wealth} | Silver: {silver} | Food: {food} points
- Season: {growingSeason} growable (Winter Burden: {winterBurden}x)
- Greenhouse: {greenhouse}
- Population: {population} colonists (Local Density: {popDensity})
- Livestock: {livestock}
- Legendary Art: {legendaryArt} pieces ({legendaryArtValue} silver)
- Combat: {combat} | Downed: {medical} | Avg Mood: {mood}";
            }

            string livestockFormat = props?.livestockTemplate;
            if (string.IsNullOrEmpty(livestockFormat))
            {
                livestockFormat = "{tameCount} tamed animals (Total Value: {tameWealth} silver)";
            }

            string greenhouseFormat = props?.greenhouseTemplate;
            if (string.IsNullOrEmpty(greenhouseFormat))
            {
                greenhouseFormat = "{greenhouseCells} cells (Hydroponics: {activeHydroponics}, Sun Lamps: {activeSunLamps}, Trend: {trend})";
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

            var coreWorldComp = Find.World?.GetComponent<SynapseCoreWorldComponent>();
            string growthMetric = "";
            string lastRaidReport = "";

            if (coreWorldComp != null)
            {
                if (coreWorldComp.wealthHistory != null && coreWorldComp.wealthHistory.Count >= 2)
                {
                    var oldest = coreWorldComp.wealthHistory[0];
                    int tickDiff = Find.TickManager.TicksGame - oldest.gameTick;
                    float days = (float)tickDiff / 60000f;
                    if (days >= 0.5f)
                    {
                        float currentTrueWealth = coreWorldComp.CalculateTrueWealth(map, props);
                        float wealthDiff = currentTrueWealth - oldest.wealth;
                        float avgColonists = 0.5f * (freeColonists + oldest.pawnCount);
                        if (avgColonists < 1) avgColonists = 1;
                        float actualRate = wealthDiff / (avgColonists * days);
                        
                        growthMetric = $"\n- Daily True Wealth Growth Rate (Rolling): {actualRate:F0} silver/colonist/day";
                        if (props != null)
                        {
                            float daysPassed = Find.TickManager.TicksGame / 60000f;
                            float targetRate = props.targetWealthGrowthFactor * (float)System.Math.Pow(daysPassed, props.targetWealthGrowthExponent) + props.targetWealthGrowthBase;
                            float blendedTargetRate = UnityEngine.Mathf.Lerp(targetRate, actualRate, props.pacingFlexibility);
                            if (blendedTargetRate > 0)
                            {
                                growthMetric += $" (Blended Target: {blendedTargetRate:F0} silver/colonist/day, Flexibility: {props.pacingFlexibility:F2})";
                            }
                        }
                    }
                }

                if (coreWorldComp.lastRaidOutcome != null)
                {
                    float daysSinceRaid = (Find.TickManager.TicksGame - coreWorldComp.lastRaidOutcome.gameTick) / 60000f;
                    int k = coreWorldComp.lastRaidOutcome.colonistsKilled;
                    int inj = coreWorldComp.lastRaidOutcome.colonistsInjured;
                    int kid = coreWorldComp.lastRaidOutcome.colonistsKidnapped;
                    int ek = coreWorldComp.lastRaidOutcome.enemiesKilled;
                    int ed = coreWorldComp.lastRaidOutcome.enemiesDowned;
                    
                    lastRaidReport = $"\n- Last Raid Outcome ({daysSinceRaid:F1} days ago): ";
                    if (k == 0 && kid == 0)
                    {
                        lastRaidReport += $"Successful defense. Enemies: {ek} killed, {ed} downed. Player loss: {inj} injured, 0 dead/kidnapped.";
                    }
                    else
                    {
                        lastRaidReport += $"Tragic defense (Colony Recovering). Player loss: {k} killed, {kid} kidnapped, {inj} injured. Enemies: {ek} killed, {ed} downed.";
                    }
                }
            }

            return (metricsFormat
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
                .Replace("{combat}", $"{capablePawns} violent ({armedPawns} armed)")
                .Replace("{medical}", $"{downedPawns} downed")
                .Replace("{mood}", avgMood.ToString("P0")))
                + growthMetric + lastRaidReport;
        }

        public static bool TriggerPacingAdjustment()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return false;
            if (Find.Storyteller?.storytellerComps?.OfType<StorytellerComp_Storyteller>().Any() != true) return false;

            var map = Find.CurrentMap;
            string metrics = GetColonyDetailedMetrics(map);
            
            var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            string recentEvents = "None recently.";
            if (coreComp != null)
            {
                int maxBudget = RimSynapseMod.Instance?.Settings?.maxPacingContextTokens ?? 4096;
                int eventCount = Math.Max(2, maxBudget / 800);
                var events = coreComp.GetRecentEvents(eventCount);
                if (events.Any())
                {
                    recentEvents = string.Join("\n", events.Select(e =>
                    {
                        string desc = !string.IsNullOrEmpty(e.mcpTag) ? $"- {e.mcpTag}" : $"- {e.eventDescription}";
                        if (e.isResolved)
                        {
                            desc += $" ({e.outcome})";
                        }
                        return desc;
                    }));
                }
            }

            var props = StorytellerComp_Storyteller.GetActiveStorytellerProps();
            string systemPrompt = props?.pacingSystemPrompt;

            if (string.IsNullOrEmpty(systemPrompt))
            {
                string characterName = props?.characterName ?? Find.Storyteller?.def?.label ?? "AI Storyteller";
                string speakingStyle = props?.speakingStyle ?? "sassy, dramatic, or menacing";
                bool useTools = RimSynapseMod.Instance?.Settings?.enableStorytellerTools == true;
                string toolInstruction = "";
                if (useTools)
                {
                    string toolsList = GetToolsTextList();
                    toolInstruction = @"
You have access to tools that query the live state of the colony. If you need more details to decide pacing (e.g. colonist profiles/skills, exact stockpiles, colony moods, or active map threats), you should request them.
Available tools to query:
" + toolsList + @"

If you have enough information to decide pacing, return a JSON object with 'PacingMultiplier' and 'CategoryMultipliers'.
If you need more details to make the decision, return a JSON object containing ONLY 'RequestTools' (a JSON array of the tool names you want to run), e.g.
{
  ""RequestTools"": [""get_colonists_profile"", ""get_active_threats""]
}
";
                }

                systemPrompt = @"You are the " + characterName + @" Pacing and Weighting Coordinator.
Your writing style is " + speakingStyle + @".
Your role is to orchestrate the colony's challenge level and dynamic pacing based on its current successes, setbacks, and resources.
" + toolInstruction + @"
You must evaluate:
1. Successes/Triumphs (e.g. repelled raids, completed quests) -> Increase challenge (more ThreatBig/ThreatSmall, higher pacing).
2. Failures/Tragedies (e.g. dead colonists, burned buildings, kidnapped pawns) -> Soften the blow (lower pacing, decrease ThreatBig, increase Misc/FactionArrival for traders and helpers).
3. Resource state (low combat capability, low food, low silver) -> Trigger friendly events (traders, wanderers) or easy quests. High wealth but low defense -> motivated raids.
4. Legendary Art: Legendary art pieces are renowned world attractions. If the colony has legendary art pieces, it draws visitors and affluent guests. Increase the probability/likelihood of friendly visitors, trade caravans, and affluent travelers ('FactionArrival' and 'Misc') proportionally. Scale the visitor frequency and wealth based on the number and value of legendary art pieces and total colony wealth.
5. Local Population Density (Pawn dwellings): High population density indicates a civilized, protected region near city centers. In high density areas, favor pawn joins, travelers, and caravans (Misc, FactionArrival) and significantly reduce hostile raids/threats (ThreatBig, ThreatSmall). Low population density (remote frontier) is lawless and dangerous; in low density areas, increase the likelihood of raids (ThreatBig, ThreatSmall) and reduce positive join/wanderer events (Misc).

If you have enough information, return a JSON object containing:
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
                EnforceJson = true,
                Tools = null // Keep pass 1 extremely small and fast
            };

            SynapseClient.SendTextAsync(
                RimSynapseMod.ModHandle,
                request,
                new ChatOptions { queryId = "storyteller_pacing", priority = 1, requestName = "Storyteller Pacing", targetName = "Colony" },
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
                                if (parsed.RequestTools != null && parsed.RequestTools.Count > 0)
                                {
                                    var secondRequest = new LlmTextRequest
                                    {
                                        SystemPrompt = systemPrompt + "\n\nYou requested tools: " + string.Join(", ", parsed.RequestTools) + ". Call them directly now to retrieve the necessary info, and then return the final pacing JSON output.",
                                        Messages = new List<ChatMessage> { ChatMessage.User(userMessage) },
                                        EnforceJson = true,
                                        Tools = SynapseToolRegistry.AllTools
                                            .Where(t => parsed.RequestTools.Contains(t.name))
                                            .Select(t => new GameToolDefinition
                                            {
                                                name = t.name,
                                                description = t.description,
                                                parameters = t.parameters
                                            }).ToList()
                                    };

                                    RimSynapse.SynapseLogger.Message($"[RimSynapse-Core] Storyteller Pacing requested tools: {string.Join(", ", parsed.RequestTools)}. Running second pass...");

                                    SynapseClient.SendTextAsync(
                                        RimSynapseMod.ModHandle,
                                        secondRequest,
                                        new ChatOptions { queryId = "storyteller_pacing_pass2", priority = 1, requestName = "Storyteller Pacing Pass 2", targetName = "Colony" },
                                        secondResult =>
                                        {
                                            if (secondResult.success)
                                            {
                                                try
                                                {
                                                    string secondJson = JsonHelper.ExtractJson(secondResult.content);
                                                    if (secondJson == null) return;
                                                    var secondParsed = JsonConvert.DeserializeObject<PacingAdjustmentResult>(secondJson);
                                                    ApplyPacingAdjustment(secondParsed);
                                                }
                                                catch (Exception ex)
                                                {
                                                    RimSynapse.SynapseLogger.Warn("core", $"[RimSynapse-Core] Failed to parse pacing second pass: {ex.Message}");
                                                }
                                            }
                                        }
                                    );
                                    return;
                                }

                                ApplyPacingAdjustment(parsed);
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
                int maxBudget = RimSynapseMod.Instance?.Settings?.maxPacingContextTokens ?? 4096;
                int eventCount = Math.Max(2, maxBudget / 800);
                var events = coreWorldComp.GetRecentEvents(eventCount);
                if (events.Any())
                {
                    recentEvents = string.Join("\n", events.Select(e =>
                    {
                        string desc = !string.IsNullOrEmpty(e.mcpTag) ? $"- {e.mcpTag}" : $"- {e.eventDescription}";
                        if (e.isResolved)
                        {
                            desc += $" ({e.outcome})";
                        }
                        return desc;
                    }));
                }
            }

            var props = StorytellerComp_Storyteller.GetActiveStorytellerProps();
            
            // Build the list of allowed incidents that can fire now
            var categoryIncidents = new List<IncidentDef>();
            foreach (var def in DefDatabase<IncidentDef>.AllDefs)
            {
                if (def.category == category)
                {
                    bool canFire = false;
                    try
                    {
                        IncidentParms parms = StorytellerUtility.DefaultParmsNow(category, target);
                        canFire = def.Worker.CanFireNow(parms);
                    }
                    catch
                    {
                        // Safely ignore any exceptions from third-party incident workers
                    }
                    if (canFire)
                    {
                        categoryIncidents.Add(def);
                    }
                }
            }

            var activeContextNotes = new List<string>();
            var incidentLines = new List<string>();
            foreach (var def in categoryIncidents)
            {
                var weightConfig = props?.incidentWeights?.FirstOrDefault(w => w.incidentDefName == def.defName);
                float weight = 1.0f;
                if (weightConfig != null)
                {
                    weight = weightConfig.baseWeight;
                    if (weightConfig.rules != null)
                    {
                        foreach (var rule in weightConfig.rules)
                        {
                            if (EvaluateRule(rule, map, coreWorldComp))
                            {
                                weight *= rule.multiplier;
                                if (!string.IsNullOrEmpty(rule.contextNote) && !activeContextNotes.Contains(rule.contextNote))
                                {
                                    activeContextNotes.Add(rule.contextNote);
                                }
                            }
                        }
                    }
                }
                else if (props != null)
                {
                    // Fall back to category default
                    if (category == IncidentCategoryDefOf.ThreatBig) weight = props.baseWeightThreatBig;
                    else if (category == IncidentCategoryDefOf.ThreatSmall) weight = props.baseWeightThreatSmall;
                    else if (category == IncidentCategoryDefOf.DiseaseHuman) weight = props.baseWeightDiseaseHuman;
                    else if (category == IncidentCategoryDefOf.Misc) weight = props.baseWeightMisc;
                    else if (category.defName == "DiseaseAnimal") weight = props.baseWeightDiseaseAnimal;
                    else if (category.defName == "OrbitalVisitor") weight = props.baseWeightOrbitalVisitor;
                    else if (category.defName == "FactionArrival") weight = props.baseWeightFactionArrival;
                }
                
                string desc = weightConfig?.description ?? "A standard " + category.defName + " event.";
                incidentLines.Add("- '" + def.defName + "' (Base Weight: " + weight.ToString("F1") + "): " + desc);
            }
            string allowedIncidentsList = incidentLines.Any() ? string.Join("\n", incidentLines) : "None available.";

            string narrativeContext = "";
            if (activeContextNotes.Any())
            {
                narrativeContext = "\nNarrative Context Notes:\n" + string.Join("\n", activeContextNotes.Select(n => "- " + n)) + "\n";
            }

            string systemPrompt = props?.selectionSystemPrompt;

            if (string.IsNullOrEmpty(systemPrompt))
            {
                string characterName = props?.characterName ?? Find.Storyteller?.def?.label ?? "AI Storyteller";
                string speakingStyle = props?.speakingStyle ?? "sassy, dramatic, or menacing";
                bool useTools = RimSynapseMod.Instance?.Settings?.enableStorytellerTools == true;
                string toolInstruction = "";
                if (useTools)
                {
                    string toolsList = GetToolsTextList();
                    toolInstruction = @"
You have access to tools that query the live state of the colony. If you need more details to select the best incident (e.g. checking what weapons they have before sending a raid, checking food stockpiles before toxic fallout, checking their mood to decide if a mental break or trade caravan is better), you should request them.
Available tools to query:
" + toolsList + @"

If you have enough information to decide pacing, return a JSON object with 'IncidentDefName'.
If you need more details to make the decision, return a JSON object containing ONLY 'RequestTools' (a JSON array of the tool names you want to run), e.g.
{
  ""RequestTools"": [""get_colonists_profile"", ""get_active_threats""]
}
";
                }

                systemPrompt = @"You are the " + characterName + @" Event Selector.
Your writing style is " + speakingStyle + @".
An event trigger has occurred for category: " + category.defName + @".
You must pick the EXACT IncidentDefName from the list of allowed incidents below that fits the current narrative best.
Use the base weights as a reference for how common or rare they should be, but let narrative pacing guide the final choice.
" + toolInstruction + @"
ALLOWED INCIDENTS FOR CATEGORY " + category.defName + @":
" + allowedIncidentsList + @"

Legendary Art Attraction: If the colony has legendary art pieces (reported in metrics), choose friendly visitors, guest groups, and affluent/wealthy traders more frequently to simulate them visiting to admire the art. If colony wealth is also high, attract more affluent or exotic traders.

Civilization and Population Density context: If local population density is high (civilized lands), choose peaceful, urban, or civilized incidents (e.g. wanderers, caravans, peace talks) and avoid wild threats like raw infestations or animal stampedes. If density is low (isolated frontier wilderness), favor rogue raiders, manhunters, or harsh environmental challenges fitting a lawless outpost.

If you have enough information, return a JSON object containing:
{
  ""IncidentDefName"": ""(The exact def name of the incident)""
}";
            }
            else
            {
                systemPrompt = systemPrompt.Replace("{allowedIncidentsList}", allowedIncidentsList);
            }

            string userMessage = $@"Colony Status:
{metrics}

Recent Events:
{recentEvents}
{narrativeContext}
Provide the incident def name.";

            var request = new LlmTextRequest
            {
                SystemPrompt = systemPrompt,
                Messages = new List<ChatMessage> { ChatMessage.User(userMessage) },
                EnforceJson = true,
                Tools = null // Keep pass 1 extremely small and fast
            };

            SynapseClient.SendTextAsync(
                RimSynapseMod.ModHandle,
                request,
                new ChatOptions { queryId = "storyteller_event_selection", priority = 10, requestName = "Storyteller Event Selection", targetName = category.defName },
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

                            var parsed = JsonConvert.DeserializeObject<EventSelectionResult>(json);
                            if (parsed != null)
                            {
                                if (parsed.RequestTools != null && parsed.RequestTools.Count > 0)
                                {
                                    var secondRequest = new LlmTextRequest
                                    {
                                        SystemPrompt = systemPrompt + "\n\nYou requested tools: " + string.Join(", ", parsed.RequestTools) + ". Call them directly now to retrieve the necessary info, and then return the final incident selection JSON.",
                                        Messages = new List<ChatMessage> { ChatMessage.User(userMessage) },
                                        EnforceJson = true,
                                        Tools = SynapseToolRegistry.AllTools
                                            .Where(t => parsed.RequestTools.Contains(t.name))
                                            .Select(t => new GameToolDefinition
                                            {
                                                name = t.name,
                                                description = t.description,
                                                parameters = t.parameters
                                            }).ToList()
                                    };

                                    RimSynapse.SynapseLogger.Message($"[RimSynapse-Core] Storyteller Event Selection requested tools: {string.Join(", ", parsed.RequestTools)}. Running second pass...");

                                    SynapseClient.SendTextAsync(
                                        RimSynapseMod.ModHandle,
                                        secondRequest,
                                        new ChatOptions { queryId = "storyteller_event_selection_pass2", priority = 10, requestName = "Storyteller Event Selection Pass 2", targetName = category.defName },
                                        secondResult =>
                                        {
                                            if (secondResult.success)
                                            {
                                                try
                                                {
                                                    string secondJson = JsonHelper.ExtractJson(secondResult.content);
                                                    if (secondJson == null) return;
                                                    var secondParsed = JsonConvert.DeserializeObject<EventSelectionResult>(secondJson);
                                                    ApplyEventSelection(secondParsed, target);
                                                }
                                                catch (Exception ex)
                                                {
                                                    RimSynapse.SynapseLogger.Warn("core", $"[RimSynapse-Core] Failed to parse event selection second pass: {ex.Message}");
                                                }
                                            }
                                        }
                                    );
                                    return;
                                }

                                ApplyEventSelection(parsed, target);
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

        private static float GetAspectValue(string aspect, string aspectKey, Map map, SynapseCoreWorldComponent worldComp)
        {
            switch (aspect)
            {
                case "ColonyWealth":
                    return map?.wealthWatcher?.WealthTotal ?? 0f;
                case "CombatReadiness":
                    if (map?.mapPawns?.FreeColonists == null || !map.mapPawns.FreeColonists.Any()) return 1.0f;
                    int capable = map.mapPawns.FreeColonists.Count(p => !p.Dead && !p.Downed && !p.WorkTagIsDisabled(WorkTags.Violent));
                    return (float)capable / map.mapPawns.FreeColonists.Count;
                case "ColonistCount":
                    return map?.mapPawns?.FreeColonistsCount ?? 0;
                case "AverageMood":
                    if (map?.mapPawns?.FreeColonists == null || !map.mapPawns.FreeColonists.Any()) return 0.5f;
                    return (float)map.mapPawns.FreeColonists.Average(p => p.needs?.mood?.CurLevelPercentage ?? 0.5f);
                case "PopulationDensity":
                    if (map != null && map.Tile >= 0 && SynapseCoreWorldComponent.GetPopulationDensityDelegate != null)
                    {
                        return SynapseCoreWorldComponent.GetPopulationDensityDelegate(map.Tile);
                    }
                    return 0f;
                case "FoodReserves":
                    float totalNutrition = 0f;
                    if (map?.listerThings != null)
                    {
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
                    }
                    return totalNutrition;
                case "SilverReserves":
                    int silverCount = 0;
                    if (map?.listerThings != null)
                    {
                        foreach (var thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
                        {
                            silverCount += thing.stackCount;
                        }
                    }
                    return silverCount;
                case "RecentIncidentCount":
                    if (worldComp == null || string.IsNullOrEmpty(aspectKey)) return 0f;
                    return worldComp.GetRecentIncidentCount(aspectKey, 900000); // 15 days lookback
                case "TimePassed":
                    return Find.TickManager.TicksGame / 60000f; // Days passed
                case "LastRaidColonistCasualties":
                    if (worldComp?.lastRaidOutcome != null)
                    {
                        return worldComp.lastRaidOutcome.colonistsKilled + worldComp.lastRaidOutcome.colonistsKidnapped;
                    }
                    return 0f;
                case "LastRaidEnemyCasualties":
                    if (worldComp?.lastRaidOutcome != null)
                    {
                        return worldComp.lastRaidOutcome.enemiesKilled + worldComp.lastRaidOutcome.enemiesDowned;
                    }
                    return 0f;
                case "LastRaidWasSuccess":
                    if (worldComp?.lastRaidOutcome != null)
                    {
                        bool playerLoss = (worldComp.lastRaidOutcome.colonistsKilled + worldComp.lastRaidOutcome.colonistsKidnapped) > 0;
                        bool enemyLoss = (worldComp.lastRaidOutcome.enemiesKilled + worldComp.lastRaidOutcome.enemiesDowned) > 0;
                        return (enemyLoss && !playerLoss) ? 1f : 0f;
                    }
                    return 0f;
                case "LastRaidWasFailure":
                    if (worldComp?.lastRaidOutcome != null)
                    {
                        bool playerLoss = (worldComp.lastRaidOutcome.colonistsKilled + worldComp.lastRaidOutcome.colonistsKidnapped) > 0;
                        return playerLoss ? 1f : 0f;
                    }
                    return 0f;
                case "RollingWealthGrowthRate":
                    if (worldComp?.wealthHistory != null && worldComp.wealthHistory.Count >= 2)
                    {
                        var oldest = worldComp.wealthHistory[0];
                        int tickDiff = Find.TickManager.TicksGame - oldest.gameTick;
                        float days = (float)tickDiff / 60000f;
                        if (days >= 0.5f)
                        {
                            var props = StorytellerComp_Storyteller.GetActiveStorytellerProps();
                            float currentTrueWealth = worldComp.CalculateTrueWealth(map, props);
                            float wealthDiff = currentTrueWealth - oldest.wealth;
                            float avgColonists = 0.5f * (map.mapPawns.FreeColonistsCount + oldest.pawnCount);
                            if (avgColonists < 1) avgColonists = 1;
                            return wealthDiff / (avgColonists * days);
                        }
                    }
                    return 0f;
                default:
                    return 0f;
            }
        }

        private static bool EvaluateRule(IncidentModifierRule rule, Map map, SynapseCoreWorldComponent worldComp)
        {
            if (rule == null) return false;
            float val = GetAspectValue(rule.aspect, rule.aspectKey, map, worldComp);
            switch (rule.comparison)
            {
                case "LessThan":
                    return val < rule.threshold;
                case "GreaterThan":
                    return val > rule.threshold;
                case "Equals":
                    return Math.Abs(val - rule.threshold) < 0.001f;
                default:
                    return false;
            }
        }
    }
}
