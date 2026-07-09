using Verse;

namespace RimSynapse
{
    /// <summary>
    /// A direct social relationship (Spouse, Rival, Friend, etc.).
    /// </summary>
    public class RelationshipEntry : IExposable
    {
        public string relationLabel;
        public string otherPawnName;
        public string otherPawnId;

        public void ExposeData()
        {
            Scribe_Values.Look(ref relationLabel, "relationLabel");
            Scribe_Values.Look(ref otherPawnName, "otherPawnName");
            Scribe_Values.Look(ref otherPawnId, "otherPawnId");
        }
    }
}
