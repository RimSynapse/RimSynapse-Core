using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Comps;

namespace RimSynapse.Patches
{
    /// <summary>
    /// Replaces vanilla's wealth-based threat calculation with a combat-competency model.
    /// The LLM's TensionModifier further scales the result for narrative pacing.
    /// </summary>
    [HarmonyPatch(typeof(StorytellerUtility), "DefaultThreatPointsNow")]
    public static class Patch_StorytellerUtility_DefaultThreatPointsNow
    {
        public static void Postfix(IIncidentTarget target, ref float __result)
        {
            if (Find.Storyteller?.storytellerComps?.OfType<StorytellerComp_Storyteller>().Any() == true)
            {
                var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
                if (coreComp != null)
                {
                    __result = coreComp.CalculateDynamicThreatPoints(target, __result);
                }
            }
        }
    }
}

