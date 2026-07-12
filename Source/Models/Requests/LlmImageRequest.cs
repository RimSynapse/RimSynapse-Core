namespace RimSynapse
{
    /// <summary>
    /// Standardized request package for Image Generation.
    /// Unifies DALL-E, Pollinations, and Flux API parameters.
    /// </summary>
    public class LlmImageRequest
    {
        public string Prompt { get; set; }
        public string NegativePrompt { get; set; }
        
        /// <summary>e.g. "1024x1024", "16:9", etc.</summary>
        public string AspectRatio { get; set; } = "1:1";
        
        public int? Seed { get; set; }

        public LlmImageRequest() { }

        public LlmImageRequest(string prompt)
        {
            Prompt = prompt;
        }
    }
}
