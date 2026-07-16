using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Tool handler: get_colony_moods
    /// Returns mood status, break risks, and negative thoughts for all colonists.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterMoodTools()
        {
            RegisterTool(
                "get_colony_moods",
                "Get mood status, breakdowns, and primary negative thoughts of the colony's colonists.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["compact"] = new Dictionary<string, object>
                        {
                            ["type"] = "boolean",
                            ["description"] = "If true, returns extremely compact details to save token costs by omitting list of negative thoughts."
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

                        if (compact)
                        {
                            colonistMoods.Add(new
                            {
                                name = pawn.LabelShort,
                                mood = (int)(pawn.needs.mood.CurLevelPercentage * 100),
                                risk = breakRisk != "None" ? breakRisk : null
                            });
                        }
                        else
                        {
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
                    }

                    return JsonConvert.SerializeObject(colonistMoods);
                }
            );
        }
    }
}
