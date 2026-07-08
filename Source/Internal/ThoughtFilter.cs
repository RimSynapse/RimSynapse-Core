using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Filters pawn thoughts for context assembly using rules defined
    /// in SynapseThoughtFilterDef XML. Applies threshold + recency filtering.
    ///
    /// A thought is included if:
    ///   |moodOffset| >= moodImpactThreshold   (significant impact)
    ///   OR age &lt; recencyHours               (recent event)
    /// </summary>
    internal static class ThoughtFilter
    {
        /// <summary>
        /// Filter a pawn's active thoughts based on the default SynapseThoughtFilterDef.
        /// Returns a list of ThoughtEntry objects suitable for context injection.
        /// </summary>
        internal static List<ThoughtEntry> FilterThoughts(Pawn pawn)
        {
            if (pawn?.needs?.mood?.thoughts == null)
                return new List<ThoughtEntry>();

            // Load filter settings from XML Def
            var filterDef = DefDatabase<SynapseThoughtFilterDef>.GetNamed(
                "SynapseThoughtFilter_Default", errorOnFail: false);

            float impactThreshold = filterDef?.moodImpactThreshold ?? 20f;
            float recencyHrs = filterDef?.recencyHours ?? 12f;
            float expirationCutoff = filterDef?.expirationCutoffPercent ?? 0.90f;
            bool dedupe = filterDef?.deduplicateStacks ?? true;
            bool includeSituational = filterDef?.includeSituational ?? true;

            var entries = new List<ThoughtEntry>();
            var seen = new HashSet<string>();

            // ── Memory-based thoughts ──
            try
            {
                var memories = pawn.needs.mood.thoughts.memories;
                if (memories?.Memories != null)
                {
                    foreach (var memory in memories.Memories)
                    {
                        if (memory?.def == null) continue;

                        float offset = memory.MoodOffset();
                        int ageTicks = memory.age;
                        float ageHours = ageTicks / 2500f;

                        // Skip if about to expire
                        float durationDays = memory.def.durationDays;
                        if (durationDays > 0)
                        {
                            float durationHours = durationDays * 24f;
                            if (ageHours > durationHours * expirationCutoff)
                                continue;
                        }

                        bool significantImpact = Math.Abs(offset) >= impactThreshold;
                        bool recentEvent = ageHours < recencyHrs;

                        if (!significantImpact && !recentEvent)
                            continue;

                        // Deduplicate stacking thoughts
                        if (dedupe)
                        {
                            string key = memory.def.defName;
                            if (seen.Contains(key)) continue;
                            seen.Add(key);
                        }

                        entries.Add(new ThoughtEntry
                        {
                            label = memory.LabelCap,
                            moodOffset = offset,
                            ageHours = ageHours,
                            isRecent = recentEvent,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                SynapseLog.Warn("context",
                    $"Error filtering memory thoughts for {pawn.Name}: {ex.Message}");
            }

            // ── Situational thoughts ──
            // TODO: RimWorld 1.6 SituationalThoughtHandler doesn't expose a clean
            // public API for enumerating individual situational thoughts.
            // Memory-based thoughts (above) capture the primary data.
            // Situational thought support will be added when the correct API
            // surface is determined.

            // Sort by absolute impact descending
            entries.Sort((a, b) =>
                Math.Abs(b.moodOffset).CompareTo(Math.Abs(a.moodOffset)));

            return entries;
        }
    }
}
