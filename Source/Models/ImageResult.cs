namespace RimSynapse
{
    /// <summary>
    /// Result from an image generation request.
    /// </summary>
    public class ImageResult
    {
        public bool success;
        
        /// <summary>Base64 encoded PNG or JPEG image.</summary>
        public string base64Data;
        
        public string error;
        public long durationMs;
        public string model;

        public static ImageResult Success(string base64Data, string model, long durationMs)
        {
            return new ImageResult
            {
                success = true,
                base64Data = base64Data,
                model = model,
                durationMs = durationMs
            };
        }

        public static ImageResult Failure(string error, long durationMs = 0)
        {
            return new ImageResult
            {
                success = false,
                error = error,
                durationMs = durationMs
            };
        }
    }
}
