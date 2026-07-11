using System;

namespace RimSynapse
{
    /// <summary>
    /// Flags enum for selecting which context tiers to include.
    /// Used by companion mods to request specific data categories
    /// without specifying individual slots.
    ///
    /// Tiers:
    ///   Identity  — Tier 1: name, backstory, traits (~45 tokens)
    ///   PawnState — Tier 2: mood, skills, health, social (~250-400 tokens)
    ///   Colony    — Tier 3: colony stats, weather, threats (~60-100 tokens)
    ///   World     — Tier 4: factions, quests (~80-200 tokens)
    ///   Synthetic — Tier 5: AI memories, personality, threads (0 if absent)
    /// </summary>
    [Flags]
    public enum ContextTierMask
    {
        /// <summary>No context tiers included.</summary>
        None = 0,

        /// <summary>Tier 1: Pawn name, gender, age, backstory, traits.</summary>
        Identity = 1 << 0,

        /// <summary>Tier 2: Mood, thoughts, skills, health, relationships, equipment, DLC data.</summary>
        PawnState = 1 << 1,

        /// <summary>Tier 3: Colony wealth, population, weather, danger, research, recent events.</summary>
        Colony = 1 << 2,

        /// <summary>Tier 4: Faction list, goodwill, leaders, active quests.</summary>
        World = 1 << 3,

        /// <summary>Tier 5: Weighted memories, AI personality, narrative threads (from companion mods).</summary>
        Synthetic = 1 << 4,

        /// <summary>Tier 6: Short-Term History (recent social interactions and global events).</summary>
        ShortTermHistory = 1 << 5,

        // ── Preset Combinations ──

        /// <summary>Identity + PawnState + Synthetic + ShortTermHistory. Default for dialogue and chat.</summary>
        Standard = Identity | PawnState | Synthetic | ShortTermHistory,

        /// <summary>All six tiers included.</summary>
        Full = Identity | PawnState | Colony | World | Synthetic | ShortTermHistory,

        /// <summary>Identity + Colony + World. For colony-level events.</summary>
        ColonyEvent = Identity | Colony | World,

        /// <summary>Identity only. For quick lightweight queries.</summary>
        Lightweight = Identity,
    }

    /// <summary>
    /// Helper methods for ContextTierMask parsing and resolution.
    /// </summary>
    public static class ContextTierMaskHelper
    {
        /// <summary>
        /// Parse a tier name string (from XML) to the corresponding flag.
        /// Returns None if the string is unrecognized.
        /// </summary>
        public static ContextTierMask ParseTier(string tierName)
        {
            if (string.IsNullOrEmpty(tierName))
                return ContextTierMask.None;

            return tierName.Trim() switch
            {
                "Identity" => ContextTierMask.Identity,
                "PawnState" => ContextTierMask.PawnState,
                "Colony" => ContextTierMask.Colony,
                "World" => ContextTierMask.World,
                "Synthetic" => ContextTierMask.Synthetic,
                "ShortTermHistory" => ContextTierMask.ShortTermHistory,
                _ => ContextTierMask.None,
            };
        }

        /// <summary>
        /// Get the default tier mask for a given event type.
        /// Used as fallback when no SynapseContextProfileDef exists for the event type.
        /// </summary>
        public static ContextTierMask GetDefaultTiers(string eventType)
        {
            if (string.IsNullOrEmpty(eventType))
                return ContextTierMask.Standard;

            return eventType switch
            {
                "thought" => ContextTierMask.Lightweight,
                "dialogue" => ContextTierMask.Standard,
                "relationship" => ContextTierMask.Standard,
                "reaction" => ContextTierMask.Identity | ContextTierMask.PawnState,
                "event" => ContextTierMask.ColonyEvent,
                "quest" => ContextTierMask.Full,
                "custom" => ContextTierMask.Standard,
                _ => ContextTierMask.Standard,
            };
        }
    }
}
