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

        public SynapseCoreWorldComponent(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Collections.Look(ref narrativeThreads, "narrativeThreads", LookMode.Deep);
            Scribe_Collections.Look(ref factionTrackers, "factionTrackers", LookMode.Deep);
            Scribe_Collections.Look(ref backlogQueueList, "backlogQueueList", LookMode.Deep);

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

        public IEnumerable<PastEvent> GetRecentEvents(int count)
        {
            return System.Linq.Enumerable.Skip(_backlogQueue, System.Math.Max(0, _backlogQueue.Count - count));
        }
    }
}
