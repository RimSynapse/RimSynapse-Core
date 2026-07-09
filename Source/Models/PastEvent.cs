using Verse;

namespace RimSynapse.Models
{
    public class PastEvent : IExposable
    {
        public int gameTick;
        public string eventDescription;
        public string category;

        public string colonySnapshot;
        public System.Collections.Generic.Dictionary<string, string> pawnSnapshots = new System.Collections.Generic.Dictionary<string, string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref gameTick, "gameTick");
            Scribe_Values.Look(ref eventDescription, "eventDescription");
            Scribe_Values.Look(ref category, "category");
            Scribe_Values.Look(ref colonySnapshot, "colonySnapshot");
            Scribe_Collections.Look(ref pawnSnapshots, "pawnSnapshots", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (pawnSnapshots == null) pawnSnapshots = new System.Collections.Generic.Dictionary<string, string>();
            }
        }
    }
}
