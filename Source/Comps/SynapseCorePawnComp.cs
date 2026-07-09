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
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            
            if (parent is Pawn pawn && pawn.Spawned && !pawn.Dead)
            {
                // Memory decay once per day
                if (pawn.IsHashIntervalTick(TickIntervalDay))
                {
                    DoMemoryDecay();
                }

                // Sample opinions periodically (e.g. every 6 in-game hours)
                if (pawn.IsHashIntervalTick(TickInterval6Hours))
                {
                    SampleOpinions(pawn);
                }
            }
        }

        private void DoMemoryDecay()
        {
            for (int i = memories.Count - 1; i >= 0; i--)
            {
                var mem = memories[i];
                mem.weight -= mem.decayRate;
                
                if (mem.weight <= 0f)
                {
                    memories.RemoveAt(i);
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
        public string GetTopMemoryBurdens(int topN = 5, float minWeightThreshold = 0f)
        {
            if (memories == null || memories.Count == 0) return "None";

            var tagWeights = new Dictionary<string, float>();
            var tagCounts = new Dictionary<string, int>();

            foreach (var memory in memories)
            {
                if (memory.tags == null) continue;
                foreach (string tag in memory.tags)
                {
                    string normalizedTag = tag.Trim().ToLower();
                    if (!tagWeights.ContainsKey(normalizedTag))
                    {
                        tagWeights[normalizedTag] = 0f;
                        tagCounts[normalizedTag] = 0;
                    }
                    tagWeights[normalizedTag] += memory.weight;
                    tagCounts[normalizedTag]++;
                }
            }

            var topTags = tagWeights.Where(kv => kv.Value >= minWeightThreshold).OrderByDescending(kv => kv.Value).Take(topN).ToList();
            
            if (topTags.Count == 0) return "None";

            List<string> burdenStrings = new List<string>();
            foreach (var kvp in topTags)
            {
                burdenStrings.Add($"[{kvp.Key}]: Weight {kvp.Value:F1} ({tagCounts[kvp.Key]} related instances)");
            }

            return string.Join(" | ", burdenStrings);
        }
    }
}
