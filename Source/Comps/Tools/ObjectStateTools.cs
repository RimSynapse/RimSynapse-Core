using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Tool handler: modify_object_state
    /// Applies direct modifications to map structures (power, fuel, damage, doors, fire).
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterObjectStateTools()
        {
            RegisterTool(
                "modify_object_state",
                "Apply direct modifications to an object/structure on the map (locks, power status, fuel levels, damage, fire).",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["thingId"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The unique load ID (ThingID) of the target object."
                        },
                        ["action"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The modification type: 'set_power', 'set_fuel', 'inflict_damage', 'set_door_lock', 'spawn_fire'."
                        },
                        ["powerOn"] = new Dictionary<string, string>
                        {
                            ["type"] = "boolean",
                            ["description"] = "Target power status for set_power."
                        },
                        ["fuelAmount"] = new Dictionary<string, string>
                        {
                            ["type"] = "number",
                            ["description"] = "Target fuel amount for set_fuel."
                        },
                        ["damageAmount"] = new Dictionary<string, string>
                        {
                            ["type"] = "integer",
                            ["description"] = "Damage points to deal for inflict_damage."
                        },
                        ["locked"] = new Dictionary<string, string>
                        {
                            ["type"] = "boolean",
                            ["description"] = "Lock state for set_door_lock."
                        }
                    },
                    ["required"] = new List<string> { "thingId", "action" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";
                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("thingId", out var idVal) || !parsedArgs.TryGetValue("action", out var actionVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing required arguments 'thingId' or 'action'.\"}";
                        }

                        string thingId = idVal?.ToString();
                        string action = actionVal?.ToString();

                        Thing thing = null;
                        foreach (var map in Find.Maps)
                        {
                            thing = map.listerThings.AllThings.FirstOrDefault(t => t.ThingID == thingId);
                            if (thing != null) break;
                        }

                        if (thing == null)
                        {
                            return $"{{\"success\": false, \"reason\": \"Object with ID '{thingId}' not found on any map.\"}}";
                        }

                        return ExecuteObjectStateAction(thing, thingId, action, parsedArgs);
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Modifying object state failed: {ex.Message}\"}}";
                    }
                }
            );
        }

        private static string ExecuteObjectStateAction(Thing thing, string thingId, string action, Dictionary<string, object> parsedArgs)
        {
            bool? powerOn = null;
            if (parsedArgs.TryGetValue("powerOn", out var pVal) && pVal != null && bool.TryParse(pVal.ToString(), out bool bPower)) powerOn = bPower;

            float? fuelAmount = null;
            if (parsedArgs.TryGetValue("fuelAmount", out var fVal) && fVal != null && float.TryParse(fVal.ToString(), out float fFuel)) fuelAmount = fFuel;

            int? damageAmount = null;
            if (parsedArgs.TryGetValue("damageAmount", out var dVal) && dVal != null && int.TryParse(dVal.ToString(), out int iDmg)) damageAmount = iDmg;

            bool? locked = null;
            if (parsedArgs.TryGetValue("locked", out var lVal) && lVal != null && bool.TryParse(lVal.ToString(), out bool bLock)) locked = bLock;

            if (action.Equals("set_power", StringComparison.OrdinalIgnoreCase))
            {
                if (!powerOn.HasValue) return "{\"success\": false, \"reason\": \"Missing 'powerOn' parameter.\"}";
                var compPower = thing.TryGetComp<CompPowerTrader>();
                if (compPower == null) return "{\"success\": false, \"reason\": \"Target object is not power-grid connectable (no CompPowerTrader).\"}";

                compPower.PowerOn = powerOn.Value;
                return $"{{\"success\": true, \"message\": \"Successfully set power status of {thing.LabelCap} to {powerOn.Value}.\"}}";
            }
            else if (action.Equals("set_fuel", StringComparison.OrdinalIgnoreCase))
            {
                if (!fuelAmount.HasValue) return "{\"success\": false, \"reason\": \"Missing 'fuelAmount' parameter.\"}";
                var compRefuelable = thing.TryGetComp<CompRefuelable>();
                if (compRefuelable == null) return "{\"success\": false, \"reason\": \"Target object is not refuelable (no CompRefuelable).\"}";

                compRefuelable.Refuel(fuelAmount.Value - compRefuelable.Fuel);
                return $"{{\"success\": true, \"message\": \"Successfully set fuel of {thing.LabelCap} to {compRefuelable.Fuel:F1}.\"}}";
            }
            else if (action.Equals("inflict_damage", StringComparison.OrdinalIgnoreCase))
            {
                if (!damageAmount.HasValue) return "{\"success\": false, \"reason\": \"Missing 'damageAmount' parameter.\"}";
                thing.TakeDamage(new DamageInfo(DamageDefOf.Bomb, damageAmount.Value));
                return $"{{\"success\": true, \"message\": \"Successfully dealt {damageAmount.Value} damage to {thing.LabelCap}.\"}}";
            }
            else if (action.Equals("set_door_lock", StringComparison.OrdinalIgnoreCase))
            {
                if (!locked.HasValue) return "{\"success\": false, \"reason\": \"Missing 'locked' parameter.\"}";
                if (!(thing is Building_Door)) return "{\"success\": false, \"reason\": \"Target object is not a door.\"}";

                if (locked.Value)
                {
                    SynapseObjectControlManager.LockedDoors.Add(thingId);
                }
                else
                {
                    SynapseObjectControlManager.LockedDoors.Remove(thingId);
                }
                return $"{{\"success\": true, \"message\": \"Successfully set door lock status of {thing.LabelCap} to {locked.Value}.\"}}";
            }
            else if (action.Equals("spawn_fire", StringComparison.OrdinalIgnoreCase))
            {
                FireUtility.TryStartFireIn(thing.Position, thing.Map, 0.5f, null);
                return $"{{\"success\": true, \"message\": \"Successfully spawned fire on {thing.LabelCap}.\"}}";
            }

            return $"{{\"success\": false, \"reason\": \"Unknown action '{action}'.\"}}";
        }
    }
}
