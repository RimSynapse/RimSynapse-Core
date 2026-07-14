using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimSynapse.Comps
{
    /// <summary>
    /// Main Aura Algorithm storyteller component. Handles the interval tick loop
    /// that decides which events fire, factoring in LLM pacing and faction perceptions.
    /// </summary>
    public partial class StorytellerComp_Aura : StorytellerComp
    {
        protected StorytellerCompProperties_Aura Props => (StorytellerCompProperties_Aura)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            var coreComp = Find.World.GetComponent<RimSynapse.SynapseCoreWorldComponent>();
            if (coreComp == null) yield break;

            if (Find.CurrentMap != null)
            {
                int currentHour = GenLocalDate.HourOfDay(Find.CurrentMap);

                // Check every 6 hours
                if (currentHour % 6 == 0 && coreComp.lastInvestigationHour != currentHour)
                {
                    coreComp.lastInvestigationHour = currentHour;
                    RimSynapse.Comps.SynapseStorytellerOpportunistic.TriggerPacingAdjustment();
                }
            }

            float pacingMultiplier = coreComp.GlobalPacingMultiplier;
            
            // Adjust the target days (higher multiplier means fewer days between incidents)
            float actualTargetDays = Props.incidentsTargetDays / Math.Max(0.1f, pacingMultiplier);

            float probPerTick = 1f / (actualTargetDays * 60000f);
            float probPerCheck = probPerTick * 1000f; // Storyteller usually checks every 1000 ticks

            if (Rand.Chance(probPerCheck))
            {
                // PERCEPTION CHECK: Does a hostile faction see us as an easy target?
                Faction highlyMotivatedFaction = GetMotivatedFaction(coreComp);
                
                if (highlyMotivatedFaction != null)
                {
                    // Scale motivated raid chance down if population density is high
                    int pop = target.Tile >= 0 ? RimSynapse.Utilities.PopulationDensityUtility.GetPopulationAtTile(target.Tile) : 0;
                    float raidMult = 1f / (1f + 0.005f * pop);

                    if (Rand.Chance(raidMult))
                    {
                        IncidentParms raidParms = GenerateParms(IncidentCategoryDefOf.ThreatBig, target);
                        raidParms.faction = highlyMotivatedFaction;
                        
                        // Force drop pods if they are rich and far away
                        if (highlyMotivatedFaction.def.techLevel >= TechLevel.Industrial && Rand.Chance(0.5f))
                        {
                            raidParms.raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop;
                        }
                        else
                        {
                            raidParms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
                        }

                        if (IncidentDefOf.RaidEnemy.Worker.CanFireNow(raidParms))
                        {
                            yield return new FiringIncident(IncidentDefOf.RaidEnemy, this, raidParms);
                        }
                    }
                }
                else
                {
                    IncidentCategoryDef category = ChooseCategory(target, coreComp);
                    if (category != null)
                    {
                        // Temporarily reduce pacing to prevent further events while LLM is thinking
                        coreComp.GlobalPacingMultiplier = 0.001f;
                        RimSynapse.Comps.SynapseStorytellerOpportunistic.TriggerEventSelection(category, target);
                    }
                }
            }
        }
    }
}
