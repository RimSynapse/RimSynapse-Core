using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimSynapse.Comps
{
    /// <summary>
    /// Helper methods for the Aura storyteller component:
    /// faction motivation checks, category selection, and LLM-weighted incident picking.
    /// </summary>
    public partial class StorytellerComp_Aura
    {
        /// <summary>
        /// Checks all hostile factions. If one perceives the colony as wealthy but weak
        /// (high greed ratio), it becomes highly motivated to invade.
        /// </summary>
        private Faction GetMotivatedFaction(SynapseCoreWorldComponent coreComp)
        {
            if (coreComp == null) return null;

            foreach (var tracker in coreComp.factionTrackers)
            {
                Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == tracker.factionId);
                if (faction != null && faction.HostileTo(Faction.OfPlayer))
                {
                    float normalizedStrength = (tracker.perceivedStrength * 50f) + 1f;
                    float greedRatio = tracker.perceivedWealth / normalizedStrength;

                    if (greedRatio > 3f && Rand.Chance(0.2f))
                    {
                        tracker.perceivedStrength += 500f; 
                        return faction;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Selects an incident category (ThreatBig, Misc, Disease, etc.) using
        /// base weights modified by the LLM's category multipliers.
        /// </summary>
        private IncidentCategoryDef ChooseCategory(IIncidentTarget target, SynapseCoreWorldComponent worldComp)
        {
            var weights = new Dictionary<IncidentCategoryDef, float>();
            
            weights[IncidentCategoryDefOf.ThreatBig] = 2f;
            weights[IncidentCategoryDefOf.ThreatSmall] = 1f;
            weights[IncidentCategoryDefOf.DiseaseHuman] = 0.5f;
            weights[IncidentCategoryDefOf.Misc] = 3f;
            
            var diseaseAnimal = DefDatabase<IncidentCategoryDef>.GetNamedSilentFail("DiseaseAnimal");
            if (diseaseAnimal != null) weights[diseaseAnimal] = 0.2f;

            var orbitalVisitor = DefDatabase<IncidentCategoryDef>.GetNamedSilentFail("OrbitalVisitor");
            if (orbitalVisitor != null) weights[orbitalVisitor] = 1f;

            var factionArrival = DefDatabase<IncidentCategoryDef>.GetNamedSilentFail("FactionArrival");
            if (factionArrival != null) weights[factionArrival] = 1f;

            if (worldComp != null)
            {
                foreach (var category in weights.Keys.ToList())
                {
                    weights[category] *= worldComp.GetCategoryMultiplier(category.defName);
                }
            }

            return weights.RandomElementByWeightWithFallback(kvp => kvp.Value, default).Key;
        }

    }
}
