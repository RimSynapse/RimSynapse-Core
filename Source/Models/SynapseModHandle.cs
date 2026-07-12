namespace RimSynapse
{
    /// <summary>
    /// Handle representing a registered consumer mod. Returned from
    /// <see cref="SynapseCore.Register"/>. Used to identify the mod
    /// in all API calls and for budget tracking.
    /// </summary>
    public class SynapseModHandle
    {
        /// <summary>Unique mod identifier (e.g., "rimsynapse.chat").</summary>
        public string ModId { get; }

        /// <summary>Human-readable name for the settings UI (e.g., "RimSynapse Chat").</summary>
        public string DisplayName { get; }

        /// <summary>
        /// Percentage of total query budget allocated to this mod (0-100).
        /// Configurable by the user via mod settings sliders.
        /// </summary>
        public float QueryBudgetPercent { get; set; }

        /// <summary>
        /// The mod's base system prompt. Injected as the beginning of the
        /// system message for all requests from this mod. Core appends
        /// context data after this prompt. If null, Core resolves a prompt
        /// from SynapsePromptDef XML based on event type.
        /// </summary>
        public string SystemPrompt { get; set; }

        /// <summary>
        /// Default context tiers this mod wants included.
        /// Can be overridden per-request via ChatOptions.contextTiers.
        /// </summary>
        public ContextTierMask DefaultTiers { get; set; } = ContextTierMask.Standard;

        /// <summary>Total requests this mod has made this session.</summary>
        public int RequestCount { get; internal set; }

        /// <summary>Requests currently queued for this mod.</summary>
        public int QueuedCount { get; internal set; }

        /// <summary>Requests made within the current rate-limit window.</summary>
        internal int WindowRequestCount { get; set; }

        /// <summary>
        /// Registered query types for this mod. Key = queryId, Value = (displayName, requiredCaps)
        /// </summary>
        public System.Collections.Generic.Dictionary<string, (string displayName, LlmCapabilities requiredCaps)> RegisteredQueries { get; } = new System.Collections.Generic.Dictionary<string, (string displayName, LlmCapabilities requiredCaps)>();

        /// <summary>
        /// Registers a query type with the Core Engine, allowing users to route these specific
        /// queries to different LLM providers in the RimSynapse Mod Settings.
        /// </summary>
        public void RegisterQueryType(string queryId, string displayName, LlmCapabilities requiredCaps = LlmCapabilities.Text)
        {
            if (!RegisteredQueries.ContainsKey(queryId))
            {
                RegisteredQueries.Add(queryId, (displayName, requiredCaps));
            }
        }

        public SynapseModHandle(string modId, string displayName)
        {
            ModId = modId;
            DisplayName = displayName;
            QueryBudgetPercent = 0f; // Set by ModRegistry after registration
        }

        public override string ToString() => $"{DisplayName} ({ModId})";
    }
}
