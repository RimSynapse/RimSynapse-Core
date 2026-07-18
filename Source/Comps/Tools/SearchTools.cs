using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    public static partial class SynapseToolRegistry
    {
        private static void RegisterSearchTools()
        {
            RegisterTool(
                "search_map_entities",
                "Search and list entities (pawns, animals, items, buildings, plants) currently on the map. Can filter to only show things visible on the player's screen.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["query"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional search query to filter by name, label, or definition (e.g. 'cow', 'centipede', 'silver')."
                        },
                        ["includeTypes"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Comma-separated types to include: 'pawns', 'animals', 'colonists', 'enemies', 'items', 'buildings', 'plants'. Defaults to 'all'."
                        },
                        ["onlyVisibleOnScreen"] = new Dictionary<string, object>
                        {
                            ["type"] = "boolean",
                            ["description"] = "If true, only returns entities within the player's current screen camera view."
                        }
                    }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";
                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        
                        string query = null;
                        if (parsedArgs != null)
                        {
                            if (parsedArgs.TryGetValue("query", out var qVal)) query = qVal?.ToString();
                            else if (parsedArgs.TryGetValue("filter", out var fVal)) query = fVal?.ToString();
                        }

                        string includeTypes = "all";
                        if (parsedArgs != null)
                        {
                            if (parsedArgs.TryGetValue("includeTypes", out var itVal)) includeTypes = itVal?.ToString();
                            else if (parsedArgs.TryGetValue("types", out var tVal)) includeTypes = tVal?.ToString();
                        }

                        bool onlyVisibleOnScreen = false;
                        if (parsedArgs != null && parsedArgs.TryGetValue("onlyVisibleOnScreen", out var vVal) && vVal != null)
                        {
                            bool.TryParse(vVal.ToString(), out onlyVisibleOnScreen);
                        }

                        var viewRect = onlyVisibleOnScreen ? Find.CameraDriver.CurrentViewRect : CellRect.Empty;
                        var matchedList = new List<object>();

                        var types = includeTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim().ToLowerInvariant().TrimEnd('s')).ToList();
                        if (types.Contains("all") || !types.Any())
                        {
                            types = new List<string> { "pawn", "animal", "colonist", "enemy", "item", "building", "plant" };
                        }

                        // Collect matching things from both map pawns and map listerThings
                        var allThings = new List<Thing>();
                        if (Find.CurrentMap.mapPawns != null && Find.CurrentMap.mapPawns.AllPawnsSpawned != null)
                        {
                            allThings.AddRange(Find.CurrentMap.mapPawns.AllPawnsSpawned.Cast<Thing>());
                        }
                        if (Find.CurrentMap.listerThings != null && Find.CurrentMap.listerThings.AllThings != null)
                        {
                            allThings.AddRange(Find.CurrentMap.listerThings.AllThings);
                        }

                        foreach (var thing in allThings)
                        {
                            if (onlyVisibleOnScreen && !viewRect.Contains(thing.Position)) continue;

                            bool matchesType = false;
                            bool isPawn = thing is Pawn;
                            Pawn p = thing as Pawn;

                            if (isPawn)
                            {
                                if (types.Contains("pawn")) matchesType = true;
                                else if (types.Contains("animal") && p.RaceProps.Animal) matchesType = true;
                                else if (types.Contains("colonist") && p.IsColonist) matchesType = true;
                                else if (types.Contains("enemy") && p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)) matchesType = true;
                            }
                            else
                            {
                                if (types.Contains("item") && thing.def.category == ThingCategory.Item) matchesType = true;
                                else if (types.Contains("building") && thing.def.category == ThingCategory.Building) matchesType = true;
                                else if (types.Contains("plant") && thing.def.category == ThingCategory.Plant) matchesType = true;
                            }

                            if (!matchesType) continue;

                            // Filter by query
                            if (!string.IsNullOrEmpty(query))
                            {
                                string label = thing.LabelShort ?? thing.Label ?? "";
                                string defName = thing.def.defName ?? "";
                                if (label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                                    defName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    continue;
                                }
                            }

                            string name = thing.LabelShort;
                            string faction = thing.Faction?.Name ?? "None";
                            float healthPct = isPawn && p.health != null && p.health.summaryHealth != null ? p.health.summaryHealth.SummaryHealthPercent : 1.0f;
                            string details = isPawn ? $"Pawn ({p.kindDef.defName}, Health: {(healthPct * 100f):F0}%)" : $"Item/Object ({thing.def.defName})";

                            matchedList.Add(new
                            {
                                name = name,
                                thingId = thing.ThingID,
                                defName = thing.def.defName,
                                pos = $"({thing.Position.x}, {thing.Position.z})",
                                faction = faction,
                                details = details
                            });

                            if (matchedList.Count >= 50) break; // Limit response size to prevent context overflow
                        }

                        return JsonConvert.SerializeObject(new { success = true, count = matchedList.Count, results = matchedList });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Map search failed: {ex.Message}\"}}";
                    }
                }
            );
        }
    }
}
