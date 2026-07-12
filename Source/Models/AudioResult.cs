namespace RimSynapse
{
    /// <summary>
    /// Result from an audio generation request.
    /// </summary>
    public class AudioResult
    {
        public bool success;
        
        /// <summary>Base64 encoded audio data.</summary>
        public string base64Audio;
        
        public string error;
        public long durationMs;
        public string model;

        public static AudioResult Success(string base64Audio, string model, long durationMs)
        {
            return new AudioResult
            {
                success = true,
                base64Audio = base64Audio,
                model = model,
                durationMs = durationMs
            };
        }

        public static AudioResult Failure(string error, long durationMs = 0)
        {
            return new AudioResult
            {
                success = false,
                error = error,
                durationMs = durationMs
            };
        }
    }
}
