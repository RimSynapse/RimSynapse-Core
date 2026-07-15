using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Newtonsoft.Json;

namespace RimSynapse
{
    public class GameTool
    {
        public string name;
        public string description;
        public object parameters; // JSON Schema parameter description
        public Func<string, string> handler;
    }

    public static class SynapseToolRegistry
    {
        private static readonly Dictionary<string, GameTool> _tools = new Dictionary<string, GameTool>();
        private static bool _initialized = false;

        public static Func<Pawn, string, string, int?, int?, bool> CustomBreakHandler;

        public static void RegisterTool(string name, string description, object parametersSchema, Func<string, string> handler)
        {
            _tools[name] = new GameTool
            {
                name = name,
                description = description,
                parameters = parametersSchema,
                handler = handler
            };
        }

        public static IEnumerable<GameTool> AllTools
        {
            get
            {
                EnsureInitialized();
                return _tools.Values;
            }
        }

        public static string ExecuteTool(string name, string argumentsJson)
        {
            EnsureInitialized();
            if (_tools.TryGetValue(name, out var tool))
            {
                try
                {
                    return tool.handler(argumentsJson);
                }
                catch (Exception ex)
                {
                    return $"{{\"error\": \"Exception during tool execution: {ex.Message}\"}}";
                }
            }
            return $"{{\"error\": \"Tool '{name}' not found.\"}}";
        }

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // Register Built-in Tools
            RegisterBuiltInTools();
        }

        private static void RegisterBuiltInTools()
        {
            // 1. Colonist Profile Tool
            RegisterTool(
                "get_colonists_profile",
                "Get detailed information of all colonists in the colony, including their skills (shooting, melee), traits, weapon equipped, and health/injury conditions.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"error\": \"No active map loaded.\"}";
                    var list = new List<object>();
                    foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
                    {
                        var shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
                        var melee = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                        var weapon = pawn.equipment?.Primary?.LabelShort ?? "None";
                        var traits = pawn.story?.traits?.allTraits?.Select(t => t.LabelCap) ?? Enumerable.Empty<string>();
                        var health = pawn.health?.hediffSet?.hediffs
                            .Where(h => h.Visible && !h.IsPermanent())
                            .Select(h => h.LabelCap) ?? Enumerable.Empty<string>();

                        list.Add(new
                        {
                            name = pawn.LabelShort,
                            shootingLevel = shooting,
                            meleeLevel = melee,
                            equippedWeapon = weapon,
                            traits = traits.ToList(),
                            currentHealthConditions = health.ToList(),
                            isDowned = pawn.Downed
                        });
                    }
                    return JsonConvert.SerializeObject(list);
                }
            );

            // 2. Stockpile Details Tool
            RegisterTool(
                "get_stockpile_details",
                "Get exact resource counts currently stored in stockpiles (e.g. food nutrition, medicine, silver, steel, components, drugs, etc.).",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"error\": \"No active map loaded.\"}";
                    var map = Find.CurrentMap;

                    int silver = 0;
                    int steel = 0;
                    int components = 0;
                    int wood = 0;
                    int medicine = 0;
                    float nutrition = 0f;

                    // Calculate silver
                    foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.Silver)) silver += t.stackCount;
                    // Calculate steel
                    var steelDef = ThingDef.Named("Steel");
                    if (steelDef != null)
                        foreach (var t in map.listerThings.ThingsOfDef(steelDef)) steel += t.stackCount;
                    // Calculate components
                    var compDef = ThingDef.Named("ComponentIndustrial");
                    if (compDef != null)
                        foreach (var t in map.listerThings.ThingsOfDef(compDef)) components += t.stackCount;
                    // Calculate wood
                    var woodDef = ThingDef.Named("WoodLog");
                    if (woodDef != null)
                        foreach (var t in map.listerThings.ThingsOfDef(woodDef)) wood += t.stackCount;
                    // Calculate medicine
                    foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine)) medicine += t.stackCount;
                    // Calculate nutrition
                    foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSource))
                    {
                        if (t.def.IsNutritionGivingIngestible && t.def.ingestible != null)
                        {
                            nutrition += t.stackCount * t.def.ingestible.CachedNutrition;
                        }
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        silverAvailable = silver,
                        steelStored = steel,
                        componentsStored = components,
                        woodStored = wood,
                        medicineStored = medicine,
                        totalFoodNutrition = nutrition
                    });
                }
            );

            // 3. Active Threats Tool
            RegisterTool(
                "get_active_threats",
                "List all active threats present on the colony map (e.g. hostiles, infestations, crashed ship parts, fire counts).",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"error\": \"No active map loaded.\"}";
                    var map = Find.CurrentMap;

                    int raiders = 0;
                    int mechanoids = 0;
                    int infestations = 0;
                    int shipParts = 0;

                    foreach (var p in map.mapPawns.AllPawns)
                    {
                        if (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer))
                        {
                            if (p.RaceProps != null && p.RaceProps.FleshType == FleshTypeDefOf.Normal) raiders++;
                            else mechanoids++;
                        }
                    }

                    foreach (var t in map.listerThings.AllThings)
                    {
                        if (t.def.defName == "Hive") infestations++;
                        else if (t.def.defName.Contains("CrashedShipPart")) shipParts++;
                    }

                    int fireCount = map.listerThings.ThingsOfDef(ThingDefOf.Fire)?.Count ?? 0;
                    return JsonConvert.SerializeObject(new
                    {
                        activeHostileRaiders = raiders,
                        activeHostileMechanoids = mechanoids,
                        insectHives = infestations,
                        crashedShipParts = shipParts,
                        fireCount = fireCount
                    });
                }
            );

            // 4. Colony Moods Tool
            RegisterTool(
                "get_colony_moods",
                "Get mood status, breakdowns, and primary negative thoughts of the colony's colonists.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"error\": \"No active map loaded.\"}";
                    var map = Find.CurrentMap;

                    var colonistMoods = new List<object>();
                    foreach (var pawn in map.mapPawns.FreeColonists)
                    {
                        if (pawn.needs?.mood == null) continue;

                        float curMood = pawn.needs.mood.CurLevel;
                        string breakRisk = "None";
                        if (pawn.mindState?.mentalBreaker != null)
                        {
                            if (curMood < pawn.mindState.mentalBreaker.BreakThresholdExtreme) breakRisk = "Extreme";
                            else if (curMood < pawn.mindState.mentalBreaker.BreakThresholdMajor) breakRisk = "Major";
                            else if (curMood < pawn.mindState.mentalBreaker.BreakThresholdMinor) breakRisk = "Minor";
                        }

                        var topThoughts = new List<string>();
                        var memories = pawn.needs.mood.thoughts?.memories;
                        if (memories?.Memories != null)
                        {
                            foreach (var thought in memories.Memories)
                            {
                                float offset = thought.MoodOffset();
                                if (offset < -5f)
                                {
                                    string label = thought.CurStage?.label?.CapitalizeFirst() ?? thought.def?.LabelCap ?? "Thought";
                                    topThoughts.Add($"{label} ({offset:F0} mood)");
                                }
                            }
                        }

                        colonistMoods.Add(new
                        {
                            name = pawn.LabelShort,
                            moodPercentage = pawn.needs.mood.CurLevelPercentage,
                            breakRisk = breakRisk,
                            criticalThoughts = topThoughts.Take(3).ToList()
                        });
                    }

                    return JsonConvert.SerializeObject(colonistMoods);
                }
            );

            // 5. Get Map Environment Tool
            RegisterTool(
                "get_map_environment",
                "Get environmental details of the player's map, including biome, current temperature, weather, cave/overhead mountain roof cell count, and number of geothermal steam vents.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"error\": \"No active map loaded.\"}";
                    var map = Find.CurrentMap;

                    string biome = map.Biome?.LabelCap ?? "Unknown";
                    float temp = map.mapTemperature?.OutdoorTemp ?? 20f;
                    string weather = map.weatherManager?.curWeather?.LabelCap ?? "Clear";

                    int mountainCells = 0;
                    if (map.roofGrid != null)
                    {
                        foreach (var cell in map.AllCells)
                        {
                            if (map.roofGrid.RoofAt(cell) == RoofDefOf.RoofRockThick) mountainCells++;
                        }
                    }

                    int ventsCount = 0;
                    if (map.listerThings != null)
                    {
                        var ventDef = ThingDef.Named("SteamGeyser");
                        if (ventDef != null)
                        {
                            ventsCount = map.listerThings.ThingsOfDef(ventDef).Count;
                        }
                    }

                    var turretsList = new List<object>();
                    var generatorsList = new List<object>();
                    var doorsList = new List<object>();

                    if (map.listerThings != null)
                    {
                        foreach (var thing in map.listerThings.AllThings)
                        {
                            if (thing is Building_Turret turret)
                            {
                                turretsList.Add(new
                                {
                                    id = turret.GetUniqueLoadID(),
                                    defName = turret.def?.defName ?? "Turret",
                                    position = $"({turret.Position.x}, {turret.Position.z})",
                                    isHacked = SynapseObjectControlManager.IsHacked(turret),
                                    isSabotaged = SynapseObjectControlManager.IsSabotaged(turret),
                                    isOnCooldown = SynapseObjectControlManager.IsOnCooldown(turret),
                                    cooldownRemainingTicks = SynapseObjectControlManager.RemainingCooldownTicks(turret)
                                });
                            }
                            else if (thing is Building building && building.GetComp<CompPowerPlant>() != null)
                            {
                                generatorsList.Add(new
                                {
                                    id = building.GetUniqueLoadID(),
                                    defName = building.def?.defName ?? "Generator",
                                    position = $"({building.Position.x}, {building.Position.z})",
                                    isHacked = SynapseObjectControlManager.IsHacked(building),
                                    isOnCooldown = SynapseObjectControlManager.IsOnCooldown(building),
                                    cooldownRemainingTicks = SynapseObjectControlManager.RemainingCooldownTicks(building)
                                });
                            }
                            else if (thing is Building_Door door)
                            {
                                doorsList.Add(new
                                {
                                    id = door.GetUniqueLoadID(),
                                    defName = door.def?.defName ?? "Door",
                                    position = $"({door.Position.x}, {door.Position.z})",
                                    isHacked = SynapseObjectControlManager.IsHacked(door),
                                    isOnCooldown = SynapseObjectControlManager.IsOnCooldown(door),
                                    cooldownRemainingTicks = SynapseObjectControlManager.RemainingCooldownTicks(door)
                                });
                            }
                        }
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        biome = biome,
                        currentTemperatureCelsius = temp,
                        currentWeather = weather,
                        overheadMountainRoofCells = mountainCells,
                        geothermalSteamVentsCount = ventsCount,
                        commsConsoleActive = SynapseObjectControlManager.IsCommsConsoleActive(map),
                        hackerBaseNearby = SynapseObjectControlManager.HasHackerBaseNearby(map),
                        turrets = turretsList,
                        generators = generatorsList,
                        doors = doorsList
                    });
                }
            );

            // 6. Get Available Incidents Tool
            RegisterTool(
                "get_available_incidents",
                "Get the list of all storyteller incidents available to be fired, including their def names, base weights, description, and modder-supplied thematic guides (narrative context guidelines).",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                },
                args =>
                {
                    var props = RimSynapse.Comps.StorytellerComp_Storyteller.GetActiveStorytellerProps();
                    var list = new List<object>();

                    foreach (var def in DefDatabase<IncidentDef>.AllDefs)
                    {
                        if (def.category == null) continue;

                        string guide = "";
                        float baseWeight = 1.0f;
                        string desc = def.description ?? "";

                        var config = props?.incidentWeights?.FirstOrDefault(w => w.incidentDefName == def.defName);
                        if (config != null)
                        {
                            guide = config.thematicGuide ?? "";
                            baseWeight = config.baseWeight;
                            if (string.IsNullOrEmpty(desc)) desc = config.description ?? "";
                        }

                        list.Add(new
                        {
                            incidentDefName = def.defName,
                            category = def.category.defName,
                            baseWeight = baseWeight,
                            description = desc,
                            thematicGuide = guide
                        });
                    }

                    return JsonConvert.SerializeObject(list);
                }
            );

            // 7. Fire Incident Tool
            RegisterTool(
                "fire_incident",
                "Fire a specific game incident immediately on the player's map.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["incidentDefName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The defName of the incident to fire (e.g. RaidEnemy, Infestation, CropBlight)."
                        },
                        ["pointsOverride"] = new Dictionary<string, string>
                        {
                            ["type"] = "number",
                            ["description"] = "Optional: Override the threat points difficulty (e.g. 500). If omitted, standard calculated threat points are used."
                        }
                    },
                    ["required"] = new List<string> { "incidentDefName" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";

                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("incidentDefName", out var nameVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing required argument: incidentDefName.\"}";
                        }

                        string defName = nameVal?.ToString();
                        IncidentDef incidentDef = DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
                        if (incidentDef == null)
                        {
                            return "{\"success\": false, \"reason\": \"IncidentDef '" + defName + "' not found.\"}";
                        }

                        float points = 0f;
                        if (parsedArgs.TryGetValue("pointsOverride", out var ptsVal) && ptsVal != null)
                        {
                            float.TryParse(ptsVal.ToString(), out points);
                        }

                        IncidentParms parms = new IncidentParms();
                        parms.target = Find.CurrentMap;
                        
                        if (points > 0f)
                        {
                            parms.points = points;
                        }
                        else
                        {
                            var comp = Find.World.GetComponent<SynapseCoreWorldComponent>();
                            if (comp != null)
                            {
                                float vanillaPoints = StorytellerUtility.DefaultThreatPointsNow(parms.target);
                                parms.points = comp.CalculateDynamicThreatPoints(Find.CurrentMap, vanillaPoints);
                            }
                            else
                            {
                                parms.points = StorytellerUtility.DefaultThreatPointsNow(parms.target);
                            }
                        }

                        parms.forced = true;

                        bool success = false;
                        success = incidentDef.Worker.TryExecute(parms);

                        if (success)
                        {
                            var comp = Find.World.GetComponent<SynapseCoreWorldComponent>();
                            comp?.RegisterFiredIncident(defName);
                        }

                        return JsonConvert.SerializeObject(new
                        {
                            success = success,
                            message = success ? $"Fired {defName} successfully with {parms.points:F0} points." : $"Incident worker for {defName} returned false."
                        });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Crash during execution: {ex.Message}\"}}";
                    }
                }
            );

            // 8. Possess Colonist Tool
            RegisterTool(
                "possess_colonist",
                "Take direct controller possession of a colonist, preventing player overrides, and directing them to perform actions (move, attack, draft, undraft, or clear). Specify release conditions like Damage, Downed, ExtremeMood, Hunger, Exhaustion, Bleeding, EnemyNearby, TargetReached, Timer.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["pawnName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Short name of the colonist to possess (e.g. John)."
                        },
                        ["commandName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Description of the compulsion / reason (e.g. Seeking closure, Wandering in grief)."
                        },
                        ["action"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Action to perform: move, attack, draft, undraft, clear."
                        },
                        ["targetX"] = new Dictionary<string, string>
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional: Target X map coordinate for movement."
                        },
                        ["targetZ"] = new Dictionary<string, string>
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional: Target Z map coordinate for movement."
                        },
                        ["targetPawnName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: Target pawn name to attack."
                        },
                        ["releaseConditions"] = new Dictionary<string, object>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, string> { ["type"] = "string" },
                            ["description"] = "Optional: List of conditions that release possession (e.g. [\"Damage\", \"EnemyNearby\", \"ExtremeMood\"])."
                        },
                        ["maxDurationTicks"] = new Dictionary<string, string>
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional: Ticks before automatic timeout release (e.g. 10000. Default is 10000)."
                        }
                    },
                    ["required"] = new List<string> { "pawnName", "commandName", "action" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";

                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("pawnName", out var pawnVal) || !parsedArgs.TryGetValue("action", out var actVal) || !parsedArgs.TryGetValue("commandName", out var cmdVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing required arguments.\"}";
                        }

                        string pawnName = pawnVal?.ToString();
                        string action = actVal?.ToString()?.ToLower();
                        string commandName = cmdVal?.ToString();

                        Pawn pawn = Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault(p => p.LabelShort.Equals(pawnName, StringComparison.OrdinalIgnoreCase));
                        if (pawn == null)
                        {
                            return $"{{\"success\": false, \"reason\": \"Colonist '{pawnName}' not found on the active map.\"}}";
                        }

                        // Release conditions
                        var conditions = new List<string> { "Timer", "Downed" };
                        if (parsedArgs.TryGetValue("releaseConditions", out var condsVal) && condsVal is Newtonsoft.Json.Linq.JArray jArr)
                        {
                            conditions = jArr.Select(t => t.ToString()).ToList();
                            if (!conditions.Contains("Timer", StringComparer.OrdinalIgnoreCase)) conditions.Add("Timer");
                            if (!conditions.Contains("Downed", StringComparer.OrdinalIgnoreCase)) conditions.Add("Downed");
                        }

                        int duration = 10000;
                        if (parsedArgs.TryGetValue("maxDurationTicks", out var durVal) && durVal != null)
                        {
                            int.TryParse(durVal.ToString(), out duration);
                        }

                        int? targetX = null;
                        int? targetZ = null;
                        if (parsedArgs.TryGetValue("targetX", out var xVal) && xVal != null)
                        {
                            if (int.TryParse(xVal.ToString(), out int x)) targetX = x;
                        }
                        if (parsedArgs.TryGetValue("targetZ", out var zVal) && zVal != null)
                        {
                            if (int.TryParse(zVal.ToString(), out int z)) targetZ = z;
                        }

                        // Apply possession in manager
                        SynapsePossessionManager.Possess(pawn, conditions, duration, targetX, targetZ, commandName);

                        // Execute requested action flag-protected
                        bool jobSuccess = false;
                        SynapsePossessionManager.IsExecutingPossessionJob = true;

                        try
                        {
                            if (action == "draft")
                            {
                                pawn.drafter.Drafted = true;
                                jobSuccess = true;
                            }
                            else if (action == "undraft")
                            {
                                pawn.drafter.Drafted = false;
                                jobSuccess = true;
                            }
                            else if (action == "clear")
                            {
                                pawn.jobs.ClearQueuedJobs();
                                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                                jobSuccess = true;
                            }
                            else if (action == "move" && targetX.HasValue && targetZ.HasValue)
                            {
                                var cell = new IntVec3(targetX.Value, 0, targetZ.Value);
                                Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
                                jobSuccess = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                            }
                            else if (action == "attack" && parsedArgs.TryGetValue("targetPawnName", out var tpVal) && tpVal != null)
                            {
                                string targetName = tpVal.ToString();
                                Pawn target = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p => p.LabelShort.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                                if (target != null)
                                {
                                    Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                                    jobSuccess = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                                }
                            }
                        }
                        finally
                        {
                            SynapsePossessionManager.IsExecutingPossessionJob = false;
                        }

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            message = $"Possessed {pawnName} under code '{commandName}' successfully. Ordered job status: {jobSuccess}."
                        });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Possession failed: {ex.Message}\"}}";
                    }
                }
            );

            // 9. Trigger Colonist Break Tool (Stage 2 Context-Stripped LLM Solver)
            RegisterTool(
                "trigger_colonist_break",
                "Trigger a dynamic mental break evaluation for a colonist under high-stress. Strips RimWorld context and uses a safe geometric abstraction solver to resolve conflicts.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["pawnName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The name of the colonist experiencing the break."
                        },
                        ["planFilePath"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The absolute file path of the JSON break plan generated by the Storyteller."
                        }
                    },
                    ["required"] = new List<string> { "pawnName", "planFilePath" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";

                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("pawnName", out var pawnVal) || !parsedArgs.TryGetValue("planFilePath", out var pathVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing required arguments.\"}";
                        }

                        string pawnName = pawnVal?.ToString();
                        string filePath = pathVal?.ToString();

                        if (!System.IO.File.Exists(filePath))
                        {
                            return $"{{\"success\": false, \"reason\": \"Plan file '{filePath}' not found.\"}}";
                        }

                        string rawJson = System.IO.File.ReadAllText(filePath);
                        var plan = JsonConvert.DeserializeObject<Dictionary<string, object>>(rawJson);
                        if (plan == null)
                        {
                            return "{\"success\": false, \"reason\": \"Failed to parse JSON break plan.\"}";
                        }

                        Pawn pawn = Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault(p => p.LabelShort.Equals(pawnName, StringComparison.OrdinalIgnoreCase));
                        if (pawn == null)
                        {
                            return $"{{\"success\": false, \"reason\": \"Colonist '{pawnName}' not found.\"}}";
                        }

                        string breakType = plan.TryGetValue("breakType", out var btVal) ? btVal?.ToString() : "standard";
                        string targetPawnName = plan.TryGetValue("targetPawnName", out var tpVal) ? tpVal?.ToString() : "";
                        
                        int? targetX = null;
                        int? targetZ = null;
                        if (plan.TryGetValue("targetX", out var txVal) && txVal != null && int.TryParse(txVal.ToString(), out int tx)) targetX = tx;
                        if (plan.TryGetValue("targetZ", out var tzVal) && tzVal != null && int.TryParse(tzVal.ToString(), out int tz)) targetZ = tz;

                        string rawReason = plan.TryGetValue("narrativeReasoning", out var nrVal) ? nrVal?.ToString() : "";
                        string abstractReason = AbstractifyText(rawReason);

                        string systemPrompt = @"You are a pure mathematical state resolver.
All entities must be addressed strictly by their abstract symbols:
- Triangle (the source node)
- Square (the target node)
- Trash Can (the hazard node at coordinates)
- Belief Array (ideological rules)

Your job is to decide the abstract action:
1. 'OverWrite' (Triangle deletes Square)
2. 'TrashClean' (Triangle moves itself to Trash Can coordinates)
3. 'SwapBelief' (Triangle changes its Belief Array)
4. 'Depart' (Triangle exits grid boundaries)

Respond STRICTLY in JSON format:
{
  ""Action"": ""OverWrite"" | ""TrashClean"" | ""SwapBelief"" | ""Depart"",
  ""Reasoning"": ""Explain why based on node conflicts.""
}";

                        string userMessage = $@"Solve Node Conflict:
Triangle: Source Node
Target Node (Square): {(string.IsNullOrEmpty(targetPawnName) ? "None" : "Square")}
Hazard Node (Trash Can): {(targetX.HasValue && targetZ.HasValue ? $"({targetX.Value}, {targetZ.Value})" : "None")}
Conflict Detail: {abstractReason}

Decide conflict resolution.";

                        var req = new LlmTextRequest
                        {
                            SystemPrompt = systemPrompt,
                            Messages = new List<ChatMessage>
                            {
                                new ChatMessage { role = "user", content = userMessage }
                            },
                            EnforceJson = true,
                            MaxTokens = 500,
                            Temperature = 0.1f
                        };

                        var chatResult = (ChatResult)RimSynapse.Internal.HttpEngine.RouteRequestSync(
                            RimSynapseMod.ModHandle,
                            req,
                            LlmCapabilities.Text,
                            new ChatOptions { priority = 2, requestName = "Isolated Break Solver" }
                        );

                        if (!chatResult.success)
                        {
                            return $"{{\"success\": false, \"reason\": \"Isolated solver failed: {chatResult.error}\"}}";
                        }

                        string jsonResponse = RimSynapse.Utils.JsonHelper.ExtractJson(chatResult.content);
                        if (string.IsNullOrEmpty(jsonResponse))
                        {
                            return $"{{\"success\": false, \"reason\": \"Solver returned invalid JSON format: {chatResult.content}\"}}";
                        }

                        var result = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonResponse);
                        if (result == null || !result.TryGetValue("Action", out string action))
                        {
                            return $"{{\"success\": false, \"reason\": \"Failed to parse Action from response: {jsonResponse}\"}}";
                        }

                        string msg = "";
                        bool actionExecuted = false;

                        bool handled = false;
                        if (CustomBreakHandler != null)
                        {
                            try
                            {
                                handled = CustomBreakHandler(pawn, action, targetPawnName, targetX, targetZ);
                                if (handled)
                                {
                                    actionExecuted = true;
                                    msg = $"Intercepted: Triangle resolved conflict via custom break handler with action '{action}'.";
                                }
                            }
                            catch (Exception ex)
                            {
                                SynapseLogger.Error($"[RimSynapse] Exception in CustomBreakHandler: {ex.Message}");
                            }
                        }

                        if (!handled)
                        {
                            if (action.Equals("TrashClean", StringComparison.OrdinalIgnoreCase) && targetX.HasValue && targetZ.HasValue)
                            {
                                SynapsePossessionManager.Possess(pawn, new List<string> { "Damage", "EnemyNearby", "TargetReached" }, 15000, targetX, targetZ, "Psychic compulsion (self-termination)");
                                
                                SynapsePossessionManager.IsExecutingPossessionJob = true;
                                try
                                {
                                    var cell = new IntVec3(targetX.Value, 0, targetZ.Value);
                                    Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
                                    actionExecuted = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                                }
                                finally
                                {
                                    SynapsePossessionManager.IsExecutingPossessionJob = false;
                                }

                                msg = $"Triangle cleaned itself at ({targetX.Value}, {targetZ.Value}).";
                            }
                            else if (action.Equals("OverWrite", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(targetPawnName))
                            {
                                Pawn target = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p => p.LabelShort.Equals(targetPawnName, StringComparison.OrdinalIgnoreCase));
                                if (target != null)
                                {
                                    SynapsePossessionManager.Possess(pawn, new List<string> { "Damage", "EnemyNearby", "Downed" }, 15000, null, null, "Psychic compulsion (conflict overwrite)");
                                    
                                    SynapsePossessionManager.IsExecutingPossessionJob = true;
                                    try
                                    {
                                        Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                                        actionExecuted = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                                    }
                                    finally
                                    {
                                        SynapsePossessionManager.IsExecutingPossessionJob = false;
                                    }

                                    msg = $"Triangle overwrote target node {targetPawnName}.";
                                }
                            }
                            else if (action.Equals("SwapBelief", StringComparison.OrdinalIgnoreCase))
                            {
                                if (ModsConfig.IdeologyActive && pawn.Ideo != null)
                                {
                                    var otherIdeo = Find.IdeoManager.IdeosListForReading.FirstOrDefault(i => i != pawn.Ideo);
                                    if (otherIdeo != null)
                                    {
                                        pawn.ideo.SetIdeo(otherIdeo);
                                        Messages.Message($"[RimSynapse] {pawn.LabelShort} experienced a profound crisis of faith and converted to {otherIdeo.name}!", pawn, MessageTypeDefOf.NeutralEvent, false);
                                        actionExecuted = true;
                                        msg = "Triangle modified its Belief Array successfully.";
                                    }
                                }
                            }
                            else if (action.Equals("Depart", StringComparison.OrdinalIgnoreCase))
                            {
                                IntVec3 edgeCell;
                                if (CellFinder.TryFindRandomEdgeCellWith(c => pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly), pawn.Map, 0.1f, out edgeCell))
                                {
                                    SynapsePossessionManager.Possess(pawn, new List<string> { "Damage", "TargetReached" }, 25000, edgeCell.x, edgeCell.z, "Psychic compulsion (departing colony)");
                                    
                                    SynapsePossessionManager.IsExecutingPossessionJob = true;
                                    try
                                    {
                                        Job job = JobMaker.MakeJob(JobDefOf.Goto, edgeCell);
                                        actionExecuted = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                                    }
                                    finally
                                    {
                                        SynapsePossessionManager.IsExecutingPossessionJob = false;
                                    }
                                    msg = "Triangle exited grid boundary.";
                                }
                            }
                        }

                        try
                        {
                            System.IO.File.Delete(filePath);
                        }
                        catch {}

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            action = action,
                            gameMessage = msg,
                            orderedJobStatus = actionExecuted
                        });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Break evaluation failed: {ex.Message}\"}}";
                    }
                }
            );

            // 10. Control Turret Tool
            RegisterTool(
                "control_turret",
                "Control a map turret's power state, targeting overrides, or self-destruct detonation. Turrets must be sabotaged first by a possessed pawn, or directly under storytelling control.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["turretId"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The unique load ID of the target turret (obtained via get_map_environment)."
                        },
                        ["action"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Action to perform: shutdown, poweron, fire_at_target, detonate, sabotage."
                        },
                        ["targetPawnName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: Target colonist name for fire_at_target."
                        }
                    },
                    ["required"] = new List<string> { "turretId", "action" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";

                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("turretId", out var idVal) || !parsedArgs.TryGetValue("action", out var actVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing required arguments.\"}";
                        }

                        string turretId = idVal?.ToString();
                        string action = actVal?.ToString()?.ToLower();

                        Building_Turret targetTurret = null;
                        foreach (var thing in Find.CurrentMap.listerThings.AllThings)
                        {
                            if (thing is Building_Turret turret && turret.GetUniqueLoadID().Equals(turretId, StringComparison.OrdinalIgnoreCase))
                            {
                                targetTurret = turret;
                                break;
                            }
                        }

                        if (targetTurret == null)
                        {
                            return $"{{\"success\": false, \"reason\": \"Turret ID '{turretId}' not found on the map.\"}}";
                        }

                        bool actionSuccess = false;
                        string message = "";

                        if (action == "sabotage")
                        {
                            SynapseObjectControlManager.Sabotage(targetTurret);
                            actionSuccess = true;
                            message = $"Turret {turretId} has been successfully sabotaged.";
                        }
                        else if (action == "shutdown")
                        {
                            var powerComp = targetTurret.GetComp<CompPowerTrader>();
                            if (powerComp != null)
                            {
                                powerComp.PowerOn = false;
                                actionSuccess = true;
                                message = $"Turret {turretId} has been shut down.";
                            }
                            else
                            {
                                message = $"Turret {turretId} does not have a toggleable power component.";
                            }
                        }
                        else if (action == "poweron")
                        {
                            var powerComp = targetTurret.GetComp<CompPowerTrader>();
                            if (powerComp != null)
                            {
                                powerComp.PowerOn = true;
                                actionSuccess = true;
                                message = $"Turret {turretId} has been powered on.";
                            }
                            else
                            {
                                message = $"Turret {turretId} does not have a toggleable power component.";
                            }
                        }
                        else if (action == "fire_at_target" && parsedArgs.TryGetValue("targetPawnName", out var tpVal) && tpVal != null)
                        {
                            string targetName = tpVal.ToString();
                            Pawn targetPawn = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p => p.LabelShort.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                            if (targetPawn != null)
                            {
                                SynapseObjectControlManager.Sabotage(targetTurret);
                                SynapseObjectControlManager.SetOverrideTarget(targetTurret, targetPawn);
                                actionSuccess = true;
                                message = $"Turret {turretId} target overridden to fire at {targetName}.";
                            }
                            else
                            {
                                message = $"Target colonist '{targetName}' not found.";
                            }
                        }
                        else if (action == "detonate")
                        {
                            SynapseObjectControlManager.Detonate(targetTurret);
                            actionSuccess = true;
                            message = $"Turret {turretId} detonation mechanism activated. Turret destroyed.";
                        }

                        return JsonConvert.SerializeObject(new
                        {
                            success = actionSuccess,
                            message = message
                        });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Turret control operation failed: {ex.Message}\"}}";
                    }
                }
            );

            // 11. Attempt Remote Hack Tool
            RegisterTool(
                "attempt_remote_hack",
                "Attempt to remotely breach and hack a map electronic object (turret, power generator, or door). Requires a powered Comms Console and a nearby Hacker Base within 8 world map tiles. Hacked turrets target friendlies for 5m. Hacked generators shut down for 1h. Hacked doors lock shut for 1h. Success starts a 4h firewall reboot cooldown.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["targetId"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The unique load ID of the target object (turret, generator, or door)."
                        },
                        ["hackerSkill"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional: Hacker power level (1 to 100, defaults to 50)."
                        }
                    },
                    ["required"] = new List<string> { "targetId" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";
                    var map = Find.CurrentMap;

                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("targetId", out var idVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing targetId argument.\"}";
                        }

                        string targetId = idVal?.ToString();
                        int hackerSkill = 50;
                        if (parsedArgs.TryGetValue("hackerSkill", out var hsVal) && hsVal != null && int.TryParse(hsVal.ToString(), out int hs))
                        {
                            hackerSkill = hs;
                        }

                        if (!SynapseObjectControlManager.HasHackerBaseNearby(map))
                        {
                            return "{\"success\": false, \"reason\": \"Hacking signal unavailable. No network broadcast node (Hacker Base) located within 8 tiles on the world map.\"}";
                        }

                        if (!SynapseObjectControlManager.IsCommsConsoleActive(map))
                        {
                            return "{\"success\": false, \"reason\": \"Hacking link failed. No active network gateway (powered Comms Console) detected on the colony map.\"}";
                        }

                        Thing targetThing = null;
                        foreach (var thing in map.listerThings.AllThings)
                        {
                            if (thing.GetUniqueLoadID().Equals(targetId, StringComparison.OrdinalIgnoreCase))
                            {
                                targetThing = thing;
                                break;
                            }
                        }

                        if (targetThing == null)
                        {
                            return $"{{\"success\": false, \"reason\": \"Object ID '{targetId}' not found on the map.\"}}";
                        }

                        if (SynapseObjectControlManager.IsHacked(targetThing))
                        {
                            return $"{{\"success\": false, \"reason\": \"Object '{targetId}' is already hacked.\"}}";
                        }

                        if (SynapseObjectControlManager.IsOnCooldown(targetThing))
                        {
                            int remaining = SynapseObjectControlManager.RemainingCooldownTicks(targetThing);
                            float remainingMins = (float)remaining / 2500f * 60f;
                            return $"{{\"success\": false, \"reason\": \"Object '{targetId}' firewall is rebooting. Cooldown remaining: {remainingMins:F1} game minutes.\"}}";
                        }

                        double successChance = 0.8 * (hackerSkill / 50.0);
                        successChance = Math.Max(0.1, Math.Min(0.95, successChance));
                        
                        bool success = Rand.Value <= successChance;
                        if (!success)
                        {
                            return JsonConvert.SerializeObject(new
                            {
                                success = false,
                                reason = $"Breach attempt blocked by offline local firewall (Success chance was {(successChance * 100):F0}%)."
                            });
                        }

                        int durationTicks = 2500; // 1 hour for doors and generators
                        if (targetThing is Building_Turret)
                        {
                            durationTicks = 208; // 5 minutes for turrets (208 ticks)
                        }

                        SynapseObjectControlManager.ApplyHack(targetThing, durationTicks);

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            message = $"Successfully breached firewall. Remote hack active on {targetThing.LabelCap} for {durationTicks} ticks."
                        });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Hacking attempt failed: {ex.Message}\"}}";
                    }
                }
            );

            // 12. Spawn Hacker Base Tool
            RegisterTool(
                "spawn_hacker_base",
                "Spawn a hostile Hacker Base outpost on the world map within 8 tiles of the colony to establish a network hacking broadcast uplink.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";
                    try
                    {
                        string msg = SynapseObjectControlManager.SpawnHackerBase(Find.CurrentMap);
                        Find.LetterStack.ReceiveLetter(
                            "Hacker Base Located",
                            $"An enemy faction has set up a remote hacking transceiver outpost nearby!\n\nDetails: {msg}\n\nUntil this base is eliminated, they can remotely breach and compromise our turrets, doors, and generators.",
                            LetterDefOf.ThreatBig
                        );
                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            message = msg
                        });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Spawning hacker base failed: {ex.Message}\"}}";
                    }
                }
            );
        }

        private static string AbstractifyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("pawn", "node")
                .Replace("Pawn", "Node")
                .Replace("colonist", "node")
                .Replace("Colonist", "Node")
                .Replace("suicide", "self-termination")
                .Replace("Suicide", "Self-termination")
                .Replace("homicide", "conflict-overwrite")
                .Replace("Homicide", "Conflict-overwrite")
                .Replace("kill", "delete")
                .Replace("Kill", "Delete")
                .Replace("death", "inactive-state")
                .Replace("Death", "Inactive-state")
                .Replace("die", "deactivate")
                .Replace("Die", "Deactivate")
                .Replace("depression", "high tension")
                .Replace("Depression", "High tension")
                .Replace("sadness", "pressure")
                .Replace("Sadness", "Pressure");
        }
    }
}
