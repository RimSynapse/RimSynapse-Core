using Verse;

namespace RimSynapse.Models
{
    public class OpinionSample : IExposable
    {
        public string targetPawnId;
        public int opinion;
        public int gameTick;

        public OpinionSample()
        {
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref targetPawnId, "targetPawnId");
            Scribe_Values.Look(ref opinion, "opinion");
            Scribe_Values.Look(ref gameTick, "gameTick");
        }
    }
}
