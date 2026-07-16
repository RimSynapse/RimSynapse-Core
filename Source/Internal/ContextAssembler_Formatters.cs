using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Formatting helpers that convert data packets into human-readable text
    /// for injection into LLM context windows.
    /// </summary>
    internal static partial class ContextAssembler
    {
        private static string FormatPawnIdentity(PawnPacket p) =>
            $"[{p.name}] {p.gender}, age {p.age}";

        private static string FormatBackstory(PawnPacket p)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(p.backstoryChildhood))
                parts.Add($"Childhood: {p.backstoryChildhood}");
            if (!string.IsNullOrEmpty(p.backstoryAdulthood))
                parts.Add($"Adulthood: {p.backstoryAdulthood}");
            return parts.Count > 0 ? $"[Backstory] {string.Join(". ", parts)}" : "";
        }

        private static string FormatTraits(PawnPacket p) =>
            p.traits?.Count > 0 ? $"[Traits] {string.Join(", ", p.traits)}" : "";

        private static string FormatMood(PawnPacket p)
        {
            var sb = new StringBuilder();
            sb.Append($"[Mood] {p.moodLevel:P0}");
            if (p.thoughts?.Count > 0)
            {
                sb.Append(" — ");
                sb.Append(string.Join("; ", p.thoughts.Select(t =>
                    $"{t.label} ({t.moodOffset:+#;-#;0})")));
            }
            return sb.ToString();
        }

        private static string FormatSkills(PawnPacket p)
        {
            if (p.skills == null || p.skills.Count == 0) return "";
            var top = p.skills.OrderByDescending(kv => kv.Value).Take(5);
            return $"[Skills] {string.Join(", ", top.Select(kv => $"{kv.Key}: {kv.Value}"))}";
        }

        private static string FormatHealth(PawnPacket p) =>
            p.healthConditions?.Count > 0
                ? $"[Health] {string.Join(", ", p.healthConditions)}"
                : "";

        private static string FormatRelationships(PawnPacket p) =>
            p.relationships?.Count > 0
                ? $"[Relationships] {string.Join(", ", p.relationships.Select(r => $"{r.relationLabel}: {r.otherPawnName}"))}"
                : "";

        private static string FormatOpinions(PawnPacket p) =>
            p.opinions?.Count > 0
                ? $"[Opinions] {string.Join(", ", p.opinions.Select(o => $"{o.pawnName}: {o.opinion:+#;-#;0}"))}"
                : "";

        private static string FormatEquipment(PawnPacket p) =>
            p.equipment?.Count > 0
                ? $"[Equipment] {string.Join(", ", p.equipment)}"
                : "";

        private static string FormatColony(ColonyPacket c)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Colony] {c.colonistCount} colonists, wealth: {c.wealthTotal:N0}");
            sb.Append($"  {c.season}, {c.weather}, {c.biome}, danger: {c.dangerLevel}");
            if (!string.IsNullOrEmpty(c.currentResearch))
                sb.Append($", researching: {c.currentResearch} ({c.researchProgress:P0})");
            return sb.ToString();
        }

        private static string FormatFactions(WorldPacket w)
        {
            if (w.factions == null || w.factions.Count == 0) return "";
            var sb = new StringBuilder("[Factions]");
            foreach (var f in w.factions)
            {
                sb.Append($"\n- {f.factionName} ({f.factionType}): {f.relationKind}, goodwill {f.goodwill}");
                if (!string.IsNullOrEmpty(f.leaderName))
                    sb.Append($", leader: {f.leaderName}");
            }
            return sb.ToString();
        }

        private static string FormatMemories(List<MemoryEntry> memories)
        {
            if (memories == null || memories.Count == 0) return "";
            return $"[Memories] {string.Join("; ", memories.Select(m => $"{m.summary} (w:{m.weight:F2})"))}";
        }

        private static string FormatThreads(List<NarrativeThreadEntry> threads)
        {
            if (threads == null || threads.Count == 0) return "";
            return $"[Story Threads] {string.Join("; ", threads.Select(t => $"{t.keyword} ({t.category}): {t.description}"))}";
        }

        private static void AppendPawnState(StringBuilder sb, PawnPacket p)
        {
            if (p.moodLevel > 0)
                sb.AppendLine(FormatMood(p));
            if (p.skills?.Count > 0)
                sb.AppendLine(FormatSkills(p));
            if (p.healthConditions?.Count > 0)
                sb.AppendLine(FormatHealth(p));
            if (p.relationships?.Count > 0)
                sb.AppendLine(FormatRelationships(p));
            if (p.opinions?.Count > 0)
                sb.AppendLine(FormatOpinions(p));
            if (!string.IsNullOrEmpty(p.personalitySummary))
                sb.AppendLine($"[Personality] {p.personalitySummary}");
            if (p.memories?.Count > 0)
                sb.AppendLine(FormatMemories(p.memories));
        }
    }
}
