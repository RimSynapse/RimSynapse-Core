using System.Collections.Generic;

namespace RimSynapse
{
    /// <summary>
    /// Standardized request package for Text Generation.
    /// Sent to HttpEngine which routes it to the specific provider translator.
    /// </summary>
    public class LlmTextRequest
    {
        public string SystemPrompt { get; set; }
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
        public float? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        
        public bool EnforceJson { get; set; }
        public bool DisableThinking { get; set; }

        /// <summary>
        /// List of active Model Context Protocol / Game tools available for this request.
        /// </summary>
        public List<GameToolDefinition> Tools { get; set; } = new List<GameToolDefinition>();

        public LlmTextRequest() { }

        public LlmTextRequest(string systemPrompt, string userMessage)
        {
            SystemPrompt = systemPrompt;
            Messages.Add(ChatMessage.User(userMessage));
        }
    }
}
