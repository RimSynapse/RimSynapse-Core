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
        
        /// <summary>
        /// Forces the provider to output strict JSON.
        /// (Maps to response_format: json_object on OpenAI)
        /// </summary>
        public bool EnforceJson { get; set; }
        
        public bool DisableThinking { get; set; }

        public LlmTextRequest() { }

        public LlmTextRequest(string systemPrompt, string userMessage)
        {
            SystemPrompt = systemPrompt;
            Messages.Add(ChatMessage.User(userMessage));
        }
    }
}
