using Verse;

namespace RimSynapse
{
    /// <summary>
    /// Runtime context assembly settings. Primarily read from
    /// SynapseContextProfileDef and SynapseThoughtFilterDef XML,
    /// but can be overridden per-request via ChatOptions.
    /// </summary>
    public class ContextSettings : IExposable
    {
        // Section toggles (derived from profile tier selection)
        public bool includeBackstory = true;
        public bool includeTraits = true;
        public bool includeMood = true;
        public bool includeSkills = true;
        public bool includeHealth = true;
        public bool includeRelationships = true;
        public bool includeOpinions = true;
        public bool includeEquipment = false;
        public bool includeIdeology = true;
        public bool includeColony = true;
        public bool includeFactions = false;
        public bool includeQuests = false;
        public bool includeMemories = true;
        public bool includeThreads = true;

        // Limits
        public int memoryLimit = 5;
        public int opinionLimit = 5;
        public int eventLimit = 5;
        public float weightThreshold = 0.15f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref includeBackstory, "includeBackstory", true);
            Scribe_Values.Look(ref includeTraits, "includeTraits", true);
            Scribe_Values.Look(ref includeMood, "includeMood", true);
            Scribe_Values.Look(ref includeSkills, "includeSkills", true);
            Scribe_Values.Look(ref includeHealth, "includeHealth", true);
            Scribe_Values.Look(ref includeRelationships, "includeRelationships", true);
            Scribe_Values.Look(ref includeOpinions, "includeOpinions", true);
            Scribe_Values.Look(ref includeEquipment, "includeEquipment", false);
            Scribe_Values.Look(ref includeIdeology, "includeIdeology", true);
            Scribe_Values.Look(ref includeColony, "includeColony", true);
            Scribe_Values.Look(ref includeFactions, "includeFactions", false);
            Scribe_Values.Look(ref includeQuests, "includeQuests", false);
            Scribe_Values.Look(ref includeMemories, "includeMemories", true);
            Scribe_Values.Look(ref includeThreads, "includeThreads", true);
            Scribe_Values.Look(ref memoryLimit, "memoryLimit", 5);
            Scribe_Values.Look(ref opinionLimit, "opinionLimit", 5);
            Scribe_Values.Look(ref eventLimit, "eventLimit", 5);
            Scribe_Values.Look(ref weightThreshold, "weightThreshold", 0.15f);
        }
    }
}
