namespace RimSynapse
{
    /// <summary>
    /// Defines a system prompt for a specific event type.
    /// Loaded from XML in Defs/SynapsePrompts/. Mod authors can add or
    /// override prompts without C# by creating XML files or XPath patches.
    ///
    /// Resolution order:
    ///   1. Mod-specific prompt (targetModId matches) — highest priority wins
    ///   2. Default prompt for event type (targetModId is null) — highest priority wins
    /// </summary>
    public class SynapsePromptDef : Verse.Def
    {
        /// <summary>
        /// The event type this prompt applies to.
        /// Standard types: "dialogue", "event", "thought", "reaction",
        /// "relationship", "quest", "custom".
        /// Companion mods may define additional event types.
        /// </summary>
        public string eventType;

        /// <summary>
        /// Which mod this prompt targets. If null, this is the default
        /// prompt for the event type (used when no mod-specific prompt exists).
        /// Example: "rimsynapse.chat", "rimsynapse.storyteller"
        /// </summary>
        public string targetModId;

        /// <summary>
        /// The system prompt text sent to the LLM.
        /// May contain newlines and formatting.
        /// </summary>
        public string systemPrompt;

        /// <summary>
        /// Priority for conflict resolution when multiple Defs match
        /// the same eventType + targetModId. Higher value wins.
        /// Core defaults use priority 0.
        /// </summary>
        public int priority = 0;

        /// <summary>
        /// Optional framing text appended after the system prompt
        /// but before the context block. Useful for event-specific
        /// instructions that augment rather than replace the prompt.
        /// </summary>
        public string contextFraming;
    }
}
