using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Models;

namespace RimSynapse.Comps
{
    public class SynapseCorePawnComp : ThingComp
    {
        public List<WeightedMemory> memories = new List<WeightedMemory>();
        
        // Runtime Hashtable Indexes for instant lookup
        private Dictionary<string, List<WeightedMemory>> memoriesByTag = new Dictionary<string, List<WeightedMemory>>();
        private Dictionary<string, List<WeightedMemory>> memoriesByPawnId = new Dictionary<string, List<WeightedMemory>>();
        public List<OpinionSample> opinionHistory = new List<OpinionSample>();
        public string personalitySummary;
        public string dynamicBackstory;
        public string clinicalAssessment;
        public string hometown;
        public List<string> llmTraits = new List<string>();
        
        // Active AI-driven modifiers shared across mods
        public Dictionary<string, float> thoughtSensitivities = new Dictionary<string, float>();
        public Dictionary<string, float> relationSensitivities = new Dictionary<string, float>();

        private const int TickIntervalDay = 60000;
        private const int TickInterval6Hours = 15000;
        
        private int lastDecayTick = -1;
        private int lastOpinionTick = -1;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref memories, "synapseMemories", LookMode.Deep);
            Scribe_Collections.Look(ref opinionHistory, "synapseOpinionHistory", LookMode.Deep);
            Scribe_Values.Look(ref personalitySummary, "synapsePersonality");
            Scribe_Values.Look(ref dynamicBackstory, "dynamicBackstory");
            Scribe_Values.Look(ref clinicalAssessment, "clinicalAssessment");
            Scribe_Values.Look(ref hometown, "hometown");
            Scribe_Collections.Look(ref llmTraits, "llmTraits", LookMode.Value);
            
            Scribe_Collections.Look(ref thoughtSensitivities, "thoughtSensitivities", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref relationSensitivities, "relationSensitivities", LookMode.Value, LookMode.Value);
            
            Scribe_Values.Look(ref lastDecayTick, "lastDecayTick", -1);
            Scribe_Values.Look(ref lastOpinionTick, "lastOpinionTick", -1);
            
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (memories == null) memories = new List<WeightedMemory>();
                if (opinionHistory == null) opinionHistory = new List<OpinionSample>();
                if (llmTraits == null) llmTraits = new List<string>();
                if (thoughtSensitivities == null) thoughtSensitivities = new Dictionary<string, float>();
                if (relationSensitivities == null) relationSensitivities = new Dictionary<string, float>();
            }
            
            // Migrate old gameTick-only memories to absTick
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                foreach (var memory in memories)
                {
                    memory.MigrateTickIfNeeded();
                }
                RebuildMemoryIndexes();
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            
            if (parent is Pawn pawn && pawn.Spawned && !pawn.Dead)
            {
                int currentTick = Find.TickManager.TicksGame;

                // Memory decay once per day
                if (lastDecayTick == -1)
                {
                    lastDecayTick = currentTick;
                }
                else if (currentTick - lastDecayTick >= TickIntervalDay)
                {
                    lastDecayTick = currentTick;
                    DoMemoryDecay();
                }

                // Sample opinions periodically (e.g. every 6 in-game hours)
                if (lastOpinionTick == -1)
                {
                    lastOpinionTick = currentTick;
                }
                else if (currentTick - lastOpinionTick >= TickInterval6Hours)
                {
                    lastOpinionTick = currentTick;
                    SampleOpinions(pawn);
                }
            }
        }

        private void DoMemoryDecay()
        {
            for (int i = memories.Count - 1; i >= 0; i--)
            {
                var mem = memories[i];
                if (mem.isLongTerm) continue; // Long term memories never decay

                mem.weight -= mem.decayRate;
                
                if (mem.weight <= 0f)
                {
                    RemoveMemoryAt(i);
                }
            }
        }

        private void SampleOpinions(Pawn pawn)
        {
            if (pawn.relations == null || pawn.Map == null) return;

            var colonists = pawn.Map.mapPawns?.FreeColonists;
            if (colonists != null)
            {
                int currentTick = GenTicks.TicksGame;
                
                foreach (var other in colonists)
                {
                    if (other == pawn) continue;

                    int opinion = pawn.relations.OpinionOf(other);
                    
                    opinionHistory.Add(new OpinionSample
                    {
                        targetPawnId = other.ThingID,
                        opinion = opinion,
                        gameTick = currentTick
                    });
                }
                
                TrimOpinionHistory();
            }
        }

        private void TrimOpinionHistory()
        {
            // Group by targetPawnId and keep only the latest 20 samples per pawn
            var grouped = opinionHistory.GroupBy(o => o.targetPawnId);
            var newHistory = new List<OpinionSample>();
            
            foreach (var group in grouped)
            {
                var recentSamples = group.OrderByDescending(o => o.gameTick).Take(20);
                newHistory.AddRange(recentSamples);
            }
            
            opinionHistory = newHistory;
        }

        /// <summary>
        /// Aggregates all memory weights by their tags and returns the top N burdens.
        /// This creates a summarized 'Sensitivity' profile for the LLM without sending every raw memory.
        /// </summary>
        public string GetTopMemoryBurdens(int topN = 5, float minWeightThreshold = 0f, string optionalTagFilter = null, string optionalPawnIdFilter = null)
        {
            if (memories == null || memories.Count == 0) return "None";

            var sourceList = memories;
            if (!string.IsNullOrEmpty(optionalTagFilter))
            {
                sourceList = GetMemoriesByTag(optionalTagFilter);
            }
            else if (!string.IsNullOrEmpty(optionalPawnIdFilter))
            {
                sourceList = GetMemoriesByPawnId(optionalPawnIdFilter);
            }

            if (sourceList.Count == 0) return "None";

            var tagWeights = new Dictionary<string, float>();
            var tagCounts = new Dictionary<string, int>();

            foreach (var memory in sourceList)
            {
                if (memory.tags == null) continue;
                foreach (string tag in memory.tags)
                {
                    if (!tagWeights.ContainsKey(tag))
                    {
                        tagWeights[tag] = 0f;
                        tagCounts[tag] = 0;
                    }
                    tagWeights[tag] += memory.weight;
                    tagCounts[tag]++;
                }
            }

            var sorted = tagWeights
                .Where(kvp => kvp.Value >= minWeightThreshold)
                .OrderByDescending(kvp => kvp.Value)
                .Take(topN)
                .ToList();

            if (sorted.Count == 0) return "None";

            return string.Join(", ", sorted.Select(kvp => $"{kvp.Key} ({kvp.Value:F1})"));
        }

        // ────────────────────────────────────────────────────────
        //  Hashtable Memory Logic
        // ────────────────────────────────────────────────────────

        public void AddMemory(WeightedMemory memory)
        {
            memories.Add(memory);
            IndexMemory(memory);
        }

        private void RemoveMemoryAt(int index)
        {
            var memory = memories[index];
            memories.RemoveAt(index);
            UnindexMemory(memory);
        }

        private void RebuildMemoryIndexes()
        {
            memoriesByTag.Clear();
            memoriesByPawnId.Clear();
            foreach (var mem in memories)
            {
                IndexMemory(mem);
            }
        }

        private void IndexMemory(WeightedMemory memory)
        {
            if (memory.tags != null)
            {
                foreach (var tag in memory.tags)
                {
                    if (!memoriesByTag.TryGetValue(tag, out var list))
                    {
                        list = new List<WeightedMemory>();
                        memoriesByTag[tag] = list;
                    }
                    list.Add(memory);
                }
            }

            if (memory.subjectPawnIds != null)
            {
                foreach (var pawnId in memory.subjectPawnIds)
                {
                    if (!memoriesByPawnId.TryGetValue(pawnId, out var list))
                    {
                        list = new List<WeightedMemory>();
                        memoriesByPawnId[pawnId] = list;
                    }
                    list.Add(memory);
                }
            }
        }

        private void UnindexMemory(WeightedMemory memory)
        {
            if (memory.tags != null)
            {
                foreach (var tag in memory.tags)
                {
                    if (memoriesByTag.TryGetValue(tag, out var list))
                    {
                        list.Remove(memory);
                    }
                }
            }

            if (memory.subjectPawnIds != null)
            {
                foreach (var pawnId in memory.subjectPawnIds)
                {
                    if (memoriesByPawnId.TryGetValue(pawnId, out var list))
                    {
                        list.Remove(memory);
                    }
                }
            }
        }

        public List<WeightedMemory> GetMemoriesByTag(string tag)
        {
            if (memoriesByTag.TryGetValue(tag, out var list))
            {
                return list;
            }
            return new List<WeightedMemory>();
        }

        public List<WeightedMemory> GetMemoriesByPawnId(string pawnId)
        {
            if (memoriesByPawnId.TryGetValue(pawnId, out var list))
            {
                return list;
            }
            return new List<WeightedMemory>();
        }
    }
}
