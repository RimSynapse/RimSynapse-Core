using System.Collections.Generic;
using Verse;

namespace RimSynapse
{
    /// <summary>
    /// A weighted memory from Psychology mod (Tier 5).
    /// Mirrors the WeightedMemory structure for context serialization.
    /// </summary>
    public class MemoryEntry : IExposable
    {
        public string summary;
        public string memoryType;
        public List<string> tags;
        public float weight;

        public void ExposeData()
        {
            Scribe_Values.Look(ref summary, "summary");
            Scribe_Values.Look(ref memoryType, "memoryType");
            Scribe_Collections.Look(ref tags, "tags", LookMode.Value);
            Scribe_Values.Look(ref weight, "weight");
        }
    }
}
