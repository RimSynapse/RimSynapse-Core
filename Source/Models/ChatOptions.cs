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

        /// <summary>Request priority. 0 = normal, higher = processed sooner within mod's budget.</summary>
        public int priority;

        /// <summary>Default options with sanitization enabled and auto model mapping.</summary>
        public static ChatOptions Default => new ChatOptions();
    }
}
