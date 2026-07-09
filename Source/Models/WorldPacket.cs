using System.Collections.Generic;
using Verse;

namespace RimSynapse
{
    /// <summary>
    /// World and faction data for context injection.
    /// Core reads raw vanilla faction data. Enriched fields
    /// (integral, trajectory, power) are populated by Storyteller if loaded.
    /// </summary>
    public class WorldPacket : IExposable
    {
        public List<FactionEntry> factions;
        public List<string> activeQuestNames;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref factions, "factions", LookMode.Deep);
            Scribe_Collections.Look(ref activeQuestNames, "activeQuestNames", LookMode.Value);
        }
    }
}
