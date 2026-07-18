using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSynapse.Patches
{
    [HarmonyPatch(typeof(Pawn_JobTracker), "TryTakeOrderedJob")]
    public static class Patch_Pawn_JobTracker_TryTakeOrderedJob
    {
        public static bool Prefix(Pawn_JobTracker __instance, Job job, Pawn ___pawn)
        {
            if (___pawn != null && SynapsePossessionManager.IsPossessed(___pawn))
            {
                // If this is currently being ordered by our possession tool, allow it!
                if (SynapsePossessionManager.IsExecutingPossessionJob)
                {
                    return true;
                }
                // Otherwise, reject the player's ordered moves/actions!
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn_DraftController), "set_Drafted")]
    public static class Patch_Pawn_DraftController_set_Drafted
    {
        public static bool Prefix(Pawn_DraftController __instance, bool value)
        {
            Pawn pawn = __instance.pawn;
            if (pawn != null && SynapsePossessionManager.IsPossessed(pawn))
            {
                // If our possession tool is ordering it, allow it!
                if (SynapsePossessionManager.IsExecutingPossessionJob)
                {
                    return true;
                }
                // Otherwise block player's manual draft button clicks
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn), "PostApplyDamage")]
    public static class Patch_Pawn_PostApplyDamage
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (totalDamageDealt > 0f)
            {
                SynapsePossessionManager.OnPawnTookDamage(__instance);
                if (__instance.IsColonist)
                {
                    var extra = new Dictionary<string, string>
                    {
                        { "damageAmount", totalDamageDealt.ToString("F1") },
                        { "damageDef", dinfo.Def?.defName ?? "Unknown" },
                        { "instigator", dinfo.Instigator?.LabelShort ?? "Unknown" }
                    };
                    SynapseTriggerManager.TriggerEvent("PawnInjured", __instance, extra);
                }
            }
        }
    }
}
