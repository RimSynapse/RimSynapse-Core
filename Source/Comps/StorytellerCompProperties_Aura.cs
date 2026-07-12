using RimWorld;

namespace RimSynapse.Comps
{
    public class StorytellerCompProperties_Aura : StorytellerCompProperties
    {
        public float incidentsTargetDays = 10f;
        public float threatsTargetDays = 10f;

        public StorytellerCompProperties_Aura()
        {
            this.compClass = typeof(StorytellerComp_Aura);
        }
    }
}
