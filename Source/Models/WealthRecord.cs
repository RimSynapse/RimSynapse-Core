using Verse;

namespace RimSynapse.Models
{
    public class WealthRecord : IExposable
    {
        public int gameTick;
        public float wealth;
        public int pawnCount;

        public WealthRecord()
        {
        }

        public WealthRecord(int gameTick, float wealth, int pawnCount)
        {
            this.gameTick = gameTick;
            this.wealth = wealth;
            this.pawnCount = pawnCount;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref gameTick, "gameTick");
            Scribe_Values.Look(ref wealth, "wealth");
            Scribe_Values.Look(ref pawnCount, "pawnCount");
        }
    }
}
