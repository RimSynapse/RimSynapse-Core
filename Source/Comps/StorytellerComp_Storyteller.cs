using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimSynapse.Comps
{
    /// <summary>
    /// Main storyteller component. Handles the interval tick loop
    /// that decides which events fire, factoring in LLM pacing and faction perceptions.
    /// </summary>
    public partial class StorytellerComp_Storyteller : StorytellerComp
    {
        protected StorytellerCompProperties_Storyteller Props => (StorytellerCompProperties_Storyteller)props;

        public static StorytellerCompProperties_Storyteller GetActiveStorytellerProps()
        {
            var storytellerComp = Find.Storyteller?.storytellerComps?.OfType<StorytellerComp_Storyteller>().FirstOrDefault();
            return storytellerComp?.props as StorytellerCompProperties_Storyteller;
        }

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            var coreComp = Find.World.GetComponent<RimSynapse.SynapseCoreWorldComponent>();
            if (coreComp == null) yield break;

            var settings = RimSynapseMod.Instance?.Settings;

            if (Find.CurrentMap != null)
            {
                int currentHour = GenLocalDate.HourOfDay(Find.CurrentMap);
                bool triggerPacing = false;

                if (settings?.enableTrainingMode == true && settings?.fastTelemetryMode == true)
                {
                    triggerPacing = (Find.TickManager.TicksGame % 1000 == 0);
                }
                else
                {
                    triggerPacing = (currentHour % 6 == 0 && coreComp.lastInvestigationHour != currentHour);
                }

                if (triggerPacing)
                {
                    coreComp.lastInvestigationHour = currentHour;
                    RimSynapse.Comps.SynapseStorytellerOpportunistic.TriggerPacingAdjustment();
                }
            }

            if (settings?.enableTrainingMode == true && settings?.fastTelemetryMode == true)
            {
                if (Find.TickManager.TicksGame % 2000 == 0)
                {
                    var categories = new List<IncidentCategoryDef> { IncidentCategoryDefOf.ThreatBig, IncidentCategoryDefOf.ThreatSmall, IncidentCategoryDefOf.Misc };
                    var category = categories.RandomElement();
                    RimSynapse.Comps.SynapseStorytellerOpportunistic.TriggerEventSelection(category, target);
                }
                yield break;
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
                    int pop = 0;
                    if (target.Tile >= 0 && SynapseCoreWorldComponent.GetPopulationDensityDelegate != null)
                    {
                        pop = SynapseCoreWorldComponent.GetPopulationDensityDelegate(target.Tile);
                    }
                    float raidMult = 1f / (1f + Props.motivatedRaidPopulationDensityFactor * pop);

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
