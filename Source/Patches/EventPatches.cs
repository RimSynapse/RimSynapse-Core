using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Models;
using System.Linq;

namespace RimSynapse.Patches
{
    [HarmonyPatch(typeof(TaleRecorder), "RecordTale")]
    internal static class TaleRecorder_RecordTale_Patch
    {
        static void Postfix(TaleDef def, params object[] args)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;

            var coreComp = Find.World.GetComponent<RimSynapse.SynapseCoreWorldComponent>();
            if (coreComp == null) return;

            // Attempt to find the primary pawn involved in this tale
            Pawn primaryPawn = args.OfType<Pawn>().FirstOrDefault();
            if (primaryPawn != null && (primaryPawn.IsColonist || primaryPawn.IsPrisonerOfColony))
            {
                // Push tale to the backlog
                string taleLabel = def.label ?? def.defName;
                string description = $"Tale Recorded: {taleLabel} involving {primaryPawn.Name.ToStringShort}.";
                
                PastEvent evt = new PastEvent
                {
                    gameTick = GenTicks.TicksGame,
                    category = "NativeTale",
                    eventDescription = description
                };
                
                coreComp.EnqueuePastEvent(evt);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class Pawn_Kill_Patch
    {
        static void Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;

            var coreComp = Find.World.GetComponent<RimSynapse.SynapseCoreWorldComponent>();
            if (coreComp == null) return;

            Pawn victim = __instance;
            Pawn killer = dinfo?.Instigator as Pawn;

            // Only log if a colonist was involved (died or killed)
            bool colonistInvolved = (victim.IsColonist || victim.IsPrisonerOfColony) || 
                                    (killer != null && (killer.IsColonist || killer.IsPrisonerOfColony));

            if (colonistInvolved)
            {
                string killerStr = killer != null ? killer.Name.ToStringShort : "Unknown Causes";
                string description = $"Death: {victim.Name.ToStringShort} was killed by {killerStr}.";
                
                PastEvent evt = new PastEvent
                {
                    gameTick = GenTicks.TicksGame,
                    category = "NativeDeath",
                    eventDescription = description
                };
                
                coreComp.EnqueuePastEvent(evt);
            }
        }
    }
}
