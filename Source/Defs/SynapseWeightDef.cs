namespace RimSynapse
{
    /// <summary>
    /// Defines the base weight for a context slot.
    /// Loaded from XML in Defs/SynapseWeights/. Mod authors and players
    /// can adjust slot priorities without C# by editing XML or using XPath patches.
    ///
    /// Weight scale:
    ///   10  = Critical / Required (never dropped during budget trimming)
    ///    8  = Very Important
    ///    6  = Important
    ///    4  = Standard
    ///    2  = Supplementary
    ///    1  = Optional / Low-signal
    ///    0  = Disabled (slot is excluded entirely)
    /// </summary>
    public class SynapseWeightDef : Verse.Def
    {
        /// <summary>
        /// The context slot this weight applies to.
        /// Standard slots: "pawnIdentity", "eventType", "backstory", "traits",
        /// "mood", "skills", "health", "relationships", "opinions", "ideology",
        /// "colony", "factions", "weather", "memories", "narrativeThreads",
        /// "personalitySummary".
        /// </summary>
        public string slot;

        /// <summary>
        /// Base weight for this slot (0–10).
        /// Higher weight = higher priority during budget trimming.
        /// Slots are dropped lowest-weight-first when the token budget is exceeded.
        /// </summary>
        public float baseWeight;

        /// <summary>
        /// If true, this slot is never dropped during budget trimming,
        /// regardless of token budget constraints. Use sparingly.
        /// </summary>
        public bool required;

        /// <summary>
        /// Human-readable explanation of what this slot contains.
        /// Shown in documentation and DevTools.
        /// Uses the inherited Def.description field.
        /// </summary>
        // Note: description is inherited from Verse.Def — no need to redeclare.
    }
}
