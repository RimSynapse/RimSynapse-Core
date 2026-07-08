using System.Collections.Generic;
using Verse;

namespace RimSynapse
{
    // ───────────────────────────────────────────────────────────────
    //  Context Packet — the top-level data structure assembled by Core
    //  and injected into LM Studio requests.
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Complete context payload assembled from game state.
    /// Serialized to text and injected as part of the system message.
    /// Optionally persisted across saves via SynapseContextWorldComponent.
    /// </summary>
    public class ContextPacket : IExposable
    {
        /// <summary>Event type that triggered this context assembly.</summary>
        public string eventType;

        /// <summary>Free-form framing text (used by "custom" event type).</summary>
        public string framing;

        /// <summary>Game tick when this packet was assembled.</summary>
        public int gameTick;

        /// <summary>Primary pawn data (may be null for colony-level events).</summary>
        public PawnPacket sourcePawn;

        /// <summary>Secondary pawn data (optional, for two-pawn interactions).</summary>
        public PawnPacket targetPawn;

        /// <summary>Colony-wide state.</summary>
        public ColonyPacket colony;

        /// <summary>World and faction data.</summary>
        public WorldPacket world;

        /// <summary>Active narrative threads from Storyteller (Tier 5, null if absent).</summary>
        public List<NarrativeThreadEntry> narrativeThreads;

        /// <summary>Assembly metadata: which slots were filled, which were dropped.</summary>
        public List<string> slotsFilled;
        public List<string> slotsDropped;
        public int estimatedTokens;

        public void ExposeData()
        {
            Scribe_Values.Look(ref eventType, "eventType");
            Scribe_Values.Look(ref framing, "framing");
            Scribe_Values.Look(ref gameTick, "gameTick");
            Scribe_Values.Look(ref estimatedTokens, "estimatedTokens");
            Scribe_Deep.Look(ref sourcePawn, "sourcePawn");
            Scribe_Deep.Look(ref targetPawn, "targetPawn");
            Scribe_Deep.Look(ref colony, "colony");
            Scribe_Deep.Look(ref world, "world");
            Scribe_Collections.Look(ref narrativeThreads, "narrativeThreads", LookMode.Deep);
            Scribe_Collections.Look(ref slotsFilled, "slotsFilled", LookMode.Value);
            Scribe_Collections.Look(ref slotsDropped, "slotsDropped", LookMode.Value);
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  Pawn Packet — per-pawn data assembled from vanilla + companions
    // ───────────────────────────────────────────────────────────────

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

    // ───────────────────────────────────────────────────────────────
    //  Sub-entries for pawn data
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A filtered pawn thought for context injection.
    /// </summary>
    public class ThoughtEntry : IExposable
    {
        public string label;
        public float moodOffset;
        public float ageHours;
        public bool isRecent;

        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref moodOffset, "moodOffset");
            Scribe_Values.Look(ref ageHours, "ageHours");
            Scribe_Values.Look(ref isRecent, "isRecent");
        }
    }

    /// <summary>
    /// A direct social relationship (Spouse, Rival, Friend, etc.).
    /// </summary>
    public class RelationshipEntry : IExposable
    {
        public string relationLabel;
        public string otherPawnName;
        public string otherPawnId;

        public void ExposeData()
        {
            Scribe_Values.Look(ref relationLabel, "relationLabel");
            Scribe_Values.Look(ref otherPawnName, "otherPawnName");
            Scribe_Values.Look(ref otherPawnId, "otherPawnId");
        }
    }

    /// <summary>
    /// A pawn's opinion score toward another pawn.
    /// </summary>
    public class OpinionEntry : IExposable
    {
        public string pawnName;
        public int opinion;

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Values.Look(ref opinion, "opinion");
        }
    }

    /// <summary>
    /// A weighted memory from Psychology mod (Tier 5).
    /// Mirrors the WeightedMemory structure for context serialization.
    /// </summary>
    public class MemoryEntry : IExposable
    {
        public string summary;
        public string memoryType;
        public List<string> tags;
        public float weight;

        public void ExposeData()
        {
            Scribe_Values.Look(ref summary, "summary");
            Scribe_Values.Look(ref memoryType, "memoryType");
            Scribe_Collections.Look(ref tags, "tags", LookMode.Value);
            Scribe_Values.Look(ref weight, "weight");
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  Colony Packet — Tier 3
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Colony-wide state data for context injection.
    /// </summary>
    public class ColonyPacket : IExposable
    {
        public int colonistCount;
        public float wealthTotal;
        public string season;
        public string weather;
        public string biome;
        public string dangerLevel;
        public string currentResearch;
        public float researchProgress;
        public List<string> recentEvents;

        public void ExposeData()
        {
            Scribe_Values.Look(ref colonistCount, "colonistCount");
            Scribe_Values.Look(ref wealthTotal, "wealthTotal");
            Scribe_Values.Look(ref season, "season");
            Scribe_Values.Look(ref weather, "weather");
            Scribe_Values.Look(ref biome, "biome");
            Scribe_Values.Look(ref dangerLevel, "dangerLevel");
            Scribe_Values.Look(ref currentResearch, "currentResearch");
            Scribe_Values.Look(ref researchProgress, "researchProgress");
            Scribe_Collections.Look(ref recentEvents, "recentEvents", LookMode.Value);
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  World Packet — Tier 4
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// World and faction data for context injection.
    /// Core reads raw vanilla faction data. Enriched fields
    /// (integral, trajectory, power) are populated by Storyteller if loaded.
    /// </summary>
    public class WorldPacket : IExposable
    {
        public List<FactionEntry> factions;
        public List<string> activeQuestNames;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref factions, "factions", LookMode.Deep);
            Scribe_Collections.Look(ref activeQuestNames, "activeQuestNames", LookMode.Value);
        }
    }

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

    /// <summary>
    /// A narrative thread entry for context serialization (Tier 5).
    /// Mirrors Storyteller's NarrativeThread for context injection.
    /// </summary>
    public class NarrativeThreadEntry : IExposable
    {
        public string keyword;
        public string category;
        public string description;
        public float weight;
        public bool isResolved;

        public void ExposeData()
        {
            Scribe_Values.Look(ref keyword, "keyword");
            Scribe_Values.Look(ref category, "category");
            Scribe_Values.Look(ref description, "description");
            Scribe_Values.Look(ref weight, "weight");
            Scribe_Values.Look(ref isResolved, "isResolved");
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  Context Settings — runtime configuration
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Runtime context assembly settings. Primarily read from
    /// SynapseContextProfileDef and SynapseThoughtFilterDef XML,
    /// but can be overridden per-request via ChatOptions.
    /// </summary>
    public class ContextSettings : IExposable
    {
        // Section toggles (derived from profile tier selection)
        public bool includeBackstory = true;
        public bool includeTraits = true;
        public bool includeMood = true;
        public bool includeSkills = true;
        public bool includeHealth = true;
        public bool includeRelationships = true;
        public bool includeOpinions = true;
        public bool includeEquipment = false;
        public bool includeIdeology = true;
        public bool includeColony = true;
        public bool includeFactions = false;
        public bool includeQuests = false;
        public bool includeMemories = true;
        public bool includeThreads = true;

        // Limits
        public int memoryLimit = 5;
        public int opinionLimit = 5;
        public int eventLimit = 5;
        public float weightThreshold = 0.15f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref includeBackstory, "includeBackstory", true);
            Scribe_Values.Look(ref includeTraits, "includeTraits", true);
            Scribe_Values.Look(ref includeMood, "includeMood", true);
            Scribe_Values.Look(ref includeSkills, "includeSkills", true);
            Scribe_Values.Look(ref includeHealth, "includeHealth", true);
            Scribe_Values.Look(ref includeRelationships, "includeRelationships", true);
            Scribe_Values.Look(ref includeOpinions, "includeOpinions", true);
            Scribe_Values.Look(ref includeEquipment, "includeEquipment", false);
            Scribe_Values.Look(ref includeIdeology, "includeIdeology", true);
            Scribe_Values.Look(ref includeColony, "includeColony", true);
            Scribe_Values.Look(ref includeFactions, "includeFactions", false);
            Scribe_Values.Look(ref includeQuests, "includeQuests", false);
            Scribe_Values.Look(ref includeMemories, "includeMemories", true);
            Scribe_Values.Look(ref includeThreads, "includeThreads", true);
            Scribe_Values.Look(ref memoryLimit, "memoryLimit", 5);
            Scribe_Values.Look(ref opinionLimit, "opinionLimit", 5);
            Scribe_Values.Look(ref eventLimit, "eventLimit", 5);
            Scribe_Values.Look(ref weightThreshold, "weightThreshold", 0.15f);
        }
    }
}
