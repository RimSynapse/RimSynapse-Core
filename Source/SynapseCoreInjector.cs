using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Comps;

namespace RimSynapse
{
    [StaticConstructorOnStartup]
    public static class SynapseCoreInjector
    {
        static SynapseCoreInjector()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.Where(d => d.race != null && d.race.Humanlike))
            {
                if (def.comps == null)
                {
                    def.comps = new System.Collections.Generic.List<CompProperties>();
                }
                
                // Only add if not already present
                if (!def.comps.Any(c => c.compClass == typeof(SynapseCorePawnComp)))
                {
                    def.comps.Add(new CompProperties
                    {
                        compClass = typeof(SynapseCorePawnComp)
                    });
                }
            }
        }
    }
}
