using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Tool handler: get_active_threats
    /// Lists hostile raiders, mechanoids, infestations, ship parts, and fires on the map.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterThreatTools()
        {
            RegisterTool(
                "get_active_threats",
                "List all active threats present on the colony map (e.g. hostiles, infestations, crashed ship parts, fire counts).",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["compact"] = new Dictionary<string, object>
                        {
                            ["type"] = "boolean",
                            ["description"] = "If true, returns extremely compact details to save token costs by omitting zero-value threat keys."
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

                    if (compact)
                    {
                        var result = new Dictionary<string, object>();
                        if (raiders > 0) result["raiders"] = raiders;
                        if (mechanoids > 0) result["mechs"] = mechanoids;
                        if (infestations > 0) result["hives"] = infestations;
                        if (shipParts > 0) result["shipParts"] = shipParts;
                        if (fireCount > 0) result["fires"] = fireCount;
                        return JsonConvert.SerializeObject(result);
                    }

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
        }
    }
}
