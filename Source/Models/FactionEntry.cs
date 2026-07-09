using Verse;

namespace RimSynapse
{
    /// <summary>
    /// Per-faction data entry for context injection.
    /// </summary>
    public class FactionEntry : IExposable
    {
        /// <summary>Faction display name.</summary>
        public string factionName;

        /// <summary>Faction type (e.g., "Pirate", "Outlander Civil", "Empire").</summary>
        public string factionType;

        /// <summary>Player relation: "Hostile", "Neutral", or "Ally".</summary>
        public string relationKind;

        /// <summary>Raw vanilla goodwill (-100 to +100).</summary>
        public int goodwill;

        /// <summary>Faction leader name (null if unknown).</summary>
        public string leaderName;

        // ── Enriched by Storyteller (null if Storyteller not loaded) ──

        /// <summary>Goodwill integral from Storyteller's tracking.</summary>
        public float? goodwillIntegral;

        /// <summary>Trajectory: "improving", "stable", "deteriorating".</summary>
        public string trajectory;

        /// <summary>Faction power estimate: "weak", "moderate", "strong", "dominant".</summary>
        public string power;

        /// <summary>Strategic opportunity: "high", "moderate", "low", "none".</summary>
        public string opportunityLevel;

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref factionType, "factionType");
            Scribe_Values.Look(ref relationKind, "relationKind");
            Scribe_Values.Look(ref goodwill, "goodwill");
            Scribe_Values.Look(ref leaderName, "leaderName");
            Scribe_Values.Look(ref trajectory, "trajectory");
            Scribe_Values.Look(ref power, "power");
            Scribe_Values.Look(ref opportunityLevel, "opportunityLevel");
            // Note: goodwillIntegral is nullable — handle via manual Scribe
        }
    }
}
