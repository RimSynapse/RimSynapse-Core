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
    }
}
