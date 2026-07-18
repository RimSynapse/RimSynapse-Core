using Verse;

namespace RimSynapse.Models
{
    public class FiredIncidentRecord : IExposable
    {
        public string incidentDefName;
        public int gameTick;

        public FiredIncidentRecord()
        {
        }

        public FiredIncidentRecord(string incidentDefName, int gameTick)
        {
            this.incidentDefName = incidentDefName;
            this.gameTick = gameTick;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref incidentDefName, "incidentDefName");
            Scribe_Values.Look(ref gameTick, "gameTick");
        }
    }
}
