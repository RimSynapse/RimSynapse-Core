using Verse;

namespace RimSynapse.Models
{
    /// <summary>
    /// Tracks combat performance and casualties during an active raid.
    /// Used to calculate a narrative "Tactician Score" at resolution time.
    /// </summary>
    public class RaidTracker : IExposable
    {
        public string raidEventId;
        public float startWealth;
        public int startColonistsCount;
        public int enemiesKilled;
        public int enemiesDowned;
        public int colonistsInjured;
        public int colonistsKilled;
        public int colonistsKidnapped;
        public int livestockInjured;
        public int livestockKilled;
        public System.Collections.Generic.List<string> lostLivestockDetails = new System.Collections.Generic.List<string>();

        public RaidTracker()
        {
        }

        public RaidTracker(string raidEventId, float startWealth, int startColonistsCount)
        {
            this.raidEventId = raidEventId;
            this.startWealth = startWealth;
            this.startColonistsCount = startColonistsCount;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref raidEventId, "raidEventId");
            Scribe_Values.Look(ref startWealth, "startWealth", 0f);
            Scribe_Values.Look(ref startColonistsCount, "startColonistsCount", 0);
            Scribe_Values.Look(ref enemiesKilled, "enemiesKilled", 0);
            Scribe_Values.Look(ref enemiesDowned, "enemiesDowned", 0);
            Scribe_Values.Look(ref colonistsInjured, "colonistsInjured", 0);
            Scribe_Values.Look(ref colonistsKilled, "colonistsKilled", 0);
            Scribe_Values.Look(ref colonistsKidnapped, "colonistsKidnapped", 0);
            Scribe_Values.Look(ref livestockInjured, "livestockInjured", 0);
            Scribe_Values.Look(ref livestockKilled, "livestockKilled", 0);
            Scribe_Collections.Look(ref lostLivestockDetails, "lostLivestockDetails", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (lostLivestockDetails == null) lostLivestockDetails = new System.Collections.Generic.List<string>();
            }
        }
    }
}
