using System.Collections.Generic;

namespace RimSynapse
{
    /// <summary>
    /// Standardized request package for Vision Analysis.
    /// Extends LlmTextRequest with base64 image data.
    /// </summary>
    public class LlmVisionRequest : LlmTextRequest
    {
        /// <summary>
        /// Base64 encoded images to include in the user prompt.
        /// (e.g. data:image/jpeg;base64,...)
        /// </summary>
        public List<string> Base64Images { get; set; } = new List<string>();

        public LlmVisionRequest() : base() { }

        public LlmVisionRequest(string systemPrompt, string userMessage, List<string> base64Images) 
            : base(systemPrompt, userMessage)
        {
            Base64Images = base64Images;
        }
    }
}
