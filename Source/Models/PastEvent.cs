using Verse;

namespace RimSynapse.Models
{
    public class PastEvent : IExposable
    {
        public int gameTick;
        public SynapseDate date;
        public string eventDescription;
        public string category;
        public string factionName;
        public string settlementName;

        public string colonySnapshot;
        public System.Collections.Generic.Dictionary<string, string> pawnSnapshots = new System.Collections.Generic.Dictionary<string, string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref gameTick, "gameTick");
            Scribe_Deep.Look(ref date, "date");
            Scribe_Values.Look(ref eventDescription, "eventDescription");
            Scribe_Values.Look(ref category, "category");
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref settlementName, "settlementName");
            Scribe_Values.Look(ref colonySnapshot, "colonySnapshot");
            Scribe_Collections.Look(ref pawnSnapshots, "pawnSnapshots", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (pawnSnapshots == null) pawnSnapshots = new System.Collections.Generic.Dictionary<string, string>();
            }
        }
    }
}
