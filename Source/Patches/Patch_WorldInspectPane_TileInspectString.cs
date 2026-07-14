using HarmonyLib;
using RimWorld.Planet;
using Verse;
using RimSynapse.Utilities;

namespace RimSynapse.Patches
{
    [HarmonyPatch(typeof(WorldInspectPane), "TileInspectString", MethodType.Getter)]
    internal static class Patch_WorldInspectPane_TileInspectString
    {
        [HarmonyPostfix]
        static void Postfix(ref string __result)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;

            PlanetTile selectedTile = Find.WorldSelector.SelectedTile;
            if (selectedTile != PlanetTile.Invalid)
            {
                int tileId = selectedTile.tileId;
                int pop = PopulationDensityUtility.GetPopulationAtTile(tileId);
                if (pop > 0)
                {
                    if (!string.IsNullOrEmpty(__result))
                    {
                        __result += "\n";
                    }
                    __result += "Pawn dwellings: " + pop;
                }
            }
        }
    }
}
