namespace RimSynapse
{
    /// <summary>
    /// Configures how pawn thoughts are filtered for context assembly.
    /// Loaded from XML in Defs/SynapseConfig/. Players can adjust
    /// thresholds without C# by editing the XML directly.
    ///
    /// Filter logic: include a thought if
    ///   |moodOffset| >= moodImpactThreshold  (significant impact)
    ///   OR age &lt; recencyHours               (recent event)
    /// </summary>
    public class SynapseThoughtFilterDef : Verse.Def
    {
        /// <summary>
        /// Minimum absolute mood impact to include a thought.
        /// Default: 20 (only significant mood swings).
        /// Lower values include more thoughts but consume more tokens.
        /// </summary>
        public float moodImpactThreshold = 20f;

        /// <summary>
        /// Include thoughts newer than this many in-game hours,
        /// regardless of mood impact. Captures recent events like
        /// "just ate" or "saw a corpse" even if mood impact is small.
        /// Default: 12 hours.
        /// </summary>
        public float recencyHours = 12f;

        /// <summary>
        /// Exclude thoughts that have passed this fraction of their
        /// total duration (about to expire). Range: 0.0 to 1.0.
        /// Default: 0.90 (exclude if 90%+ expired).
        /// </summary>
        public float expirationCutoffPercent = 0.90f;

        /// <summary>
        /// If true, stacking thoughts (same def appearing multiple times)
        /// are deduplicated — only the first instance is included.
        /// Default: true.
        /// </summary>
        public bool deduplicateStacks = true;

        /// <summary>
        /// If true, include situational thoughts (non-memory, always-active
        /// thoughts like "Comfortable temperature" or "Sharing bedroom").
        /// Only included if they pass the mood impact threshold.
        /// Default: true.
        /// </summary>
        public bool includeSituational = true;
    }
}
