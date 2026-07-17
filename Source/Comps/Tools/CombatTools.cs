using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Tool handler: control_turret
    /// Controls turret power state, targeting overrides, sabotage, and detonation.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterCombatTools()
        {
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

            // Fire Weapon At Cell Tool
            RegisterTool(
                "fire_weapon_at_cell",
                "Simulate a weapon blast or gunshot from a source pawn targeting a specific map coordinate cell. Spawns visual bullet impacts and deals damage or explosion effects.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["pawnName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The name of the pawn firing the weapon."
                        },
                        ["targetX"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "Target X coordinate."
                        },
                        ["targetZ"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "Target Z coordinate."
                        },
                        ["damageType"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: Damage type to deal. Options: 'bomb', 'bullet', 'bullet_high_damage'. Default: 'bullet'."
                        }
                    },
                    ["required"] = new List<string> { "pawnName", "targetX", "targetZ" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";
                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("pawnName", out var pVal) || !parsedArgs.TryGetValue("targetX", out var xVal) || !parsedArgs.TryGetValue("targetZ", out var zVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing required arguments.\"}";
                        }

                        string pawnName = pVal.ToString();
                        int x = int.Parse(xVal.ToString());
                        int z = int.Parse(zVal.ToString());
                        string damageType = parsedArgs.TryGetValue("damageType", out var dVal) ? dVal?.ToString()?.ToLower() : "bullet";

                        Pawn pawn = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p => p.LabelShort.Equals(pawnName, StringComparison.OrdinalIgnoreCase));
                        if (pawn == null) return $"{{\"success\": false, \"reason\": \"Pawn '{pawnName}' not found.\"}}";

                        var cell = new IntVec3(x, 0, z);
                        DamageDef damageDef = DamageDefOf.Bullet;
                        if (damageType == "bomb") damageDef = DamageDefOf.Bomb;

                        SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("Shot_Shotgun");

                        GenExplosion.DoExplosion(
                            center: cell,
                            map: Find.CurrentMap,
                            radius: 0.9f,
                            damType: damageDef,
                            instigator: pawn,
                            damAmount: 30,
                            armorPenetration: 0.5f,
                            explosionSound: sound
                        );

                        return $"{{\"success\": true, \"message\": \"Successfully simulated gunshot from {pawnName} targeting ({x}, {z}).\"}}";
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Failed to fire weapon: {ex.Message}\"}}";
                    }
                }
            );

            // Damage Self With Equipped Tool
            RegisterTool(
                "damage_self_with_equipped",
                "Force a pawn to inflict damage on themselves (suicide) using their currently equipped weapon. Can target a specific body part (e.g. Head, Heart) with a damage multiplier.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["pawnName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The name of the pawn."
                        },
                        ["targetBodyPart"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The Def name of the target body part (e.g. Head, Heart, Brain)."
                        },
                        ["damageMultiplier"] = new Dictionary<string, object>
                        {
                            ["type"] = "number",
                            ["description"] = "Optional: Damage multiplier to ensure lethality (usually 2.0 to 5.0)."
                        }
                    },
                    ["required"] = new List<string> { "pawnName", "targetBodyPart" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";
                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("pawnName", out var pVal) || !parsedArgs.TryGetValue("targetBodyPart", out var bpVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing required arguments.\"}";
                        }

                        string pawnName = pVal.ToString();
                        string bodyPart = bpVal.ToString();
                        float dmgMultiplier = 4.0f;
                        if (parsedArgs.TryGetValue("damageMultiplier", out var mVal) && mVal != null)
                        {
                            float.TryParse(mVal.ToString(), out dmgMultiplier);
                        }

                        Pawn pawn = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p => p.LabelShort.Equals(pawnName, StringComparison.OrdinalIgnoreCase));
                        if (pawn == null) return $"{{\"success\": false, \"reason\": \"Pawn '{pawnName}' not found.\"}}";

                        ThingWithComps primary = pawn.equipment.Primary;
                        if (primary == null) return $"{{\"success\": false, \"reason\": \"Pawn '{pawnName}' is not holding a weapon.\"}}";

                        VerbTracker verbTracker = primary.GetComp<CompEquippable>()?.verbTracker;
                        Verb primaryVerb = verbTracker?.AllVerbs?.FirstOrDefault(v => v.verbProps.isPrimary);
                        ThingDef projectileDef = primaryVerb?.verbProps?.defaultProjectile;

                        DamageDef damageDef = DamageDefOf.Bullet;
                        float damageAmount = 15f;
                        float armorPenetration = 0.2f;

                        if (projectileDef != null)
                        {
                            damageDef = projectileDef.projectile.damageDef;
                            damageAmount = (float)projectileDef.projectile.GetDamageAmount(primary, null);
                            armorPenetration = projectileDef.projectile.GetArmorPenetration(primary, null);
                        }
                        else
                        {
                            damageDef = DamageDefOf.Stab;
                            damageAmount = 15f;
                            armorPenetration = 0.3f;
                            
                            if (primary.def.tools != null && primary.def.tools.Count > 0)
                            {
                                var tool = primary.def.tools[0];
                                damageAmount = tool.power;
                                armorPenetration = tool.armorPenetration;
                                if (tool.capacities != null && tool.capacities.Count > 0)
                                {
                                    var cap = tool.capacities[0];
                                    if (cap != null && cap.defName != null)
                                    {
                                        if (cap.defName.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            damageDef = DamageDefOf.Cut;
                                        }
                                        else if (cap.defName.IndexOf("Stab", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            damageDef = DamageDefOf.Stab;
                                        }
                                        else if (cap.defName.IndexOf("Blunt", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            damageDef = DamageDefOf.Blunt;
                                        }
                                    }
                                }
                            }
                        }

                        BodyPartRecord part = pawn.RaceProps.body.AllParts.FirstOrDefault(bp => bp.def.defName.Equals(bodyPart, StringComparison.OrdinalIgnoreCase));
                        if (part == null) return $"{{\"success\": false, \"reason\": \"BodyPart '{bodyPart}' not found on pawn.\"}}";

                        var dinfo = new DamageInfo(
                            def: damageDef,
                            amount: damageAmount * dmgMultiplier,
                            armorPenetration: armorPenetration,
                            angle: -1f,
                            instigator: pawn,
                            hitPart: part,
                            weapon: primary.def
                        );

                        pawn.TakeDamage(dinfo);
                        return $"{{\"success\": true, \"message\": \"Pawn '{pawnName}' successfully shot themselves in the '{bodyPart}' with their '{primary.def.defName}'.\"}}";
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Failed to damage self: {ex.Message}\"}}";
                    }
                }
            );
        }
    }
}
