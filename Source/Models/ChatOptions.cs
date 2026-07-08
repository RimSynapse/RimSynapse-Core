namespace RimSynapse
{
    /// <summary>
    /// Options for a chat completion request. All fields are optional.
    /// </summary>
    public class ChatOptions
    {
        /// <summary>Model name to use. Null = auto-map to active model in LM Studio.</summary>
        public string model;

        /// <summary>Maximum tokens to generate. Null = LM Studio default.</summary>
        public int? maxTokens;

        /// <summary>Sampling temperature. Null = LM Studio default.</summary>
        public float? temperature;

        /// <summary>Enable response sanitization (strip think blocks, repair JSON). Default: true.</summary>
        public bool sanitize = true;

        /// <summary>
        /// Enable model thinking/reasoning. Null = use global setting,
        /// true = force thinking on, false = force thinking off.
        /// Disabling thinking saves tokens and reduces latency.
        /// </summary>
        public bool? thinking;

        /// <summary>Request priority. 0 = normal, higher = processed sooner within mod's budget.</summary>
        public int priority;

        // --- Context Embedding ---

        /// <summary>
        /// Event type for context assembly. Controls which context profile
        /// (tiers, budget fraction, weight overrides) is used.
        /// Standard types: "dialogue", "event", "thought", "reaction",
        /// "relationship", "quest", "custom".
        /// If null, no context is assembled (raw passthrough).
        /// </summary>
        public string eventType;

        /// <summary>
        /// Override context tier selection for this request.
        /// If null, uses the mod's DefaultTiers or the profile default.
        /// </summary>
        public ContextTierMask? contextTiers;

        /// <summary>
        /// Per-slot weight overrides for this request.
        /// These override both XML weights and profile overrides.
        /// Used by Storyteller for dynamic weight boosting.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, float> weightOverrides;

        /// <summary>
        /// Source pawn for context assembly (the primary pawn in the interaction).
        /// If null and eventType is set, context will be colony-level only.
        /// </summary>
        public Verse.Pawn sourcePawn;

        /// <summary>
        /// Target pawn for context assembly (secondary pawn in two-pawn interactions).
        /// </summary>
        public Verse.Pawn targetPawn;

        /// <summary>Default options with sanitization enabled and auto model mapping.</summary>
        public static ChatOptions Default => new ChatOptions();
    }
}
