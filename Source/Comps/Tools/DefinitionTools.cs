using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Newtonsoft.Json;

namespace RimSynapse
{
    public static partial class SynapseToolRegistry
    {
        private class SynapseIndexedDef
        {
            public string defName;
            public string label;
            public string description;
            public string defType;
        }

        private static List<SynapseIndexedDef> _indexedDefs = null;

        private static void EnsureDefsIndexed()
        {
            if (_indexedDefs != null) return;

            _indexedDefs = new List<SynapseIndexedDef>();

            try
            {
                // Index ThingDefs (weapons, apparel, items, buildings, pawns)
                foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (def == null) continue;
                    _indexedDefs.Add(new SynapseIndexedDef
                    {
                        defName = def.defName,
                        label = def.label ?? "",
                        description = def.description ?? "",
                        defType = "ThingDef"
                    });
                }

                // Index HediffDefs (injuries, diseases, implants)
                foreach (var def in DefDatabase<HediffDef>.AllDefsListForReading)
                {
                    if (def == null) continue;
                    _indexedDefs.Add(new SynapseIndexedDef
                    {
                        defName = def.defName,
                        label = def.label ?? "",
                        description = def.description ?? "",
                        defType = "HediffDef"
                    });
                }

                // Index TraitDefs (traits)
                foreach (var def in DefDatabase<TraitDef>.AllDefsListForReading)
                {
                    if (def == null) continue;
                    _indexedDefs.Add(new SynapseIndexedDef
                    {
                        defName = def.defName,
                        label = def.label ?? "",
                        description = def.description ?? "",
                        defType = "TraitDef"
                    });
                }

                // Index ThoughtDefs (moods, memories)
                foreach (var def in DefDatabase<ThoughtDef>.AllDefsListForReading)
                {
                    if (def == null) continue;
                    _indexedDefs.Add(new SynapseIndexedDef
                    {
                        defName = def.defName,
                        label = def.label ?? "",
                        description = def.description ?? "",
                        defType = "ThoughtDef"
                    });
                }

                // Index IncidentDefs (raids, events)
                foreach (var def in DefDatabase<IncidentDef>.AllDefsListForReading)
                {
                    if (def == null) continue;
                    _indexedDefs.Add(new SynapseIndexedDef
                    {
                        defName = def.defName,
                        label = def.label ?? "",
                        description = def.description ?? "",
                        defType = "IncidentDef"
                    });
                }

                // Index BiomeDefs
                foreach (var def in DefDatabase<BiomeDef>.AllDefsListForReading)
                {
                    if (def == null) continue;
                    _indexedDefs.Add(new SynapseIndexedDef
                    {
                        defName = def.defName,
                        label = def.label ?? "",
                        description = def.description ?? "",
                        defType = "BiomeDef"
                    });
                }

                // Index NeedDefs
                foreach (var def in DefDatabase<NeedDef>.AllDefsListForReading)
                {
                    if (def == null) continue;
                    _indexedDefs.Add(new SynapseIndexedDef
                    {
                        defName = def.defName,
                        label = def.label ?? "",
                        description = def.description ?? "",
                        defType = "NeedDef"
                    });
                }

                // Index MentalStateDefs
                foreach (var def in DefDatabase<MentalStateDef>.AllDefsListForReading)
                {
                    if (def == null) continue;
                    _indexedDefs.Add(new SynapseIndexedDef
                    {
                        defName = def.defName,
                        label = def.label ?? "",
                        description = def.description ?? "",
                        defType = "MentalStateDef"
                    });
                }
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLogger.Error($"[RimSynapse] Error compiling active Defs index: {ex.Message}");
            }
        }

        private static void RegisterDefinitionTools()
        {
            RegisterTool(
                "search_game_definitions",
                "Queries the dynamically compiled list of game object definitions (ThingDefs, HediffDefs, TraitDefs, etc.) to map common human terms to exact C# DefNames.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["query"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The keyword or concept to search for (e.g. 'shotgun', 'infection', 'bipolar', 'flu')."
                        },
                        ["defType"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: Filter search to a specific Def category ('ThingDef', 'HediffDef', 'TraitDef', 'IncidentDef', 'NeedDef', 'ThoughtDef', 'MentalStateDef')."
                        }
                    },
                    ["required"] = new List<string> { "query" }
                },
                args =>
                {
                    try
                    {
                        EnsureDefsIndexed();

                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("query", out var qVal) || qVal == null)
                        {
                            return "{\"success\": false, \"reason\": \"Missing required parameter 'query'.\"}";
                        }

                        string query = qVal.ToString().Trim();
                        string defTypeFilter = parsedArgs.TryGetValue("defType", out var dtVal) && dtVal != null ? dtVal.ToString().Trim() : null;

                        if (string.IsNullOrEmpty(query))
                        {
                            return "{\"success\": false, \"reason\": \"Query parameter cannot be empty.\"}";
                        }

                        var queryLower = query.ToLower();
                        var searchResults = _indexedDefs
                            .Where(d => string.IsNullOrEmpty(defTypeFilter) || d.defType.Equals(defTypeFilter, StringComparison.OrdinalIgnoreCase))
                            .Select(d => new
                            {
                                def = d,
                                score = GetMatchScore(d, queryLower)
                            })
                            .Where(x => x.score > 0)
                            .OrderByDescending(x => x.score)
                            .Take(15)
                            .Select(x => new
                            {
                                defName = x.def.defName,
                                label = x.def.label,
                                defType = x.def.defType,
                                description = x.def.description.Length > 120 ? x.def.description.Substring(0, 120) + "..." : x.def.description
                            })
                            .ToList();

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            count = searchResults.Count,
                            results = searchResults
                        });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Failed to query defs: {ex.Message}\"}}";
                    }
                }
            );
        }

        private static int GetMatchScore(SynapseIndexedDef d, string queryLower)
        {
            string labelLower = d.label.ToLower();
            string defNameLower = d.defName.ToLower();

            if (labelLower == queryLower) return 100;
            if (defNameLower == queryLower) return 90;

            if (labelLower.StartsWith(queryLower)) return 80;
            if (defNameLower.StartsWith(queryLower)) return 70;

            if (labelLower.Contains(queryLower)) return 60;
            if (defNameLower.Contains(queryLower)) return 50;

            return 0;
        }
    }
}
