using Verse;

namespace RimSynapse.Models
{
    public class RaidOutcomeRecord : IExposable
    {
        public int gameTick;
        public int colonistsKilled;
        public int colonistsInjured;
        public int colonistsKidnapped;
        public int enemiesKilled;
        public int enemiesDowned;

        public RaidOutcomeRecord()
        {
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref gameTick, "gameTick");
            Scribe_Values.Look(ref colonistsKilled, "colonistsKilled");
            Scribe_Values.Look(ref colonistsInjured, "colonistsInjured");
            Scribe_Values.Look(ref colonistsKidnapped, "colonistsKidnapped");
            Scribe_Values.Look(ref enemiesKilled, "enemiesKilled");
            Scribe_Values.Look(ref enemiesDowned, "enemiesDowned");
        }
    }
}
