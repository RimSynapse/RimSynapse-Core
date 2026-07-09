using Verse;

namespace RimSynapse.Models
{
    /// <summary>
    /// Slim base faction tracker owned by Core.
    /// Stores only the fields Core needs: faction identity and 
    /// perceived wealth/strength from the knowledge propagation system.
    /// StoryTeller-specific fields (goodwill history, custom natural goodwill,
    /// faction history) live in StoryTeller's extended tracker.
    /// </summary>
    public class FactionRelationshipTracker : IExposable
    {
        public string factionId;

        public float perceivedWealth;
        public float perceivedStrength;

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Values.Look(ref perceivedWealth, "perceivedWealth");
            Scribe_Values.Look(ref perceivedStrength, "perceivedStrength");
        }
    }
}
