using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Models;

namespace RimSynapse
{
    /// <summary>
    /// Storyteller-related methods: category/incident multipliers, threat points,
    /// wealth calculation, and wealth growth pacing.
    /// </summary>
    public partial class SynapseCoreWorldComponent
    {
        public float GetCategoryMultiplier(string categoryDefName)
        {
            if (categoryMultipliers.TryGetValue(categoryDefName, out float mult))
            {
                return mult;
            }
            return 1.0f;
        }

        public float GetIncidentMultiplier(string incidentDefName)
        {
            if (incidentMultipliers.TryGetValue(incidentDefName, out float mult))
            {
                return mult;
            }
            return 1.0f;
        }

        public float CalculateDynamicThreatPoints(IIncidentTarget target, float vanillaPoints)
        {
            Map map = target as Map;
            if (map == null) return vanillaPoints * TensionModifier;

            float combatCompetence = CalculateCombatCompetence(map);
            float securityPower = CalculateSecurityPower(map);
            int freeColonists = map.mapPawns?.FreeColonistsSpawned?.Count(p => !p.Downed && !p.Dead) ?? 0;

            float baseColonistPoints = freeColonists * 35f;
            float actualThreat = (baseColonistPoints + combatCompetence + securityPower) * TensionModifier;

            return UnityEngine.Mathf.Clamp(actualThreat, 35f, 10000f);
        }

        public void RegisterFiredIncident(string incidentDefName)
        {
            if (string.IsNullOrEmpty(incidentDefName)) return;
            firedIncidentHistory.Add(new FiredIncidentRecord(incidentDefName, Find.TickManager.TicksGame));
        }

        public int GetRecentIncidentCount(string incidentDefName, int lookbackTicks)
        {
            if (string.IsNullOrEmpty(incidentDefName)) return 0;
            int cutoff = Find.TickManager.TicksGame - lookbackTicks;
            return firedIncidentHistory.Count(r => r.incidentDefName == incidentDefName && r.gameTick >= cutoff);
        }

        public float CalculateTrueWealth(Map map, RimSynapse.Comps.StorytellerCompProperties_Storyteller props)
        {
            if (map == null) return 0f;
            if (props == null) return map.wealthWatcher?.WealthTotal ?? 0f;

            float items = map.wealthWatcher?.WealthItems ?? 0f;
            float buildings = map.wealthWatcher?.WealthBuildings ?? 0f;
            float pawns = map.wealthWatcher?.WealthPawns ?? 0f;

            float combatCompetence = CalculateCombatCompetence(map);
            float securityPower = CalculateSecurityPower(map);

            return (items * props.weightItemWealth) +
                   (buildings * props.weightBuildingWealth) +
                   (pawns * props.weightPawnWealth) +
                   (combatCompetence * props.weightCombatCompetence) +
                   (securityPower * props.weightSecurityPower);
        }

        private float CalculateCombatCompetence(Map map)
        {
            float combatCompetence = 0f;
            if (map.mapPawns?.FreeColonistsSpawned == null) return combatCompetence;

            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Downed || pawn.Dead) continue;

                combatCompetence += (pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0) * 5f;
                combatCompetence += (pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0) * 5f;

                if (pawn.equipment?.Primary != null)
                {
                    combatCompetence += pawn.equipment.Primary.MarketValue / 10f;
                }
                
                if (pawn.apparel?.WornApparel != null)
                {
                    foreach (var app in pawn.apparel.WornApparel)
                    {
                        combatCompetence += app.MarketValue / 20f;
                    }
                }
            }

            return combatCompetence;
        }

        private float CalculateSecurityPower(Map map)
        {
            float securityPower = 0f;
            if (map.listerThings == null) return securityPower;

            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (t.def.building != null && t.def.building.IsTurret)
                {
                    securityPower += t.MarketValue / 5f;
                }
            }
            return securityPower;
        }

        private void CheckWealthGrowthPacing()
        {
            var props = Find.Storyteller?.storytellerComps?.OfType<RimSynapse.Comps.StorytellerComp_Storyteller>().FirstOrDefault()?.props as RimSynapse.Comps.StorytellerCompProperties_Storyteller;
            if (props == null || Find.AnyPlayerHomeMap == null) return;

            Map map = Find.AnyPlayerHomeMap;
            float curWealth = CalculateTrueWealth(map, props);
            int curColonists = map.mapPawns.FreeColonistsCount;

            wealthHistory.Add(new WealthRecord(Find.TickManager.TicksGame, curWealth, curColonists));
            wealthHistory.RemoveAll(r => Find.TickManager.TicksGame - r.gameTick > 300000);

            if (wealthHistory.Count < 2) return;

            var oldest = wealthHistory[0];
            int tickDiff = Find.TickManager.TicksGame - oldest.gameTick;
            float days = (float)tickDiff / 60000f;
            if (days < 0.5f) return;

            float wealthDiff = curWealth - oldest.wealth;
            float avgColonists = 0.5f * (curColonists + oldest.pawnCount);
            if (avgColonists < 1) avgColonists = 1;

            float actualGrowthRate = wealthDiff / (avgColonists * days);
            float daysPassed = Find.TickManager.TicksGame / 60000f;
            float targetRate = props.targetWealthGrowthFactor * (float)System.Math.Pow(daysPassed, props.targetWealthGrowthExponent) + props.targetWealthGrowthBase;

            float blendedTargetRate = UnityEngine.Mathf.Lerp(targetRate, actualGrowthRate, props.pacingFlexibility);

            if (blendedTargetRate <= 0) return;

            float paceMultiplier;
            if (actualGrowthRate >= 0)
            {
                float ratio = actualGrowthRate / blendedTargetRate;
                paceMultiplier = UnityEngine.Mathf.Lerp(0.5f, 2.0f, ratio / 2.0f);
            }
            else
            {
                float lossRatio = UnityEngine.Mathf.Abs(actualGrowthRate) / blendedTargetRate;
                paceMultiplier = UnityEngine.Mathf.Lerp(0.5f, 0.25f, lossRatio / 2.0f);
            }

            BasePacingMultiplier = UnityEngine.Mathf.Lerp(BasePacingMultiplier, paceMultiplier, 0.3f);
            BasePacingMultiplier = UnityEngine.Mathf.Clamp(BasePacingMultiplier, 0.2f, 3.0f);
            GlobalPacingMultiplier = BasePacingMultiplier;

            SynapseLogger.Message($"[RimSynapse-Core] Daily pacing check: Actual Growth/Pawn/Day: {actualGrowthRate:F0} (Blended Target: {blendedTargetRate:F0}). Adjusting BasePacingMultiplier smoothly to: {BasePacingMultiplier:F2}");
        }
    }
}
