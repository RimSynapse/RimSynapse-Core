using Verse;

namespace RimSynapse
{
    /// <summary>
    /// A narrative thread entry for context serialization (Tier 5).
    /// Mirrors Storyteller's NarrativeThread for context injection.
    /// </summary>
    public class NarrativeThreadEntry : IExposable
    {
        public string keyword;
        public string category;
        public string description;
        public float weight;
        public bool isResolved;

        public void ExposeData()
        {
            Scribe_Values.Look(ref keyword, "keyword");
            Scribe_Values.Look(ref category, "category");
            Scribe_Values.Look(ref description, "description");
            Scribe_Values.Look(ref weight, "weight");
            Scribe_Values.Look(ref isResolved, "isResolved");
        }
    }
}
