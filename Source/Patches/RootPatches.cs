using HarmonyLib;
using Verse;

namespace RimSynapse.Patches
{
    /// <summary>
    /// Harmony patch on Root.OnDestroy for clean shutdown.
    /// Disposes HTTP client, stops timers, flushes queue.
    /// </summary>
    [HarmonyPatch(typeof(Root), nameof(Root.OnDestroy))]
    internal static class Root_OnDestroy_Patch
    {
        static void Postfix()
        {
            SynapseCore.Shutdown();
        }
    }
}
