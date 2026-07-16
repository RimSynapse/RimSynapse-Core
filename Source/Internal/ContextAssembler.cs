using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Assembles context from live game state and companion mod data.
    /// Reads XML Defs for weight tables, tier selection, and thought filters.
    /// Applies budget trimming by dropping lowest-weight slots first.
    ///
    /// This is Core's central context engine — it reads vanilla RimWorld APIs
    /// and optional companion mod data (via null-safe TryGetComp), but never
    /// makes LLM calls or creates persistent data itself.
    /// </summary>
    internal static partial class ContextAssembler
    {
        /// <summary>
        /// Fired when assembling a pawn packet so expansion integrations can inject data.
        /// </summary>
        public static event Action<Pawn, PawnPacket, List<ContextSlot>> OnAssemblePawnPacket;

        /// <summary>
        /// Build a complete context packet for a request.
        /// </summary>
        /// <param name="eventType">Event type driving context assembly</param>
        /// <param name="sourcePawn">Primary pawn (may be null for colony events)</param>
        /// <param name="targetPawn">Secondary pawn (optional)</param>
        /// <param name="tiers">Which tiers to include (null = use profile default)</param>
        /// <param name="weightOverrides">Per-request weight overrides from C# (e.g., Storyteller boosting)</param>
        /// <param name="maxTokens">Override token budget (0 = use adaptive calculation)</param>
        /// <returns>Assembled ContextPacket with budget metadata</returns>
        internal static ContextPacket Build(
            string eventType,
            Pawn sourcePawn = null,
            Pawn targetPawn = null,
            ContextTierMask? tiers = null,
            Dictionary<string, float> weightOverrides = null,
            int maxTokens = 0)
        {
            var packet = new ContextPacket
            {
                eventType = eventType,
                gameTick = Find.TickManager?.TicksGame ?? 0,
                slotsFilled = new List<string>(),
                slotsDropped = new List<string>(),
            };

            // Resolve tier mask
            var tierMask = tiers ?? QueryBudgetProfile.GetTiers(eventType);

            // Build slots
            var slots = new List<ContextSlot>();

            // ── Tier 1: Identity ──
            if ((tierMask & ContextTierMask.Identity) != 0)
            {
                if (sourcePawn != null)
                {
                    packet.sourcePawn = BuildPawnIdentity(sourcePawn);
                    slots.Add(MakeSlot("pawnIdentity", FormatPawnIdentity(packet.sourcePawn)));
                    slots.Add(MakeSlot("backstory", FormatBackstory(packet.sourcePawn)));
                    slots.Add(MakeSlot("traits", FormatTraits(packet.sourcePawn)));
                }
                if (targetPawn != null)
                {
                    packet.targetPawn = BuildPawnIdentity(targetPawn);
                }
            }

            // ── Tier 2: Pawn State ──
            if ((tierMask & ContextTierMask.PawnState) != 0 && sourcePawn != null)
            {
                FillPawnState(packet.sourcePawn, sourcePawn, slots);
                if (targetPawn != null && packet.targetPawn != null)
                    FillPawnState(packet.targetPawn, targetPawn, slots);
            }

            // ── Tier 3: Colony ──
            if ((tierMask & ContextTierMask.Colony) != 0)
            {
                packet.colony = BuildColonyPacket();
                if (packet.colony != null)
                    slots.Add(MakeSlot("colony", FormatColony(packet.colony)));
            }

            // ── Tier 4: World / Factions ──
            if ((tierMask & ContextTierMask.World) != 0)
            {
                packet.world = BuildWorldPacket();
                if (packet.world != null)
                {
                    slots.Add(MakeSlot("factions", FormatFactions(packet.world)));
                }
            }

            // ── Tier 5: Synthetic (companion mod data) ──
            if ((tierMask & ContextTierMask.Synthetic) != 0)
            {
                FillSyntheticData(packet, sourcePawn, slots);
            }

            // ── Tier 6: Short-Term History ──
            if ((tierMask & ContextTierMask.ShortTermHistory) != 0)
            {
                FillShortTermHistory(packet, sourcePawn, slots);
            }

            // ── Event type framing ──
            slots.Insert(0, MakeSlot("eventType", $"Event: {eventType}"));

            // ── Budget trimming ──
            int budget = maxTokens > 0
                ? maxTokens
                : QueryBudgetProfile.CalculateBudget(eventType);

            // Apply weight overrides from profile XML
            var profileOverrides = QueryBudgetProfile.GetWeightOverrides(eventType);
            foreach (var kvp in profileOverrides)
            {
                var slot = slots.FirstOrDefault(s => s.Name == kvp.Key);
                if (slot != null) slot.Weight = kvp.Value;
            }

            // Apply C# runtime overrides (e.g., Storyteller dynamic boosting)
            if (weightOverrides != null)
            {
                foreach (var kvp in weightOverrides)
                {
                    var slot = slots.FirstOrDefault(s => s.Name == kvp.Key);
                    if (slot != null) slot.Weight = kvp.Value;
                }
            }

            // Sort by weight descending, trim to budget
            slots.Sort((a, b) => b.Weight.CompareTo(a.Weight));

            int totalTokens = 0;
            var included = new List<ContextSlot>();
            foreach (var slot in slots)
            {
                int slotTokens = QueryBudgetProfile.EstimateTokens(slot.Text);
                if (slot.Required || totalTokens + slotTokens <= budget)
                {
                    included.Add(slot);
                    totalTokens += slotTokens;
                    packet.slotsFilled.Add(slot.Name);
                }
                else
                {
                    packet.slotsDropped.Add(slot.Name);
                }
            }

            packet.estimatedTokens = totalTokens;

            // Log assembly result
            SynapseLogger.Debug("context",
                $"Context assembled: {eventType} | " +
                $"{packet.slotsFilled.Count} slots filled, " +
                $"{packet.slotsDropped.Count} dropped | " +
                $"~{totalTokens} tokens (budget: {budget})");

            if (packet.slotsDropped.Count > 0)
            {
                SynapseLogger.Message($"Dropped slots: {string.Join(", ", packet.slotsDropped)}");
            }

            return packet;
        }

        /// <summary>
        /// Serialize a ContextPacket to a text block suitable for injection
        /// into a system message.
        /// </summary>
        internal static string SerializeToText(ContextPacket packet)
        {
            if (packet == null) return "";

            // Rebuild the text from the packet's filled slots
            // This is a simplified version — the full implementation would
            // serialize each field into a structured text format
            var sb = new StringBuilder();
            sb.AppendLine("--- CONTEXT ---");

            if (packet.sourcePawn != null)
            {
                sb.AppendLine(FormatPawnIdentity(packet.sourcePawn));
                sb.AppendLine(FormatBackstory(packet.sourcePawn));
                sb.AppendLine(FormatTraits(packet.sourcePawn));
                AppendPawnState(sb, packet.sourcePawn);
            }

            if (packet.targetPawn != null)
            {
                sb.AppendLine();
                sb.AppendLine($"[Other: {packet.targetPawn.name}]");
                sb.AppendLine(FormatPawnIdentity(packet.targetPawn));
            }

            if (packet.colony != null)
                sb.AppendLine(FormatColony(packet.colony));

            if (packet.world != null)
                sb.AppendLine(FormatFactions(packet.world));

            if (packet.narrativeThreads?.Count > 0)
            {
                sb.AppendLine("[Narrative Threads]");
                foreach (var t in packet.narrativeThreads)
                {
                    if (!t.isResolved)
                        sb.AppendLine($"- {t.keyword} ({t.category}): {t.description}");
                }
            }

            if (packet.recentEvents?.Count > 0)
            {
                sb.AppendLine("[Recent History]");
                foreach (var ev in packet.recentEvents)
                {
                    sb.AppendLine($"- [{ev.date.ToString()}] {ev.description}");
                }
            }

            sb.AppendLine("--- END CONTEXT ---");
            return sb.ToString();
        }


        // ────────────────────────────────────────────────────────
        //  Slot helper
        // ────────────────────────────────────────────────────────

        public static ContextSlot MakeSlot(string name, string text)
        {
            // Read base weight from XML Def
            var weightDef = DefDatabase<SynapseWeightDef>.AllDefs
                .FirstOrDefault(d => d.slot == name);

            return new ContextSlot
            {
                Name = name,
                Text = text ?? "",
                Weight = weightDef?.baseWeight ?? 4f,
                Required = weightDef?.required ?? false,
            };
        }

        /// <summary>
        /// Slot representation for budget trimming.
        /// </summary>
        public class ContextSlot
        {
            public string Name;
            public string Text;
            public float Weight;
            public bool Required;
        }
    }
}
