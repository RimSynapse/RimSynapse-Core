namespace RimSynapse
{
    /// <summary>
    /// Result from a chat completion request.
    /// </summary>
    public class ChatResult
    {
        /// <summary>Whether the request completed successfully.</summary>
        public bool success;

        /// <summary>The assistant's response text (sanitized if enabled).</summary>
        public string content;

        /// <summary>Error message if the request failed.</summary>
        public string error;

        /// <summary>Tokens used in the prompt.</summary>
        public int promptTokens;

        /// <summary>Tokens generated in the completion.</summary>
        public int completionTokens;

        /// <summary>Total request duration in milliseconds.</summary>
        public long durationMs;

        /// <summary>Which model actually responded.</summary>
        public string model;

        /// <summary>True if the request was delayed by queue budget throttling.</summary>
        public bool wasThrottled;

        /// <summary>Optional: List of tool calls requested by the assistant.</summary>
        public System.Collections.Generic.List<ChatToolCall> toolCalls;

        /// <summary>Create a successful result.</summary>
        public static ChatResult Success(string content, string model, int promptTokens,
            int completionTokens, long durationMs)
        {
            return new ChatResult
            {
                success = true,
                content = content,
                model = model,
                promptTokens = promptTokens,
                completionTokens = completionTokens,
                durationMs = durationMs,
            };
        }

        /// <summary>Create a failure result.</summary>
        public static ChatResult Failure(string error, long durationMs = 0)
        {
            return new ChatResult
            {
                success = false,
                error = error,
                durationMs = durationMs,
            };
        }
    }
}
