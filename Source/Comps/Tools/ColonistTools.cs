using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Tool handler: get_colonists_profile
    /// Returns detailed colonist information including skills, traits, weapons, and health.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterColonistTools()
        {
            RegisterTool(
                "get_colonists_profile",
                "Get detailed information of all colonists in the colony, including their skills (shooting, melee), traits, weapon equipped, and health/injury conditions.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["compact"] = new Dictionary<string, object>
                        {
                            ["type"] = "boolean",
                            ["description"] = "If true, returns extremely compact/abbreviated details to save token costs."
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
                    var list = new List<object>();
                    foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
                    {
                        var shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
                        var melee = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                        var weapon = pawn.equipment?.Primary?.LabelShort ?? "None";

                        if (compact)
                        {
                            list.Add(new
                            {
                                name = pawn.LabelShort,
                                shoot = shooting,
                                melee = melee,
                                weapon = weapon,
                                down = pawn.Downed
                            });
                        }
                        else
                        {
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
                    }
                    return JsonConvert.SerializeObject(list);
                }
            );
        }
    }
}
