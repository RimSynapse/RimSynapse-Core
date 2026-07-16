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
        }
    }
}
