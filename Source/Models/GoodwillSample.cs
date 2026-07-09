using Verse;

namespace RimSynapse.Models
{
    public class GoodwillSample : IExposable
    {
        public int gameTick;
        public int goodwill;

        public void ExposeData()
        {
            Scribe_Values.Look(ref gameTick, "gameTick");
            Scribe_Values.Look(ref goodwill, "goodwill");
        }
    }
}
