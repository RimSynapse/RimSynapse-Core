using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Tool handlers: get_stockpile_details and find_items_on_map.
    /// Provides resource inventory and map item search capabilities.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterStockpileTools()
        {
            // Stockpile Details Tool
            RegisterTool(
                "get_stockpile_details",
                "Get exact resource counts currently stored in stockpiles (e.g. food nutrition, medicine, silver, steel, components, drugs, etc.).",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["compact"] = new Dictionary<string, object>
                        {
                            ["type"] = "boolean",
                            ["description"] = "If true, returns extremely compact details to save token costs by omitting zero-value keys."
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

                    if (compact)
                    {
                        var result = new Dictionary<string, object>();
                        if (silver > 0) result["silver"] = silver;
                        if (steel > 0) result["steel"] = steel;
                        if (components > 0) result["components"] = components;
                        if (wood > 0) result["wood"] = wood;
                        if (medicine > 0) result["medicine"] = medicine;
                        if (nutrition > 0) result["nutrition"] = (int)nutrition;
                        return JsonConvert.SerializeObject(result);
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

            // Find Items On Map Tool
            RegisterTool(
                "find_items_on_map",
                "Search the map for loose items (weapons, apparel, food, medicine, resources, drugs) by defName, label search term, or category. Returns their names, positions (x, z), distance from center, and item ID.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["searchTerm"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: Text match search pattern in the item's label (e.g. 'Assault Rifle', 'Pills')."
                        },
                        ["category"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: Filter by item category ('Weapons', 'Apparel', 'Food', 'Medicine', 'Resources', 'Drugs')."
                        },
                        ["maxResults"] = new Dictionary<string, string>
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional: Maximum results to return (default: 20)."
                        }
                    }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"error\": \"No active map loaded.\"}";
                    var map = Find.CurrentMap;

                    string searchTerm = null;
                    string category = null;
                    int maxResults = 20;

                    try
                    {
                        var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsed != null)
                        {
                            if (parsed.TryGetValue("searchTerm", out var st))
                            {
                                if (st != null) searchTerm = st.ToString();
                            }
                            if (parsed.TryGetValue("category", out var cat))
                            {
                                if (cat != null) category = cat.ToString();
                            }
                            if (parsed.TryGetValue("maxResults", out var mr))
                            {
                                if (mr != null) int.TryParse(mr.ToString(), out maxResults);
                            }
                        }
                    }
                    catch {}

                    var list = new List<MapItemSearchResult>();
                    IntVec3 center = map.Center;

                    foreach (var thing in map.listerThings.AllThings)
                    {
                        if (thing == null) continue;
                        if (!thing.Spawned) continue;
                        if (thing.Position.Fogged(map)) continue;

                        // Check if it's a loose item (not held by pawn or container)
                        if (thing.ParentHolder is Pawn) continue;
                        if (thing.ParentHolder is Building_NutrientPasteDispenser) continue;

                        // Filter by category
                        if (!string.IsNullOrEmpty(category))
                        {
                            bool matchesCategory = false;
                            string catLower = category.ToLower();
                            if (catLower == "weapons")
                            {
                                if (thing.def.IsWeapon) matchesCategory = true;
                            }
                            else if (catLower == "apparel")
                            {
                                if (thing.def.IsApparel) matchesCategory = true;
                            }
                            else if (catLower == "food")
                            {
                                if (thing.def.IsNutritionGivingIngestible) matchesCategory = true;
                            }
                            else if (catLower == "medicine")
                            {
                                if (thing.def.IsMedicine) matchesCategory = true;
                            }
                            else if (catLower == "resources")
                            {
                                if (thing.def.category == ThingCategory.Item)
                                {
                                    if (!thing.def.IsWeapon)
                                    {
                                        if (!thing.def.IsApparel)
                                        {
                                            if (!thing.def.IsIngestible)
                                            {
                                                matchesCategory = true;
                                            }
                                        }
                                    }
                                }
                            }
                            else if (catLower == "drugs")
                            {
                                if (thing.def.IsDrug) matchesCategory = true;
                            }

                            if (!matchesCategory) continue;
                        }
                        else
                        {
                            // By default only return Items/loose things, not structures, terrains, or pawns
                            if (thing.def.category != ThingCategory.Item)
                                continue;
                        }

                        // Filter by search term
                        if (!string.IsNullOrEmpty(searchTerm))
                        {
                            if (thing.Label.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                if (thing.def.defName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    continue;
                                }
                            }
                        }

                        float distance = thing.Position.DistanceTo(center);
                        list.Add(new MapItemSearchResult
                        {
                            label = thing.LabelShort,
                            defName = thing.def.defName,
                            x = thing.Position.x,
                            z = thing.Position.z,
                            distance = (int)distance,
                            stackCount = thing.stackCount
                        });
                    }

                    // Sort by distance and return top results
                    var sorted = list.OrderBy(item => item.distance).Take(maxResults).ToList();
                    return JsonConvert.SerializeObject(sorted);
                }
            );
        }
    }
}
