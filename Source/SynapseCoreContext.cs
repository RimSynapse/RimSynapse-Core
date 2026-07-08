using System.Collections.Generic;
using System.Linq;
using RimSynapse.Internal;
using Verse;

namespace RimSynapse
{
    /// <summary>
    /// Public static API for context embedding.
    /// Companion mods can use this to manually trigger context assembly,
    /// though in normal usage context is assembled automatically when
    /// requests are dispatched through the queue.
    /// </summary>
    public static class SynapseCoreContext
    {
        /// <summary>
        /// Build a context packet for a pawn and event type.
        /// Returns null if context embedding is disabled in settings.
        /// </summary>
        /// <param name="eventType">Event type (dialogue, event, thought, etc.)</param>
        /// <param name="sourcePawn">Primary pawn (null for colony-level events)</param>
        /// <param name="targetPawn">Secondary pawn (optional)</param>
        /// <param name="tiers">Override tier selection (null = use profile default)</param>
        /// <param name="weightOverrides">Per-slot weight overrides (null = use defaults)</param>
        /// <returns>ContextPacket or null if disabled</returns>
        public static ContextPacket BuildContext(
            string eventType,
            Pawn sourcePawn = null,
            Pawn targetPawn = null,
            ContextTierMask? tiers = null,
            Dictionary<string, float> weightOverrides = null)
        {
            if (!IsEnabled())
            {
                SynapseLog.Debug("context", "Context embedding is disabled.");
                return null;
            }

            return ContextAssembler.Build(eventType, sourcePawn, targetPawn,
                tiers, weightOverrides);
        }

        /// <summary>
        /// Serialize a context packet to a text block for injection
        /// into a system message.
        /// </summary>
        public static string SerializeContext(ContextPacket packet)
        {
            return ContextAssembler.SerializeToText(packet);
        }

        /// <summary>
        /// Build and serialize in one call. Convenience method.
        /// Returns empty string if disabled.
        /// </summary>
        public static string GetContextText(
            string eventType,
            Pawn sourcePawn = null,
            Pawn targetPawn = null,
            ContextTierMask? tiers = null,
            Dictionary<string, float> weightOverrides = null)
        {
            var packet = BuildContext(eventType, sourcePawn, targetPawn,
                tiers, weightOverrides);
            if (packet == null) return "";
            return SerializeContext(packet);
        }

        /// <summary>
        /// Resolve the system prompt for a given event type and mod.
        /// Reads SynapsePromptDef XML, resolving mod-specific prompts first,
        /// then falling back to default prompts.
        /// </summary>
        /// <param name="eventType">Event type to resolve prompt for</param>
        /// <param name="modId">Mod requesting the prompt (for mod-specific matching)</param>
        /// <returns>System prompt text, or empty string if none found</returns>
        public static string ResolvePrompt(string eventType, string modId = null)
        {
            // 1. Look for mod-specific prompt (highest priority first)
            if (!string.IsNullOrEmpty(modId))
            {
                var modSpecific = DefDatabase<SynapsePromptDef>.AllDefs
                    .Where(d => d.eventType == eventType && d.targetModId == modId)
                    .OrderByDescending(d => d.priority)
                    .FirstOrDefault();

                if (modSpecific != null)
                    return modSpecific.systemPrompt?.Trim() ?? "";
            }

            // 2. Fall back to default prompt for this event type
            var defaultPrompt = DefDatabase<SynapsePromptDef>.AllDefs
                .Where(d => d.eventType == eventType &&
                           string.IsNullOrEmpty(d.targetModId))
                .OrderByDescending(d => d.priority)
                .FirstOrDefault();

            return defaultPrompt?.systemPrompt?.Trim() ?? "";
        }

        /// <summary>
        /// Check if context embedding is enabled in settings.
        /// </summary>
        public static bool IsEnabled()
        {
            return RimSynapseMod.Instance?.Settings?.enableContextEmbedding ?? false;
        }
    }
}
