using Verse;

namespace RimSynapse
{
    /// <summary>
    /// A pawn's opinion score toward another pawn.
    /// </summary>
    public class OpinionEntry : IExposable
    {
        public string pawnName;
        public int opinion;

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Values.Look(ref opinion, "opinion");
        }
    }
}
