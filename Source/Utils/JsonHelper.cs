namespace RimSynapse.Utils
{
    /// <summary>
    /// Shared utility for extracting valid JSON from LLM responses.
    /// Handles markdown fencing and stray text before/after the JSON body.
    /// </summary>
    public static class JsonHelper
    {
        /// <summary>
        /// Extracts the first valid JSON object from raw LLM output.
        /// Strips markdown code fences and finds the outermost { ... } block.
        /// Returns null if no JSON object is found.
        /// </summary>
        public static string ExtractJson(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            string json = raw.Trim();

            // Strip markdown code fences
            if (json.StartsWith("```json")) json = json.Substring(7);
            else if (json.StartsWith("```")) json = json.Substring(3);
            if (json.EndsWith("```")) json = json.Substring(0, json.Length - 3);
            json = json.Trim();

            // Find outermost { ... } block
            int startIndex = json.IndexOf('{');
            int endIndex = json.LastIndexOf('}');
            if (startIndex >= 0 && endIndex > startIndex)
            {
                return json.Substring(startIndex, endIndex - startIndex + 1);
            }

            return null;
        }
    }
}
