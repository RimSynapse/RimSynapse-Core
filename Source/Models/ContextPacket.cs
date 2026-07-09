using System.Collections.Generic;
using Verse;

namespace RimSynapse
{
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
}
