using System.Collections.Generic;
using Verse;

namespace RimSynapse
{
    /// <summary>
    /// Colony-wide state data for context injection.
    /// </summary>
    public class ColonyPacket : IExposable
    {
        public int colonistCount;
        public float wealthTotal;
        public string season;
        public string weather;
        public string biome;
        public string dangerLevel;
        public string currentResearch;
        public float researchProgress;
        public List<string> recentEvents;

        public void ExposeData()
        {
            Scribe_Values.Look(ref colonistCount, "colonistCount");
            Scribe_Values.Look(ref wealthTotal, "wealthTotal");
            Scribe_Values.Look(ref season, "season");
            Scribe_Values.Look(ref weather, "weather");
            Scribe_Values.Look(ref biome, "biome");
            Scribe_Values.Look(ref dangerLevel, "dangerLevel");
            Scribe_Values.Look(ref currentResearch, "currentResearch");
            Scribe_Values.Look(ref researchProgress, "researchProgress");
            Scribe_Collections.Look(ref recentEvents, "recentEvents", LookMode.Value);
        }
    }
}
