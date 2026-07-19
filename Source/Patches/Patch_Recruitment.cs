using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Utils;

namespace RimSynapse.Patches
{
    [HarmonyPatch(typeof(RimWorld.InteractionWorker_RecruitAttempt), "RecruitChance")]
    internal static class Patch_InteractionWorker_RecruitAttempt_RecruitChance
    {
        static void Postfix(ref float __result, Pawn recipient, Pawn recruiter)
        {
            if (recipient == null || recruiter == null) return;
            
            if (SynapseRecruitmentMath.IsPsychologyInstalled())
            {
                __result = SynapseRecruitmentMath.CalculateRecruitmentChance(recruiter, recipient, __result);
            }
        }
    }
}
