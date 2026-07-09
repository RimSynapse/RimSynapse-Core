using Verse;
using RimWorld;

namespace RimSynapse.Models
{
    public class KnowledgePacket : IExposable
    {
        public string sourceFactionId;
        public string targetFactionId;
        public float payloadWealth;
        public float payloadStrength;
        public int arrivalTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref sourceFactionId, "sourceFactionId");
            Scribe_Values.Look(ref targetFactionId, "targetFactionId");
            Scribe_Values.Look(ref payloadWealth, "payloadWealth");
            Scribe_Values.Look(ref payloadStrength, "payloadStrength");
            Scribe_Values.Look(ref arrivalTick, "arrivalTick");
        }
    }
}
