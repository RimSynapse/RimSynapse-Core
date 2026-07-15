using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimSynapse.Patches
{
    [HarmonyPatch(typeof(Building_TurretGun), "TryFindNewTarget")]
    public static class Patch_Building_TurretGun_TryFindNewTarget
    {
        public static void Postfix(Building_TurretGun __instance, ref LocalTargetInfo __result)
        {
            if (SynapseObjectControlManager.IsSabotaged(__instance))
            {
                Pawn target = SynapseObjectControlManager.GetOverrideTarget(__instance);
                if (target != null && !target.Dead && !target.Downed && target.Spawned && target.Map == __instance.Map)
                {
                    __result = new LocalTargetInfo(target);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Building_TurretGun), "IsValidTarget")]
    public static class Patch_Building_TurretGun_IsValidTarget
    {
        public static bool Prefix(Building_TurretGun __instance, Thing t, ref bool __result)
        {
            if (SynapseObjectControlManager.IsSabotaged(__instance))
            {
                Pawn target = SynapseObjectControlManager.GetOverrideTarget(__instance);
                if (target != null && t == target)
                {
                    __result = !target.Dead && !target.Downed && target.Spawned;
                    return false; // Skip vanilla validation checks
                }
            }
            return true;
        }
    }
}
