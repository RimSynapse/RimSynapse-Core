using System.Text.RegularExpressions;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Generic response sanitizer. Cleans LLM output for safe consumption.
    /// No dialogue-specific or schema-specific logic — consumer mods handle
    /// their own JSON structure validation.
    /// </summary>
    internal static class Sanitizer
    {
        // Pre-compiled regex patterns for performance
        private static readonly Regex ThinkBlockRegex = new Regex(
            @"<think>[\s\S]*?</think>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MarkdownWrapperRegex = new Regex(
            @"^```(?:json)?\s*([\s\S]*?)\s*```$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TrailingCommaObjectRegex = new Regex(
            @",\s*}",
            RegexOptions.Compiled);

        private static readonly Regex TrailingCommaArrayRegex = new Regex(
            @",\s*\]",
            RegexOptions.Compiled);

        /// <summary>
        /// Clean LLM output: strip think blocks, unwrap markdown, repair JSON.
        /// Returns the cleaned string.
        /// </summary>
        internal static string Clean(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            string cleaned = content.Trim();

            // 1. Strip <think>...</think> blocks
            cleaned = ThinkBlockRegex.Replace(cleaned, "").Trim();

            // 2. Unwrap markdown code blocks: ```json ... ``` or ``` ... ```
            var mdMatch = MarkdownWrapperRegex.Match(cleaned);
            if (mdMatch.Success && !string.IsNullOrWhiteSpace(mdMatch.Groups[1].Value))
            {
                SynapseLogger.Message("Unwrapped markdown code block from LLM output.");
                cleaned = mdMatch.Groups[1].Value.Trim();
            }

            // 3. Strip <think> again (catches blocks hidden inside markdown)
            cleaned = ThinkBlockRegex.Replace(cleaned, "").Trim();

            // 4. If it looks like JSON, try to repair common issues
            if (LooksLikeJson(cleaned))
            {
                cleaned = RepairJson(cleaned);
            }

            return cleaned;
        }

        /// <summary>
        /// Check if the string appears to be JSON (starts with { or [).
        /// </summary>
        private static bool LooksLikeJson(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            char first = s[0];
            return first == '{' || first == '[';
        }

        /// <summary>
        /// Attempt to repair common JSON issues from LLM output:
        /// - Extract JSON from surrounding text
        /// - Fix trailing commas
        /// </summary>
        private static string RepairJson(string input)
        {
            string json = input;

            // Extract JSON object/array from surrounding text
            int startIdx = json.IndexOf('{');
            int endIdx = json.LastIndexOf('}');
            if (startIdx >= 0 && endIdx > startIdx)
            {
                // Check if it's an array instead
                int arrayStart = json.IndexOf('[');
                int arrayEnd = json.LastIndexOf(']');
                if (arrayStart >= 0 && arrayStart < startIdx && arrayEnd > arrayStart)
                {
                    json = json.Substring(arrayStart, arrayEnd - arrayStart + 1);
                }
                else
                {
                    json = json.Substring(startIdx, endIdx - startIdx + 1);
                }
            }

            // Fix trailing commas: ,} → } and ,] → ]
            json = TrailingCommaObjectRegex.Replace(json, "}");
            json = TrailingCommaArrayRegex.Replace(json, "]");

            return json;
        }
    }
}
