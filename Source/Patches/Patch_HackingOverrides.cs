using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimSynapse.Patches
{
    [HarmonyPatch(typeof(CompPowerPlant), "get_DesiredPowerOutput")]
    public static class Patch_CompPowerPlant_DesiredPowerOutput
    {
        public static bool Prefix(CompPowerPlant __instance, ref float __result)
        {
            if (SynapseObjectControlManager.IsHacked(__instance.parent))
            {
                __result = 0f;
                return false; // Force output to 0, skipping vanilla computation
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Building_Door), "PawnCanOpen")]
    public static class Patch_Building_Door_PawnCanOpen
    {
        public static bool Prefix(Building_Door __instance, ref bool __result)
        {
            if (SynapseObjectControlManager.IsHacked(__instance) || SynapseObjectControlManager.LockedDoors.Contains(__instance.ThingID))
            {
                __result = false; // Remotely locked
                return false; // Skip vanilla open logic
            }
            return true;
        }
    }
}
