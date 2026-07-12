using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Calculates the adaptive token budget for context assembly.
    /// Reads SynapseContextProfileDef XML for per-event-type settings.
    ///
    /// Budget = (contextWindow - completion - conversation - systemPrompt)
    ///          × budgetFraction × performanceScalar
    /// </summary>
    internal static class QueryBudgetProfile
    {
        /// <summary>
        /// Get the budget fraction for a given event type from XML Defs.
        /// Falls back to a hardcoded default if no Def exists.
        /// </summary>
        internal static float GetBudgetFraction(string eventType)
        {
            var profile = FindProfile(eventType);
            if (profile != null)
                return profile.budgetFraction;

            // Hardcoded fallbacks (should rarely be needed)
            return eventType switch
            {
                "thought" => 0.15f,
                "dialogue" => 0.50f,
                "relationship" => 0.35f,
                "reaction" => 0.25f,
                "event" => 0.70f,
                "quest" => 0.60f,
                "custom" => 0.50f,
                _ => 0.40f,
            };
        }

        /// <summary>
        /// Get the context tier mask for a given event type from XML Defs.
        /// Falls back to ContextTierMaskHelper defaults.
        /// </summary>
        internal static ContextTierMask GetTiers(string eventType)
        {
            var profile = FindProfile(eventType);
            if (profile?.includeTiers != null && profile.includeTiers.Count > 0)
            {
                var mask = ContextTierMask.None;
                foreach (var tier in profile.includeTiers)
                {
                    mask |= ContextTierMaskHelper.ParseTier(tier);
                }
                return mask;
            }

            return ContextTierMaskHelper.GetDefaultTiers(eventType);
        }

        /// <summary>
        /// Get per-slot weight overrides from the profile for a given event type.
        /// Returns an empty dictionary if no overrides exist.
        /// </summary>
        internal static Dictionary<string, float> GetWeightOverrides(string eventType)
        {
            var profile = FindProfile(eventType);
            var overrides = new Dictionary<string, float>();

            if (profile?.weightOverrides != null)
            {
                foreach (var o in profile.weightOverrides)
                {
                    if (!string.IsNullOrEmpty(o.slot))
                        overrides[o.slot] = o.weight;
                }
            }

            return overrides;
        }

        /// <summary>
        /// Calculate the full adaptive token budget for a request.
        /// </summary>
        /// <param name="eventType">Event type driving the request</param>
        /// <param name="systemPromptTokens">Estimated tokens in the mod's system prompt</param>
        /// <param name="conversationTokens">Estimated tokens in conversation history</param>
        /// <returns>Token budget available for context assembly</returns>
        internal static int CalculateBudget(
            string eventType,
            int systemPromptTokens = 0,
            int conversationTokens = 0)
        {
            // Available context window from LM Studio
            int contextWindow = ModelManager.ContextLength ?? 4096;

            // Reserve space for completion output
            int reservedForCompletion = Math.Max(512, (int)(contextWindow * 0.25f));

            // Available space after reservations
            int available = contextWindow
                          - reservedForCompletion
                          - conversationTokens
                          - systemPromptTokens;

            if (available <= 0)
            {
                SynapseLogger.Warn("context",
                    $"No token budget remaining after reservations. " +
                    $"Window={contextWindow}, completion={reservedForCompletion}, " +
                    $"conversation={conversationTokens}, prompt={systemPromptTokens}");
                return 128; // Minimal fallback
            }

            // Apply event-type budget fraction
            float fraction = GetBudgetFraction(eventType);
            int budget = (int)(available * fraction);

            // Performance scaling: shrink budget if responses are slow
            float perfScalar = GetPerformanceScalar();
            budget = (int)(budget * perfScalar);

            // Floor at 64 tokens
            return Math.Max(64, budget);
        }

        /// <summary>
        /// Performance-based budget scaling.
        /// If recent responses are slow, shrink the context budget.
        /// </summary>
        private static float GetPerformanceScalar()
        {
            // Access recent request durations from the queue
            // (simplified — in practice, read from RequestQueue metrics)
            float avgMs = RequestQueue.AverageResponseMs;

            if (avgMs > 10000) return 0.6f;  // Heavy shrink
            if (avgMs > 5000) return 0.8f;   // Moderate shrink
            return 1.0f;                      // Full speed
        }

        /// <summary>
        /// Find the SynapseContextProfileDef for a given event type.
        /// Returns null if no matching Def exists.
        /// </summary>
        private static SynapseContextProfileDef FindProfile(string eventType)
        {
            if (string.IsNullOrEmpty(eventType))
                return null;

            return DefDatabase<SynapseContextProfileDef>.AllDefs
                .FirstOrDefault(d => d.eventType == eventType);
        }

        /// <summary>
        /// Simple token estimation: character count divided by 4.
        /// Good enough for budget planning; actual token count
        /// comes from LM Studio's response.
        /// </summary>
        internal static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Length / 4;
        }
    }
}
