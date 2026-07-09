using Verse;

namespace RimSynapse
{
    /// <summary>
    /// A filtered pawn thought for context injection.
    /// </summary>
    public class ThoughtEntry : IExposable
    {
        public string label;
        public float moodOffset;
        public float ageHours;
        public bool isRecent;

        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref moodOffset, "moodOffset");
            Scribe_Values.Look(ref ageHours, "ageHours");
            Scribe_Values.Look(ref isRecent, "isRecent");
        }
    }
}
