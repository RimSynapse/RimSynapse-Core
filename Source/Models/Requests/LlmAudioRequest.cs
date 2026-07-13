namespace RimSynapse
{
    /// <summary>
    /// Standardized request package for Audio Generation (TTS).
    /// </summary>
    public class LlmAudioRequest
    {
        public string InputText { get; set; }
        
        /// <summary>The voice model or ID to use (e.g. "alloy", "echo")</summary>
        public string Voice { get; set; }
        
        /// <summary>Desired output format, e.g. "mp3", "opus", "wav"</summary>
        public string ResponseFormat { get; set; } = "mp3";

        public LlmAudioRequest() { }

        public LlmAudioRequest(string inputText, string voice)
        {
            InputText = inputText;
            Voice = voice;
        }
    }
}
