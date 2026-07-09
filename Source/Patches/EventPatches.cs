using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Models;
using System.Linq;

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
        }
    }
}
