using System.Collections.Generic;
using Verse;

namespace RimSynapse
{
    /// <summary>
    /// Structured pawn data for context injection.
    /// Tiers 1–2 are assembled from vanilla RimWorld APIs.
    /// Tier 5 fields are populated from companion mod comps (null-safe).
    /// </summary>
    public class PawnPacket : IExposable
    {
        // ── Tier 1: Identity ──
        public string pawnId;
        public string name;
        public string gender;
        public int age;
        public string backstoryChildhood;
        public string backstoryAdulthood;
        public List<string> traits;

        // ── Tier 2: State ──
        public float moodLevel;
        public List<ThoughtEntry> thoughts;
        public Dictionary<string, int> skills;
        public List<string> passions;
        public List<string> healthConditions;
        public List<string> equipment;
        public string currentJob;

        // ── Tier 2: Social ──
        public List<RelationshipEntry> relationships;
        public List<OpinionEntry> opinions;

        // ── Tier 2: DLC (nullable) ──
        public string ideology;
        public List<string> precepts;
        public string royaltyTitle;

        // ── Tier 5: From Psychology (null if mod absent) ──
        public List<MemoryEntry> memories;
        public string personalitySummary;
        public float? opinionIntegral;

        // ── Extrapolated Metrics ──
        public float hungerRatePerDay;
        public float productivityScore;
        public float netEconomicValue;

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref gender, "gender");
            Scribe_Values.Look(ref age, "age");
            Scribe_Values.Look(ref backstoryChildhood, "backstoryChildhood");
            Scribe_Values.Look(ref backstoryAdulthood, "backstoryAdulthood");
            Scribe_Collections.Look(ref traits, "traits", LookMode.Value);
            Scribe_Values.Look(ref moodLevel, "moodLevel");
            Scribe_Collections.Look(ref thoughts, "thoughts", LookMode.Deep);
            Scribe_Collections.Look(ref healthConditions, "healthConditions", LookMode.Value);
            Scribe_Collections.Look(ref equipment, "equipment", LookMode.Value);
            Scribe_Values.Look(ref currentJob, "currentJob");
            Scribe_Collections.Look(ref relationships, "relationships", LookMode.Deep);
            Scribe_Collections.Look(ref opinions, "opinions", LookMode.Deep);
            Scribe_Values.Look(ref ideology, "ideology");
            Scribe_Collections.Look(ref precepts, "precepts", LookMode.Value);
            Scribe_Values.Look(ref royaltyTitle, "royaltyTitle");
            Scribe_Collections.Look(ref memories, "memories", LookMode.Deep);
            Scribe_Values.Look(ref personalitySummary, "personalitySummary");
            // Note: skills and passions use Dictionary/List which need special handling
            // opinionIntegral is a nullable float — Scribe doesn't directly support nullable
        }
    }
}
