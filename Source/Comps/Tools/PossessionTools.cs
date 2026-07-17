using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Tool handler: possess_colonist
    /// Takes direct controller possession of a colonist to perform actions.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterPossessionTools()
        {
            RegisterTool(
                "possess_colonist",
                "Take direct controller possession of a colonist, preventing player overrides, and directing them to perform actions (move, attack, draft, undraft, or clear). Specify release conditions like Damage, Downed, ExtremeMood, Hunger, Exhaustion, Bleeding, EnemyNearby, TargetReached, Timer.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["pawnName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Short name of the colonist to possess (e.g. John)."
                        },
                        ["commandName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Description of the compulsion / reason (e.g. Seeking closure, Wandering in grief)."
                        },
                        ["action"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Action to perform: move, teleport, attack, draft, undraft, clear, equip, ingest, prioritize."
                        },
                        ["targetThingId"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: Unique load ID (ThingID) of a target object (like a bed frame or steel vein) to prioritize/work on."
                        },
                        ["targetX"] = new Dictionary<string, string>
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional: Target X map coordinate for movement or item interaction."
                        },
                        ["targetZ"] = new Dictionary<string, string>
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional: Target Z map coordinate for movement or item interaction."
                        },
                        ["targetPawnName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: Target pawn name to attack."
                        },
                        ["targetItemDef"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: DefName of the item to equip or ingest."
                        },
                        ["targetItemName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: Match string in display label of the item to equip or ingest."
                        },
                        ["releaseConditions"] = new Dictionary<string, object>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, string> { ["type"] = "string" },
                            ["description"] = "Optional: List of conditions that release possession (e.g. [\"Damage\", \"EnemyNearby\", \"ExtremeMood\"])."
                        },
                        ["maxDurationTicks"] = new Dictionary<string, string>
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional: Ticks before automatic timeout release (e.g. 10000. Default is 10000)."
                        }
                    },
                    ["required"] = new List<string> { "pawnName", "commandName", "action" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";

                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("pawnName", out var pawnVal) || !parsedArgs.TryGetValue("action", out var actVal) || !parsedArgs.TryGetValue("commandName", out var cmdVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing required arguments.\"}";
                        }

                        string pawnName = pawnVal?.ToString();
                        string action = actVal?.ToString()?.ToLower();
                        string commandName = cmdVal?.ToString();

                        Pawn pawn = Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault(p => p.LabelShort.Equals(pawnName, StringComparison.OrdinalIgnoreCase));
                        if (pawn == null)
                        {
                            return $"{{\"success\": false, \"reason\": \"Colonist '{pawnName}' not found on the active map.\"}}";
                        }

                        // Release conditions
                        var conditions = new List<string> { "Timer", "Downed" };
                        if (parsedArgs.TryGetValue("releaseConditions", out var condsVal) && condsVal is Newtonsoft.Json.Linq.JArray jArr)
                        {
                            conditions = jArr.Select(t => t.ToString()).ToList();
                            if (!conditions.Contains("Timer", StringComparer.OrdinalIgnoreCase)) conditions.Add("Timer");
                            if (!conditions.Contains("Downed", StringComparer.OrdinalIgnoreCase)) conditions.Add("Downed");
                        }

                        int duration = 10000;
                        if (parsedArgs.TryGetValue("maxDurationTicks", out var durVal) && durVal != null)
                        {
                            int.TryParse(durVal.ToString(), out duration);
                        }

                        int? targetX = null;
                        int? targetZ = null;
                        if (parsedArgs.TryGetValue("targetX", out var xVal) && xVal != null)
                        {
                            if (int.TryParse(xVal.ToString(), out int x)) targetX = x;
                        }
                        if (parsedArgs.TryGetValue("targetZ", out var zVal) && zVal != null)
                        {
                            if (int.TryParse(zVal.ToString(), out int z)) targetZ = z;
                        }

                        // Apply possession in manager
                        SynapsePossessionManager.Possess(pawn, conditions, duration, targetX, targetZ, commandName);

                        // Execute requested action flag-protected
                        bool jobSuccess = false;
                        SynapsePossessionManager.IsExecutingPossessionJob = true;

                        try
                        {
                            jobSuccess = ExecutePossessionAction(pawn, action, parsedArgs, targetX, targetZ);
                        }
                        finally
                        {
                            SynapsePossessionManager.IsExecutingPossessionJob = false;
                        }

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            message = $"Possessed {pawnName} under code '{commandName}' successfully. Ordered job status: {jobSuccess}."
                        });
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Possession failed: {ex.Message}\"}}";
                    }
                }
            );
        }

        private static bool ExecutePossessionAction(Pawn pawn, string action, Dictionary<string, object> parsedArgs, int? targetX, int? targetZ)
        {
            if (action == "draft")
            {
                pawn.drafter.Drafted = true;
                return true;
            }
            else if (action == "undraft")
            {
                pawn.drafter.Drafted = false;
                return true;
            }
            else if (action == "clear")
            {
                pawn.jobs.ClearQueuedJobs();
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                return true;
            }
            else if (action == "move")
            {
                if (targetX.HasValue && targetZ.HasValue)
                {
                    var cell = new IntVec3(targetX.Value, 0, targetZ.Value);
                    Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
                    return pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
            }
            else if (action == "teleport")
            {
                if (targetX.HasValue && targetZ.HasValue)
                {
                    var cell = new IntVec3(targetX.Value, 0, targetZ.Value);
                    pawn.Position = cell;
                    pawn.Notify_Teleported(true, false);
                    return true;
                }
            }
            else if (action == "equip")
            {
                return TryEquipOrIngestAtCell(pawn, parsedArgs, targetX, targetZ, JobDefOf.Equip);
            }
            else if (action == "ingest")
            {
                return TryEquipOrIngestAtCell(pawn, parsedArgs, targetX, targetZ, JobDefOf.Ingest, requireIngestible: true);
            }
            else if (action == "attack")
            {
                if (parsedArgs.TryGetValue("targetPawnName", out var tpVal) && tpVal != null)
                {
                    string targetName = tpVal.ToString();
                    Pawn target = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p => p.LabelShort.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                    if (target != null)
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                        return pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }
                }
            }
            else if (action == "prioritize" || action == "work_on")
            {
                Thing target = null;
                string targetId = parsedArgs.TryGetValue("targetThingId", out var idVal) ? idVal?.ToString() : null;
                string targetName = parsedArgs.TryGetValue("targetItemName", out var nVal) ? nVal?.ToString() : null;

                if (!string.IsNullOrEmpty(targetId))
                {
                    target = Find.CurrentMap.listerThings.AllThings.FirstOrDefault(t => t.ThingID == targetId);
                }
                if (target == null && !string.IsNullOrEmpty(targetName))
                {
                    target = Find.CurrentMap.listerThings.AllThings.FirstOrDefault(t => t.LabelShort.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                if (target == null && targetX.HasValue && targetZ.HasValue)
                {
                    var cell = new IntVec3(targetX.Value, 0, targetZ.Value);
                    target = cell.GetThingList(pawn.Map).FirstOrDefault(t => t.def.category != ThingCategory.Pawn);
                }

                if (target == null) return false;

                Job job = null;
                if (target is Frame)
                {
                    job = JobMaker.MakeJob(JobDefOf.FinishFrame, target);
                }
                else if (target is Blueprint)
                {
                    job = JobMaker.MakeJob(JobDefOf.PlaceNoCostFrame, target);
                }
                else if (target.def.mineable)
                {
                    job = JobMaker.MakeJob(JobDefOf.Mine, target);
                }
                else if (target is Plant plant && plant.HarvestableNow)
                {
                    job = JobMaker.MakeJob(JobDefOf.Harvest, target);
                }
                else if (target.def.category == ThingCategory.Building)
                {
                    if (target.HitPoints < target.MaxHitPoints)
                    {
                        job = JobMaker.MakeJob(JobDefOf.Repair, target);
                    }
                }

                if (job != null)
                {
                    return pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
            }
            return false;
        }

        private static bool TryEquipOrIngestAtCell(Pawn pawn, Dictionary<string, object> parsedArgs, int? targetX, int? targetZ, JobDef jobDef, bool requireIngestible = false)
        {
            if (!targetX.HasValue || !targetZ.HasValue) return false;

            var cell = new IntVec3(targetX.Value, 0, targetZ.Value);
            string itemDef = parsedArgs.TryGetValue("targetItemDef", out var tid) ? tid?.ToString() : null;
            string itemName = parsedArgs.TryGetValue("targetItemName", out var tin) ? tin?.ToString() : null;

            Thing item = null;
            foreach (var t in cell.GetThingList(pawn.Map))
            {
                if (itemDef != null)
                {
                    if (t.def.defName.Equals(itemDef, StringComparison.OrdinalIgnoreCase))
                    {
                        item = t;
                        break;
                    }
                }
                else if (itemName != null)
                {
                    if (t.Label.IndexOf(itemName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        item = t;
                        break;
                    }
                }
                else
                {
                    item = t;
                    break;
                }
            }

            if (item == null) return false;
            if (requireIngestible && item.def.ingestible == null) return false;

            Job job = JobMaker.MakeJob(jobDef, item);
            if (requireIngestible) job.count = 1;
            return pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
