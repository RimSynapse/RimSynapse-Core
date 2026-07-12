using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using RimSynapse.Models;

namespace RimSynapse
{
    public class SynapseCoreWorldComponent : WorldComponent
    {
        public List<NarrativeThread> narrativeThreads = new List<NarrativeThread>();
        public List<FactionRelationshipTracker> factionTrackers = new List<FactionRelationshipTracker>();
        public List<ShortTermEvent> shortTermEvents = new List<ShortTermEvent>();
        
        public List<PastEvent> backlogQueueList = new List<PastEvent>();
        private Queue<PastEvent> _backlogQueue = new Queue<PastEvent>();
        private int lastPurgeTick = -1;

        // Storyteller properties
        public Dictionary<string, float> categoryMultipliers = new Dictionary<string, float>();
        public Dictionary<string, float> incidentMultipliers = new Dictionary<string, float>();
        public float GlobalPacingMultiplier = 1.0f;
        public float BasePacingMultiplier = 1.0f;
        public float TensionModifier = 1.0f;
        public int lastInvestigationHour = -1;

        public SynapseCoreWorldComponent(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Collections.Look(ref narrativeThreads, "narrativeThreads", LookMode.Deep);
            Scribe_Collections.Look(ref factionTrackers, "factionTrackers", LookMode.Deep);
            Scribe_Collections.Look(ref shortTermEvents, "shortTermEvents", LookMode.Deep);
            Scribe_Collections.Look(ref backlogQueueList, "backlogQueueList", LookMode.Deep);

            // Storyteller properties
            Scribe_Collections.Look(ref categoryMultipliers, "categoryMultipliers", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref incidentMultipliers, "incidentMultipliers", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref GlobalPacingMultiplier, "globalPacingMultiplier", 1.0f);
            Scribe_Values.Look(ref BasePacingMultiplier, "basePacingMultiplier", 1.0f);
            Scribe_Values.Look(ref TensionModifier, "tensionModifier", 1.0f);
            Scribe_Values.Look(ref lastInvestigationHour, "lastInvestigationHour", -1);

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                backlogQueueList.Clear();
                backlogQueueList.AddRange(_backlogQueue);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (narrativeThreads == null) narrativeThreads = new List<NarrativeThread>();
                if (factionTrackers == null) factionTrackers = new List<FactionRelationshipTracker>();
                if (shortTermEvents == null) shortTermEvents = new List<ShortTermEvent>();
                if (backlogQueueList == null) backlogQueueList = new List<PastEvent>();
                
                if (categoryMultipliers == null) categoryMultipliers = new Dictionary<string, float>();
                if (incidentMultipliers == null) incidentMultipliers = new Dictionary<string, float>();
                
                _backlogQueue.Clear();
                foreach (var pastEvent in backlogQueueList)
                {
                    _backlogQueue.Enqueue(pastEvent);
                }
            }
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            int currentTick = Find.TickManager.TicksGame;
            
            // Purge short-term memory every 2500 ticks (1 in-game hour)
            if (currentTick - lastPurgeTick > 2500)
            {
                lastPurgeTick = currentTick;
                float hours = RimSynapseMod.Instance.Settings.shortTermMemoryHours;
                int maxAgeTicks = (int)(hours * 2500f);
                
                shortTermEvents.RemoveAll(e => currentTick - e.gameTick > maxAgeTicks);
            }
        }

        public void LogShortTermInteraction(Pawn initiator, Pawn recipient, InteractionDef intDef)
        {
            var stEvent = new ShortTermEvent
            {
                gameTick = Find.TickManager.TicksGame,
                date = SynapseDate.Now(),
                eventType = ShortTermEventType.Generic,
                involvedPawnIds = new List<string> { initiator.ThingID, recipient.ThingID }
            };

            // Filter generic chitchat vs deep talk
            if (intDef == InteractionDefOf.Chitchat)
            {
                // In RimWorld, Chitchat generally results in small positive opinion. 
                // A true negative outcome would usually be a slight or insult.
                stEvent.eventType = ShortTermEventType.PositiveInteraction;
                stEvent.description = $"{initiator.NameShortColored} and {recipient.NameShortColored} shared positive chitchat.";
            }
            else if (intDef == InteractionDefOf.DeepTalk)
            {
                stEvent.eventType = ShortTermEventType.DeepTalk;
                stEvent.description = $"{initiator.NameShortColored} and {recipient.NameShortColored} had a deep talk.";
            }
            else if (intDef.defName == "Slight" || intDef == InteractionDefOf.Insult)
            {
                stEvent.eventType = ShortTermEventType.NegativeInteraction;
                stEvent.description = $"{initiator.NameShortColored} insulted or slighted {recipient.NameShortColored}.";
            }
            else if (intDef == InteractionDefOf.RomanceAttempt)
            {
                stEvent.eventType = ShortTermEventType.DeepTalk;
                stEvent.description = $"{initiator.NameShortColored} flirted with {recipient.NameShortColored}.";
            }
            else
            {
                stEvent.description = $"{initiator.NameShortColored} interacted with {recipient.NameShortColored} ({intDef.label}).";
            }

            shortTermEvents.Add(stEvent);
        }

        public void LogGlobalEvent(string category, string description, string factionName = null, string settlementName = null)
        {
            var pastEvent = new PastEvent
            {
                gameTick = Find.TickManager.TicksGame,
                date = SynapseDate.Now(),
                category = category,
                eventDescription = description,
                factionName = factionName,
                settlementName = settlementName
            };
            EnqueuePastEvent(pastEvent);
        }

        public void EnqueuePastEvent(PastEvent pastEvent)
        {
            if (Find.AnyPlayerHomeMap != null)
            {
                Map map = Find.AnyPlayerHomeMap;
                
                // Take a quick snapshot of the colony status
                float nutrition = map.resourceCounter.TotalHumanEdibleNutrition;
                pastEvent.colonySnapshot = $"Wealth: {map.wealthWatcher.WealthTotal:F0}, Nutrition Available: {nutrition:F0}";

                // Take snapshots of all free colonists
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn.needs != null && pawn.needs.mood != null && pawn.needs.food != null && pawn.needs.rest != null)
                    {
                        string moodStr = pawn.needs.mood.CurLevelPercentage.ToStringPercent();
                        string foodStr = pawn.needs.food.CurLevelPercentage.ToStringPercent();
                        string restStr = pawn.needs.rest.CurLevelPercentage.ToStringPercent();
                        pastEvent.pawnSnapshots[pawn.ThingID] = $"Mood: {moodStr}, Food: {foodStr}, Rest: {restStr}";
                    }
                }
            }
            
            _backlogQueue.Enqueue(pastEvent);
        }

        public bool TryDequeuePastEvent(out PastEvent pastEvent)
        {
            if (_backlogQueue.Count > 0)
            {
                pastEvent = _backlogQueue.Dequeue();
                return true;
            }
            
            pastEvent = null;
            return false;
        }

        public int BacklogCount => _backlogQueue.Count;

        public IEnumerable<PastEvent> GetRecentEvents(int count)
        {
            return System.Linq.Enumerable.Skip(_backlogQueue, System.Math.Max(0, _backlogQueue.Count - count));
        }

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

            float combatCompetence = 0f;
            int freeColonists = 0;

            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Downed || pawn.Dead) continue;
                
                freeColonists++;
                
                // Add points for combat skills
                combatCompetence += (pawn.skills?.GetSkill(RimWorld.SkillDefOf.Shooting)?.Level ?? 0) * 5f;
                combatCompetence += (pawn.skills?.GetSkill(RimWorld.SkillDefOf.Melee)?.Level ?? 0) * 5f;

                // Add points for equipped weapons
                if (pawn.equipment?.Primary != null)
                {
                    combatCompetence += pawn.equipment.Primary.MarketValue / 10f;
                }
                
                // Add points for apparel (armor)
                if (pawn.apparel != null)
                {
                    foreach (var app in pawn.apparel.WornApparel)
                    {
                        combatCompetence += app.MarketValue / 20f;
                    }
                }
            }

            // Also factor in installed security structures (turrets)
            float securityPower = 0f;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (t.def.building != null && t.def.building.IsTurret)
                {
                    securityPower += t.MarketValue / 5f;
                }
            }

            float baseColonistPoints = freeColonists * 35f;
            float actualThreat = (baseColonistPoints + combatCompetence + securityPower) * TensionModifier;

            return UnityEngine.Mathf.Clamp(actualThreat, 35f, 10000f);
        }
    }
}
