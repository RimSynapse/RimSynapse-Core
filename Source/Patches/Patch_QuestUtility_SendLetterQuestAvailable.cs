using HarmonyLib;
using RimWorld;

namespace RimSynapse.Patches
{
    /// <summary>
    /// Intercepts quest letter delivery to rewrite quest titles and descriptions via LLM,
    /// giving each quest a narrative flavor that fits your colony's current story.
    /// </summary>
    [HarmonyPatch(typeof(QuestUtility), "SendLetterQuestAvailable")]
    public static class Patch_QuestUtility_SendLetterQuestAvailable
    {
        public static bool Prefix(Quest quest)
        {
            // OBSOLETE: Quest and threat letter rewrites are now handled by 
            // Patch_LetterStack_ReceiveLetter to ensure grammar is fully resolved.
            return true;
        }
    }
}
