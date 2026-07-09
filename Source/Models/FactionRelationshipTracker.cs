using System.Collections.Generic;
using Verse;

namespace RimSynapse.Models
{
    public class FactionRelationshipTracker : IExposable
    {
        public string factionId;
        public List<GoodwillSample> goodwillHistory = new List<GoodwillSample>();
        public float goodwillIntegral;
        public int lastEventTick;

        public float perceivedWealth;
        public float perceivedStrength;

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Collections.Look(ref goodwillHistory, "goodwillHistory", LookMode.Deep);
            Scribe_Values.Look(ref goodwillIntegral, "goodwillIntegral");
            Scribe_Values.Look(ref lastEventTick, "lastEventTick");
            
            // Ensure lists aren't null after load
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (goodwillHistory == null)
                    goodwillHistory = new List<GoodwillSample>();
            }

            Scribe_Values.Look(ref perceivedWealth, "perceivedWealth");
            Scribe_Values.Look(ref perceivedStrength, "perceivedStrength");
        }
    }
}
