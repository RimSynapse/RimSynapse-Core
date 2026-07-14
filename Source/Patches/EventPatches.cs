using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Models;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace RimSynapse.Patches
{
    /// <summary>
    /// Shared helper for native event interception.
    /// Both Tale and Kill patches route through here to avoid code duplication.
    /// </summary>
    internal static class EventPatchHelper
    {
        public static void EnqueueIfPlaying(string category, string description)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;

            var coreComp = Find.World.GetComponent<RimSynapse.SynapseCoreWorldComponent>();
            if (coreComp == null) return;

            coreComp.EnqueuePastEvent(new PastEvent
            {
                gameTick = GenTicks.TicksGame,
                category = category,
                eventDescription = description
            });
        }
    }

    /// <summary>
    /// Intercepts RimWorld's Tale system (marriages, legendary crafting, recruitment, etc.)
    /// and enqueues a PastEvent so the LLM can generate personalized memories.
    /// </summary>
    [HarmonyPatch(typeof(TaleRecorder), "RecordTale")]
    internal static class Patch_TaleRecorder_RecordTale
    {
        static void Postfix(TaleDef def, params object[] args)
        {
            Pawn primaryPawn = args.OfType<Pawn>().FirstOrDefault();
            if (primaryPawn != null && (primaryPawn.IsColonist || primaryPawn.IsPrisonerOfColony))
            {
                string taleLabel = def.label ?? def.defName;
                EventPatchHelper.EnqueueIfPlaying("NativeTale",
                    $"Tale Recorded: {taleLabel} involving {primaryPawn.Name.ToStringShort}.");
            }
        }
    }

    /// <summary>
    /// Intercepts pawn deaths where a colonist is involved (as victim or killer).
    /// This catches deaths that TaleRecorder misses (starvation, disease, environmental).
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class Patch_Pawn_Kill
    {
        static void Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            Pawn victim = __instance;
            Pawn killer = dinfo?.Instigator as Pawn;

            bool colonistInvolved = (victim.IsColonist || victim.IsPrisonerOfColony) ||
                                    (killer != null && (killer.IsColonist || killer.IsPrisonerOfColony));

            if (colonistInvolved)
            {
                string killerStr = killer != null ? killer.Name.ToStringShort : "Unknown Causes";
                EventPatchHelper.EnqueueIfPlaying("NativeDeath",
                    $"Death: {victim.Name.ToStringShort} was killed by {killerStr}.");
            }

            // Track deaths for active raid stats
            if (Current.ProgramState == ProgramState.Playing && Find.World != null)
            {
                var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
                if (coreComp != null && coreComp.activeRaidTracker != null)
                {
                    if (victim.Faction != null)
                    {
                        if (victim.Faction.IsPlayer)
                        {
                            if (victim.RaceProps != null && victim.RaceProps.Animal)
                            {
                                coreComp.activeRaidTracker.livestockKilled++;
                                string trainability = victim.RaceProps.trainability?.label ?? "none";
                                coreComp.activeRaidTracker.lostLivestockDetails.Add($"{victim.Label} (Value: {victim.MarketValue:F0} silver, Trainability: {trainability})");
                            }
                            else
                            {
                                coreComp.activeRaidTracker.colonistsKilled++;
                            }
                        }
                        else if (victim.Faction.HostileTo(Faction.OfPlayer))
                        {
                            coreComp.activeRaidTracker.enemiesKilled++;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Intercepts pawns getting downed to track injuries for raid statistics.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    internal static class Patch_Pawn_MakeDowned
    {
        static void Postfix(Pawn_HealthTracker __instance)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;
            var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            if (coreComp == null || coreComp.activeRaidTracker == null) return;

            Pawn pawn = HarmonyLib.Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn?.Faction != null)
            {
                if (pawn.Faction.IsPlayer)
                {
                    if (pawn.RaceProps != null && pawn.RaceProps.Animal)
                    {
                        coreComp.activeRaidTracker.livestockInjured++;
                    }
                    else
                    {
                        coreComp.activeRaidTracker.colonistsInjured++;
                    }
                }
                else if (pawn.Faction.HostileTo(Faction.OfPlayer))
                {
                    coreComp.activeRaidTracker.enemiesDowned++;
                }
            }
        }
    }

    /// <summary>
    /// Intercepts quest endings to resolve active quest offers and track outcomes.
    /// </summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    internal static class Patch_Quest_End
    {
        static void Postfix(Quest __instance, QuestEndOutcome outcome)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;
            var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            if (coreComp == null) return;

            string questId = __instance.GetUniqueLoadID();
            EventOutcome coreOutcome = EventOutcome.Unknown;

            if (outcome == QuestEndOutcome.Success) coreOutcome = EventOutcome.Success;
            else if (outcome == QuestEndOutcome.Fail) coreOutcome = EventOutcome.Failed;
            else coreOutcome = EventOutcome.Ignored;

            string resolutionDesc = $"Quest ended with outcome: {outcome}.";
            coreComp.ResolveEvent(questId, resolutionDesc, coreOutcome);
        }
    }

    /// <summary>
    /// Intercepts LordToil_PanicFlee initialization to capture enemies fleeing from a raid.
    /// Calculates the final Tactician Score.
    /// </summary>
    [HarmonyPatch(typeof(LordToil_PanicFlee), nameof(LordToil_PanicFlee.Init))]
    internal static class Patch_LordToil_PanicFlee_Init
    {
        static void Postfix(LordToil_PanicFlee __instance)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;
            var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            if (coreComp == null || string.IsNullOrEmpty(coreComp.activeRaidEventId)) return;

            string outcomeDesc = "Raiders were broken and are fleeing in panic.";
            EventOutcome outcome = EventOutcome.Triumph;

            if (coreComp.activeRaidTracker != null)
            {
                float curWealth = Find.CurrentMap?.wealthWatcher?.WealthTotal ?? 0f;
                float wealthLost = System.Math.Max(0f, coreComp.activeRaidTracker.startWealth - curWealth);

                int kills = coreComp.activeRaidTracker.enemiesKilled;
                int downs = coreComp.activeRaidTracker.enemiesDowned;
                int colKills = coreComp.activeRaidTracker.colonistsKilled;
                int colInj = coreComp.activeRaidTracker.colonistsInjured;
                int colKid = coreComp.activeRaidTracker.colonistsKidnapped;
                int liveKills = coreComp.activeRaidTracker.livestockKilled;
                int liveInj = coreComp.activeRaidTracker.livestockInjured;

                // Calculate Tactician Rating comparing player losses (including livestock) vs enemy losses
                float playerPowerLoss = (colKills * 3.0f) + (colInj * 0.5f) + (colKid * 4.0f) + (liveKills * 0.75f) + (liveInj * 0.15f) + (wealthLost / 5000f);
                float enemyPowerLoss = (kills * 1.5f) + (downs * 0.5f);

                string rating = "Decisive Triumph";
                if (playerPowerLoss > 0.1f)
                {
                    float ratio = enemyPowerLoss / playerPowerLoss;
                    if (ratio > 5.0f)
                    {
                        rating = "Decisive Triumph";
                    }
                    else if (ratio > 1.5f)
                    {
                        rating = "Pyrrhic Victory";
                    }
                    else if (ratio > 0.7f)
                    {
                        rating = "Heavy Standoff";
                        outcome = EventOutcome.Conflict;
                    }
                    else
                    {
                        rating = "Tragic Setback";
                        outcome = EventOutcome.Tragedy;
                    }
                }
                else if (enemyPowerLoss > 0)
                {
                    rating = "Flawless Defense";
                }
                else
                {
                    rating = "Tactical Standoff";
                }

                string livestockReport = "";
                if (liveKills > 0 || liveInj > 0)
                {
                    livestockReport = $", Livestock loss: {liveKills} killed, {liveInj} injured";
                    if (coreComp.activeRaidTracker.lostLivestockDetails.Any())
                    {
                        livestockReport += $" ({string.Join(", ", coreComp.activeRaidTracker.lostLivestockDetails)})";
                    }
                }

                outcomeDesc = $"Raid ended in a {rating}. Casualties: {kills} enemies killed, {downs} downed. Player loss: {colKills} colonists killed, {colInj} injured, {colKid} kidnapped{livestockReport}. Wealth destroyed: {wealthLost:F0} silver.";
            }

            coreComp.ResolveEvent(coreComp.activeRaidEventId, outcomeDesc, outcome);
            coreComp.activeRaidEventId = null;
            coreComp.activeRaidTracker = null;
        }
    }

    /// <summary>
    /// Intercepts kidnappings during raids to register a tragic resolution.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreKidnapped))]
    internal static class Patch_Pawn_PreKidnapped
    {
        static void Postfix(Pawn __instance, Pawn kidnapper)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;
            var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            if (coreComp == null) return;

            if (coreComp.activeRaidTracker != null && __instance.Faction != null && __instance.Faction.IsPlayer)
            {
                coreComp.activeRaidTracker.colonistsKidnapped++;
            }

            if (string.IsNullOrEmpty(coreComp.activeRaidEventId)) return;

            coreComp.ResolveEvent(
                coreComp.activeRaidEventId,
                $"Raiders successfully kidnapped {__instance.Name.ToStringShort} and escaped.",
                EventOutcome.Tragedy
            );
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class Patch_Pawn_SpawnSetup_Raider
    {
        static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad) return;
            if (__instance.Faction == null) return;
            if (__instance.Faction.IsPlayer) return;

            var coreComp = Find.World?.GetComponent<SynapseCoreWorldComponent>();
            if (coreComp != null)
            {
                if (!string.IsNullOrEmpty(coreComp.activeRaidEventId))
                {
                    if (__instance.Faction.HostileTo(Faction.OfPlayer))
                    {
                        coreComp.pawnToRaidId[__instance.ThingID] = coreComp.activeRaidEventId;
                    }
                }

                if (!__instance.Faction.HostileTo(Faction.OfPlayer) && 
                    __instance.RaceProps != null && 
                    __instance.RaceProps.Humanlike && 
                    !__instance.IsPrisoner)
                {
                    coreComp.visitorEntryTicks[__instance.ThingID] = Find.TickManager.TicksGame;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    internal static class Patch_Pawn_SetFaction_RecruitOrEnslave
    {
        private static bool wasPrisonerBefore = false;
        private static string raidIdBefore = null;

        [HarmonyPrefix]
        static void Prefix(Pawn __instance, Faction newFaction)
        {
            wasPrisonerBefore = false;
            raidIdBefore = null;

            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;
            if (newFaction == null || !newFaction.IsPlayer) return;

            // Check if the pawn is currently a prisoner
            if (__instance.IsPrisoner)
            {
                wasPrisonerBefore = true;
                
                var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
                if (coreComp != null)
                {
                    if (coreComp.pawnToRaidId.TryGetValue(__instance.ThingID, out string raidId))
                    {
                        raidIdBefore = raidId;
                    }
                }
            }
        }

        [HarmonyPostfix]
        static void Postfix(Pawn __instance, Faction newFaction)
        {
            if (!wasPrisonerBefore || string.IsNullOrEmpty(raidIdBefore)) return;
            if (__instance.Faction == null || !__instance.Faction.IsPlayer) return;

            var coreComp = Find.World?.GetComponent<SynapseCoreWorldComponent>();
            if (coreComp != null)
            {
                // Determine if they joined as a slave or regular colonist
                bool isSlave = false;
                try
                {
                    isSlave = __instance.IsSlave;
                }
                catch { }

                if (!coreComp.raidRecruitedPawns.TryGetValue(raidIdBefore, out var list))
                {
                    list = new List<string>();
                    coreComp.raidRecruitedPawns[raidIdBefore] = list;
                }

                if (!list.Contains(__instance.LabelShort))
                {
                    list.Add(__instance.LabelShort);

                    var raidEvent = coreComp.backlogQueueList.FirstOrDefault(e => e.eventId == raidIdBefore);
                    if (raidEvent != null)
                    {
                        if (!string.IsNullOrEmpty(raidEvent.outcomeDescription))
                        {
                            string actionText = isSlave ? "enslaved" : "recruited";
                            if (!raidEvent.outcomeDescription.Contains("recruited and joined") && !raidEvent.outcomeDescription.Contains("enslaved and joined"))
                            {
                                raidEvent.outcomeDescription += " (Additionally, former raider " + __instance.LabelShort + " was " + actionText + " and joined the colony!)";
                            }
                            else
                            {
                                raidEvent.outcomeDescription = raidEvent.outcomeDescription.Replace("was recruited", "and " + __instance.LabelShort + " were recruited");
                                raidEvent.outcomeDescription = raidEvent.outcomeDescription.Replace("was enslaved", "and " + __instance.LabelShort + " were enslaved");
                            }
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Recipe_Surgery_ApplyOnPawn
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Recipe_InstallArtificialBodyPart), nameof(Recipe_InstallArtificialBodyPart.ApplyOnPawn));
            yield return AccessTools.Method(typeof(Recipe_InstallNaturalBodyPart), nameof(Recipe_InstallNaturalBodyPart.ApplyOnPawn));
        }

        [HarmonyPostfix]
        static void Postfix(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null || billDoer == null || part == null || bill == null) return;
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;

            var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            if (coreComp != null)
            {
                string partLabel = part.Label;
                string bionicLabel = bill.recipe?.LabelCap ?? "bionic part";
                
                string evDesc = billDoer.LabelShort + " successfully performed surgery on " + pawn.LabelShort + ", installing " + bionicLabel + " to restore their " + partLabel + ".";
                
                var ev = new PastEvent
                {
                    eventId = System.Guid.NewGuid().ToString(),
                    category = "BionicInstallation",
                    eventDescription = evDesc,
                    gameTick = Find.TickManager.TicksGame,
                    isResolved = true,
                    outcomeDescription = pawn.LabelShort + " is recovering well and functional again.",
                    outcome = EventOutcome.Success
                };
                coreComp.backlogQueueList.Add(ev);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
    internal static class Patch_Pawn_HealthTracker_RemoveHediff_LimbRegrowth
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_HealthTracker __instance, Hediff hediff)
        {
            if (hediff == null || hediff.pawn == null) return;
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;

            Pawn pawn = hediff.pawn;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) return;

            if (hediff is Hediff_MissingPart)
            {
                string partLabel = hediff.Part?.Label ?? "body part";

                var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
                if (coreComp != null)
                {
                    // Prevent double logging if a surgery restoration occurred in the same tick for this pawn
                    bool duplicate = coreComp.backlogQueueList.Any(e => 
                        e.category == "SurgeryRestoration" && 
                        e.gameTick == Find.TickManager.TicksGame && 
                        e.eventDescription.Contains(pawn.LabelShort));

                    if (!duplicate)
                    {
                        string evDesc = pawn.LabelShort + "'s " + partLabel + " was successfully restored and regrown.";
                        
                        var ev = new PastEvent
                        {
                            eventId = System.Guid.NewGuid().ToString(),
                            category = "SurgeryRestoration",
                            eventDescription = evDesc,
                            gameTick = Find.TickManager.TicksGame,
                            isResolved = true,
                            outcomeDescription = pawn.LabelShort + " has regained full functionality of their " + partLabel + ".",
                            outcome = EventOutcome.Success
                        };
                        coreComp.backlogQueueList.Add(ev);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    internal static class Patch_Pawn_DeSpawn_Visitor
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;
            if (__instance.Faction == null || __instance.Faction.IsPlayer || __instance.Faction.HostileTo(Faction.OfPlayer) || !__instance.RaceProps.Humanlike) return;

            var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            if (coreComp != null)
            {
                if (coreComp.visitorEntryTicks.TryGetValue(__instance.ThingID, out int entryTick))
                {
                    coreComp.visitorEntryTicks.Remove(__instance.ThingID);

                    int exitTick = Find.TickManager.TicksGame;
                    CheckWitnessedReconstruction(coreComp, __instance, entryTick, exitTick);
                }
            }
        }

        private static void CheckWitnessedReconstruction(SynapseCoreWorldComponent coreComp, Pawn visitor, int entryTick, int exitTick)
        {
            var eventsDuringVisit = coreComp.backlogQueueList.Where(e => 
                e.gameTick >= entryTick && 
                e.gameTick <= exitTick).ToList();

            if (!eventsDuringVisit.Any()) return;

            PastEvent maxEvent = null;
            float maxScore = -1f;
            foreach (var ev in eventsDuringVisit)
            {
                float score = coreComp.CalculateSignificance(ev);
                if (score > maxScore)
                {
                    maxScore = score;
                    maxEvent = ev;
                }
            }

            if (maxEvent == null) return;

            // Determine if the event is significant enough compared to other world events
            var otherEvents = coreComp.backlogQueueList.Where(e => e.gameTick < entryTick || e.gameTick > exitTick).ToList();
            float avgOtherScore = 30f;
            if (otherEvents.Any())
            {
                float sum = 0f;
                foreach (var oe in otherEvents)
                {
                    sum += coreComp.CalculateSignificance(oe);
                }
                avgOtherScore = sum / otherEvents.Count;
            }

            // We require the event to be significantly high (at least 60f) and equal to or higher than the average other events
            if (maxScore >= 60f && maxScore >= avgOtherScore)
            {
                string headline = visitor.LabelShort + " Spreads News of " + maxEvent.category + " at " + Faction.OfPlayer.Name + "!";
                string text = "While visiting the player colony, " + visitor.LabelShort + " from " + visitor.Faction.Name + 
                              " witnessed a major event: " + maxEvent.eventDescription + 
                              " Reports indicate the local outcome was: " + maxEvent.outcomeDescription + ".";

                try
                {
                    var worldComponents = Find.World?.components;
                    if (worldComponents != null)
                    {
                        foreach (var comp in worldComponents)
                        {
                            if (comp.GetType().Name == "SynapseWorldNewsWorldComponent")
                            {
                                var unpublishedEventsField = comp.GetType().GetField("unpublishedEvents");
                                if (unpublishedEventsField != null)
                                {
                                    var list = (System.Collections.IList)unpublishedEventsField.GetValue(comp);
                                    if (list != null)
                                    {
                                        string eventString = "[" + GenLocalDate.Twelfth(Find.TickManager.TicksGame) + ", " + GenLocalDate.Year(Find.TickManager.TicksGame) + "] Gossip: " + headline + " - " + text;
                                        list.Add(eventString);

                                        if (list.Count >= 4)
                                        {
                                            var triggerMethod = comp.GetType().GetMethod("TriggerNewspaperGeneration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            if (triggerMethod != null)
                                            {
                                                triggerMethod.Invoke(comp, null);
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning("Failed to route visitor news to WorldNews module dynamically: " + ex.Message);
                }

                // Log a Success event in the storyteller backlog representing the rumor spreading
                var coreEv = new PastEvent
                {
                    eventId = System.Guid.NewGuid().ToString(),
                    category = "VisitorRumorSpreading",
                    eventDescription = text,
                    gameTick = Find.TickManager.TicksGame,
                    isResolved = true,
                    outcomeDescription = visitor.LabelShort + " has spread this story across " + visitor.Faction.Name + " networks.",
                    outcome = EventOutcome.Success
                };
                coreComp.backlogQueueList.Add(coreEv);
            }
        }
    }
}
