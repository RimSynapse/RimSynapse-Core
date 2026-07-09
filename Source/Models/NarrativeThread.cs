using Verse;

namespace RimSynapse.Models
{
    public class NarrativeThread : IExposable
    {
        public string keyword;
        public string category;
        public string description;
        public float weight;
        public float decayRate = 0.03f;
        public int timesReferenced;
        public bool isResolved;
        public string resolutionSummary;

        public void ExposeData()
        {
            Scribe_Values.Look(ref keyword, "keyword");
            Scribe_Values.Look(ref category, "category");
            Scribe_Values.Look(ref description, "description");
            Scribe_Values.Look(ref weight, "weight");
            Scribe_Values.Look(ref decayRate, "decayRate", 0.03f);
            Scribe_Values.Look(ref timesReferenced, "timesReferenced");
            Scribe_Values.Look(ref isResolved, "isResolved");
            Scribe_Values.Look(ref resolutionSummary, "resolutionSummary");
        }
    }
}
