namespace RimSynapse
{
    /// <summary>
    /// A single message in an OpenAI-format chat conversation.
    /// </summary>
    public class ChatMessage
    {
        /// <summary>Message role: "system", "user", or "assistant".</summary>
        public string role;

        /// <summary>Message text content.</summary>
        public string content;

        public string tool_call_id;
        public string name;
        public System.Collections.Generic.List<ChatToolCall> tool_calls;

        public ChatMessage() { }

        public ChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }

        /// <summary>Create a system message.</summary>
        public static ChatMessage System(string content) => new ChatMessage("system", content);

        /// <summary>Create a user message.</summary>
        public static ChatMessage User(string content) => new ChatMessage("user", content);

        /// <summary>Create an assistant message.</summary>
        public static ChatMessage Assistant(string content) => new ChatMessage("assistant", content);

        /// <summary>Create a tool response message.</summary>
        public static ChatMessage Tool(string content, string toolCallId) => new ChatMessage("tool", content) { tool_call_id = toolCallId };
    }
}
