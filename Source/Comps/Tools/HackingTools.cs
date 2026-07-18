using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Tool handlers: attempt_remote_hack and spawn_hacker_base.
    /// Provides remote breach hacking of turrets, generators, and doors,
    /// and spawning of hacker base outposts on the world map.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterHackingTools()
        {
            // Attempt Remote Hack Tool
            RegisterTool(
                "attempt_remote_hack",
                "Attempt to remotely breach and hack a map electronic object (turret, power generator, or door). Requires a powered Comms Console and a nearby Hacker Base within 8 world map tiles. Hacked turrets target friendlies for 5m. Hacked generators shut down for 1h. Hacked doors lock shut for 1h. Success starts a 4h firewall reboot cooldown.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["targetId"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The unique load ID of the target object (turret, generator, or door)."
                        },
                        ["hackerSkill"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional: Hacker power level (1 to 100, defaults to 50)."
                        }
                    },
                    ["required"] = new List<string> { "targetId" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";
                    var map = Find.CurrentMap;

                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("targetId", out var idVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing targetId argument.\"}";
                        }

                        string targetId = idVal?.ToString();
                        int hackerSkill = 50;
                        if (parsedArgs.TryGetValue("hackerSkill", out var hsVal) && hsVal != null && int.TryParse(hsVal.ToString(), out int hs))
                        {
                            hackerSkill = hs;
                        }

                        if (!SynapseObjectControlManager.HasHackerBaseNearby(map))
                        {
                            return "{\"success\": false, \"reason\": \"Hacking signal unavailable. No network broadcast node (Hacker Base) located within 8 tiles on the world map.\"}";
                        }

                        if (!SynapseObjectControlManager.IsCommsConsoleActive(map))
                        {
                            return "{\"success\": false, \"reason\": \"Hacking link failed. No active network gateway (powered Comms Console) detected on the colony map.\"}";
                        }

                        Thing targetThing = null;
                        foreach (var thing in map.listerThings.AllThings)
                        {
                            if (thing.GetUniqueLoadID().Equals(targetId, StringComparison.OrdinalIgnoreCase))
                            {
                                targetThing = thing;
                                break;
                            }
                        }

                        if (targetThing == null)
                        {
                            return $"{{\"success\": false, \"reason\": \"Object ID '{targetId}' not found on the map.\"}}";
                        }

                        if (SynapseObjectControlManager.IsHacked(targetThing))
                        {
                            return $"{{\"success\": false, \"reason\": \"Object '{targetId}' is already hacked.\"}}";
                        }

                        if (SynapseObjectControlManager.IsOnCooldown(targetThing))
                        {
                            int remaining = SynapseObjectControlManager.RemainingCooldownTicks(targetThing);
                            float remainingMins = (float)remaining / 2500f * 60f;
                            return $"{{\"success\": false, \"reason\": \"Object '{targetId}' firewall is rebooting. Cooldown remaining: {remainingMins:F1} game minutes.\"}}";
                        }

                        double successChance = 0.8 * (hackerSkill / 50.0);
                        successChance = Math.Max(0.1, Math.Min(0.95, successChance));
                        
                        bool success = Rand.Value <= successChance;
                        if (!success)
                        {
                            return JsonConvert.SerializeObject(new
                            {
                                success = false,
                                reason = $"Breach attempt blocked by offline local firewall (Success chance was {(successChance * 100):F0}%)."
                            });
                        }

                        int durationTicks = 2500; // 1 hour for doors and generators
                        if (targetThing is Building_Turret)
                        {
                            durationTicks = 208; // 5 minutes for turrets (208 ticks)
                        }

                        SynapseObjectControlManager.ApplyHack(targetThing, durationTicks);

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            message = $"Successfully breached firewall. Remote hack active on {targetThing.LabelCap} for {durationTicks} ticks."
                        });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Hacking attempt failed: {ex.Message}\"}}";
                    }
                }
            );

            // Spawn Hacker Base Tool
            RegisterTool(
                "spawn_hacker_base",
                "Spawn a hostile Hacker Base outpost on the world map within 8 tiles of the colony to establish a network hacking broadcast uplink.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";
                    try
                    {
                        string msg = SynapseObjectControlManager.SpawnHackerBase(Find.CurrentMap);
                        Find.LetterStack.ReceiveLetter(
                            "Hacker Base Located",
                            $"An enemy faction has set up a remote hacking transceiver outpost nearby!\n\nDetails: {msg}\n\nUntil this base is eliminated, they can remotely breach and compromise our turrets, doors, and generators.",
                            LetterDefOf.ThreatBig
                        );
                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            message = msg
                        });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Spawning hacker base failed: {ex.Message}\"}}";
                    }
                }
            );
        }
    }
}
