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
        
        public List<PastEvent> backlogQueueList = new List<PastEvent>();
        private Queue<PastEvent> _backlogQueue = new Queue<PastEvent>();

        public List<KnowledgePacket> inTransitKnowledge = new List<KnowledgePacket>();

        public SynapseCoreWorldComponent(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Collections.Look(ref narrativeThreads, "narrativeThreads", LookMode.Deep);
            Scribe_Collections.Look(ref factionTrackers, "factionTrackers", LookMode.Deep);
            Scribe_Collections.Look(ref backlogQueueList, "backlogQueueList", LookMode.Deep);
            Scribe_Collections.Look(ref inTransitKnowledge, "inTransitKnowledge", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                backlogQueueList.Clear();
                backlogQueueList.AddRange(_backlogQueue);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (narrativeThreads == null) narrativeThreads = new List<NarrativeThread>();
                if (factionTrackers == null) factionTrackers = new List<FactionRelationshipTracker>();
                if (backlogQueueList == null) backlogQueueList = new List<PastEvent>();
                if (inTransitKnowledge == null) inTransitKnowledge = new List<KnowledgePacket>();
                
                _backlogQueue.Clear();
                foreach (var pastEvent in backlogQueueList)
                {
                    _backlogQueue.Enqueue(pastEvent);
                }
            }
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

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            if (Find.TickManager.TicksGame % 1000 == 0 && inTransitKnowledge.Count > 0)
            {
                int currentTick = Find.TickManager.TicksGame;
                for (int i = inTransitKnowledge.Count - 1; i >= 0; i--)
                {
                    var packet = inTransitKnowledge[i];
                    if (currentTick >= packet.arrivalTick)
                    {
                        ProcessArrivingKnowledge(packet);
                        inTransitKnowledge.RemoveAt(i);
                    }
                }
            }
        }

        private void ProcessArrivingKnowledge(KnowledgePacket packet)
        {
            var tracker = factionTrackers.Find(f => f.factionId == packet.targetFactionId);
            if (tracker == null)
            {
                tracker = new FactionRelationshipTracker { factionId = packet.targetFactionId };
                factionTrackers.Add(tracker);
            }

            tracker.perceivedWealth = UnityEngine.Mathf.Lerp(tracker.perceivedWealth, packet.payloadWealth, 0.2f);
            tracker.perceivedStrength = UnityEngine.Mathf.Lerp(tracker.perceivedStrength, packet.payloadStrength, 0.2f);
        }

        public void BroadcastKnowledge(Faction originFaction, float actualWealth, float actualStrength)
        {
            if (originFaction == null || originFaction.IsPlayer) return;

            foreach (Faction targetFaction in Find.FactionManager.AllFactionsVisible)
            {
                if (targetFaction == originFaction || targetFaction.IsPlayer || targetFaction.Hidden) continue;

                float relation = originFaction.GoodwillWith(targetFaction);
                float knowledgeTransferFactor = UnityEngine.Mathf.Clamp01((relation + 100f) / 200f * 0.9f + 0.1f);

                float payloadWealth = actualWealth * knowledgeTransferFactor;
                float payloadStrength = actualStrength * knowledgeTransferFactor;

                int distance = 50;
                if (originFaction.def.settlementGenerationWeight > 0 && targetFaction.def.settlementGenerationWeight > 0)
                {
                    var originBase = Find.WorldObjects.Settlements.Find(s => s.Faction == originFaction);
                    var targetBase = Find.WorldObjects.Settlements.Find(s => s.Faction == targetFaction);
                    if (originBase != null && targetBase != null)
                    {
                        distance = Find.WorldGrid.TraversalDistanceBetween(originBase.Tile, targetBase.Tile, true, 100);
                        if (distance > 100 || distance < 0) distance = 100;
                    }
                }

                int delayTicks = distance * 1000;

                inTransitKnowledge.Add(new KnowledgePacket
                {
                    sourceFactionId = originFaction.GetUniqueLoadID(),
                    targetFactionId = targetFaction.GetUniqueLoadID(),
                    payloadWealth = payloadWealth,
                    payloadStrength = payloadStrength,
                    arrivalTick = Find.TickManager.TicksGame + delayTicks
                });
            }

            var originTracker = factionTrackers.Find(f => f.factionId == originFaction.GetUniqueLoadID());
            if (originTracker == null)
            {
                originTracker = new FactionRelationshipTracker { factionId = originFaction.GetUniqueLoadID() };
                factionTrackers.Add(originTracker);
            }
            originTracker.perceivedWealth = actualWealth;
            originTracker.perceivedStrength = actualStrength;
        }
    }
}
