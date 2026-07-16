using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Tool handlers: get_available_incidents and fire_incident.
    /// Provides incident browsing and direct incident firing for the storyteller.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterIncidentTools()
        {
            // Get Available Incidents Tool
            RegisterTool(
                "get_available_incidents",
                "Get the list of all storyteller incidents available to be fired, including their def names, base weights, description, and modder-supplied thematic guides (narrative context guidelines).",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["compact"] = new Dictionary<string, object>
                        {
                            ["type"] = "boolean",
                            ["description"] = "If true, returns extremely compact details to save token costs by omitting description and thematicGuide text."
                        }
                    }
                },
                args =>
                {
                    bool compact = false;
                    try
                    {
                        var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (dict != null && dict.TryGetValue("compact", out var val) && val is bool b)
                        {
                            compact = b;
                        }
                    }
                    catch {}

                    var props = RimSynapse.Comps.StorytellerComp_Storyteller.GetActiveStorytellerProps();
                    var list = new List<object>();

                    foreach (var def in DefDatabase<IncidentDef>.AllDefs)
                    {
                        if (def.category == null) continue;

                        float baseWeight = 1.0f;
                        var config = props?.incidentWeights?.FirstOrDefault(w => w.incidentDefName == def.defName);
                        if (config != null)
                        {
                            baseWeight = config.baseWeight;
                        }

                        if (compact)
                        {
                            list.Add(new
                            {
                                def = def.defName,
                                cat = def.category.defName,
                                weight = baseWeight
                            });
                        }
                        else
                        {
                            string desc = def.description ?? "";
                            string guide = "";
                            if (config != null)
                            {
                                guide = config.thematicGuide ?? "";
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
                    }

                    return JsonConvert.SerializeObject(list);
                }
            );

            // Fire Incident Tool
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
        }
    }
}
