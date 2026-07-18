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
        /// Strips markdown code fences and finds the matching closing brace.
        /// Returns null if no JSON object is found.
        /// </summary>
        public static string ExtractJson(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            string trimmed = raw.Trim();

            // 1. Try to extract content inside ```json ... ``` or ``` ... ```
            int fenceStart = trimmed.IndexOf("```json");
            if (fenceStart >= 0)
            {
                int contentStart = fenceStart + 7;
                int fenceEnd = trimmed.IndexOf("```", contentStart);
                if (fenceEnd > contentStart)
                {
                    return trimmed.Substring(contentStart, fenceEnd - contentStart).Trim();
                }
            }
            else
            {
                fenceStart = trimmed.IndexOf("```");
                if (fenceStart >= 0)
                {
                    int contentStart = fenceStart + 3;
                    int fenceEnd = trimmed.IndexOf("```", contentStart);
                    if (fenceEnd > contentStart)
                    {
                        return trimmed.Substring(contentStart, fenceEnd - contentStart).Trim();
                    }
                }
            }

            // 2. Fallback: Find matching braces by counting braces
            int firstBrace = trimmed.IndexOf('{');
            if (firstBrace >= 0)
            {
                int braceCount = 0;
                bool inString = false;
                bool isEscaped = false;
                for (int i = firstBrace; i < trimmed.Length; i++)
                {
                    char c = trimmed[i];
                    if (inString)
                    {
                        if (isEscaped)
                        {
                            isEscaped = false;
                        }
                        else if (c == '\\')
                        {
                            isEscaped = true;
                        }
                        else if (c == '"')
                        {
                            inString = false;
                        }
                    }
                    else
                    {
                        if (c == '"')
                        {
                            inString = true;
                        }
                        else if (c == '{')
                        {
                            braceCount++;
                        }
                        else if (c == '}')
                        {
                            braceCount--;
                            if (braceCount == 0)
                            {
                                return trimmed.Substring(firstBrace, i - firstBrace + 1);
                            }
                        }
                    }
                }
            }

            // 3. Last fallback: naive IndexOf/LastIndexOf
            int startIndex = trimmed.IndexOf('{');
            int endIndex = trimmed.LastIndexOf('}');
            if (startIndex >= 0 && endIndex > startIndex)
            {
                return trimmed.Substring(startIndex, endIndex - startIndex + 1);
            }

            return null;
        }
    }
}
