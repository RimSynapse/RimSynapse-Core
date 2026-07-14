using Verse;

namespace RimSynapse.Models
{
    public class PastEvent : IExposable
    {
        public string eventId;
        public string parentEventId;
        public int gameTick;
        public SynapseDate date;
        public string eventDescription;
        public string category;
        public string factionName;
        public string settlementName;

        public string outcomeDescription;
        public EventOutcome outcome = EventOutcome.Unknown;
        public bool isResolved;
        public int resolvedTick;

        public float startWealth;
        public float endWealth;
        public float startFoodNutrition;
        public float endFoodNutrition;

        public string sourceFactionId;
        public string targetFactionId;

        public string colonySnapshot;
        public System.Collections.Generic.Dictionary<string, string> pawnSnapshots = new System.Collections.Generic.Dictionary<string, string>();

        public PastEvent()
        {
            eventId = System.Guid.NewGuid().ToString();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref eventId, "eventId");
            Scribe_Values.Look(ref parentEventId, "parentEventId");
            Scribe_Values.Look(ref gameTick, "gameTick");
            Scribe_Deep.Look(ref date, "date");
            Scribe_Values.Look(ref eventDescription, "eventDescription");
            Scribe_Values.Look(ref category, "category");
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref settlementName, "settlementName");
            Scribe_Values.Look(ref outcomeDescription, "outcomeDescription");
            Scribe_Values.Look(ref outcome, "outcome", EventOutcome.Unknown);
            Scribe_Values.Look(ref isResolved, "isResolved");
            Scribe_Values.Look(ref resolvedTick, "resolvedTick", 0);
            Scribe_Values.Look(ref startWealth, "startWealth", 0f);
            Scribe_Values.Look(ref endWealth, "endWealth", 0f);
            Scribe_Values.Look(ref startFoodNutrition, "startFoodNutrition", 0f);
            Scribe_Values.Look(ref endFoodNutrition, "endFoodNutrition", 0f);
            Scribe_Values.Look(ref sourceFactionId, "sourceFactionId");
            Scribe_Values.Look(ref targetFactionId, "targetFactionId");
            Scribe_Values.Look(ref colonySnapshot, "colonySnapshot");
            Scribe_Collections.Look(ref pawnSnapshots, "pawnSnapshots", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (pawnSnapshots == null) pawnSnapshots = new System.Collections.Generic.Dictionary<string, string>();
                if (string.IsNullOrEmpty(eventId)) eventId = System.Guid.NewGuid().ToString();
            }
        }
    }
}
