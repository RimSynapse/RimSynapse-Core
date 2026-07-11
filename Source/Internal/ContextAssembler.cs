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
    internal static class ContextAssembler
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
        //  Pawn data builders (read vanilla APIs)
        // ────────────────────────────────────────────────────────

        private static void FillShortTermHistory(ContextPacket packet, Pawn sourcePawn, List<ContextSlot> slots)
        {
            var comp = Find.World?.GetComponent<SynapseCoreWorldComponent>();
            if (comp == null || comp.shortTermEvents == null || comp.shortTermEvents.Count == 0) return;

            // Filter for events involving this pawn, or global events if no source pawn
            var relevantEvents = new List<Models.ShortTermEvent>();
            foreach (var ev in comp.shortTermEvents)
            {
                if (sourcePawn == null)
                {
                    relevantEvents.Add(ev);
                }
                else if (ev.involvedPawnIds.Contains(sourcePawn.ThingID) || ev.eventType == Models.ShortTermEventType.GlobalEvent)
                {
                    relevantEvents.Add(ev);
                }
            }

            if (relevantEvents.Count > 0)
            {
                packet.recentEvents = relevantEvents.OrderBy(e => e.gameTick).ToList();
                
                var sb = new StringBuilder();
                foreach (var ev in packet.recentEvents)
                {
                    sb.AppendLine($"- [{ev.date.ToString()}] {ev.description}");
                }
                
                slots.Add(MakeSlot("shortTermHistory", sb.ToString()));
            }
        }

        private static PawnPacket BuildPawnIdentity(Pawn pawn)
        {
            return new PawnPacket
            {
                pawnId = pawn.ThingID,
                name = pawn.Name?.ToStringShort ?? "Unknown",
                gender = pawn.gender.ToString(),
                age = pawn.ageTracker?.AgeBiologicalYears ?? 0,
                backstoryChildhood = pawn.story?.Childhood?.title,
                backstoryAdulthood = pawn.story?.Adulthood?.title,
                traits = pawn.story?.traits?.allTraits?
                    .Select(t => t.Label).ToList() ?? new List<string>(),
            };
        }

        private static void FillPawnState(PawnPacket packet, Pawn pawn, List<ContextSlot> slots)
        {
            // Mood
            if (pawn.needs?.mood != null)
            {
                packet.moodLevel = pawn.needs.mood.CurLevel;
                packet.thoughts = ThoughtFilter.FilterThoughts(pawn);
                slots.Add(MakeSlot("mood", FormatMood(packet)));
            }

            // Skills
            if (pawn.skills?.skills != null)
            {
                packet.skills = new Dictionary<string, int>();
                packet.passions = new List<string>();
                foreach (var skill in pawn.skills.skills)
                {
                    packet.skills[skill.def.label] = skill.Level;
                    if (skill.passion != Passion.None)
                        packet.passions.Add($"{skill.def.label} ({skill.passion})");
                }
                slots.Add(MakeSlot("skills", FormatSkills(packet)));
            }

            // Health
            if (pawn.health?.hediffSet?.hediffs != null)
            {
                packet.healthConditions = pawn.health.hediffSet.hediffs
                    .Where(h => h.Visible)
                    .Select(h =>
                    {
                        string part = h.Part?.Label;
                        string label = h.Label;
                        return part != null ? $"{label} ({part})" : label;
                    })
                    .ToList();

                if (packet.healthConditions.Count > 0)
                    slots.Add(MakeSlot("health", FormatHealth(packet)));
            }

            // Relationships
            if (pawn.relations?.DirectRelations != null)
            {
                packet.relationships = pawn.relations.DirectRelations
                    .Select(r => new RelationshipEntry
                    {
                        relationLabel = r.def.label,
                        otherPawnName = r.otherPawn?.Name?.ToStringShort ?? "Unknown",
                        otherPawnId = r.otherPawn?.ThingID,
                    })
                    .ToList();

                if (packet.relationships.Count > 0)
                    slots.Add(MakeSlot("relationships", FormatRelationships(packet)));
            }

            // Opinions (top N by absolute value)
            try
            {
                var colonists = pawn.Map?.mapPawns?.FreeColonists;
                if (colonists != null)
                {
                    packet.opinions = colonists
                        .Where(c => c != pawn)
                        .Select(c => new OpinionEntry
                        {
                            pawnName = c.Name?.ToStringShort ?? "Unknown",
                            opinion = pawn.relations?.OpinionOf(c) ?? 0,
                        })
                        .OrderByDescending(o => Math.Abs(o.opinion))
                        .Take(5)
                        .ToList();

                    if (packet.opinions.Count > 0)
                        slots.Add(MakeSlot("opinions", FormatOpinions(packet)));
                }
            }
            catch { /* Opinion access can fail in edge cases */ }

            // Current job
            if (pawn.CurJob != null)
            {
                packet.currentJob = pawn.CurJob.def?.label ?? "unknown activity";
            }

            // Equipment
            var equipList = new List<string>();
            if (pawn.equipment?.Primary != null)
                equipList.Add(pawn.equipment.Primary.Label);
            if (pawn.apparel?.WornApparel != null)
            {
                foreach (var a in pawn.apparel.WornApparel)
                    equipList.Add(a.Label);
            }
            if (equipList.Count > 0)
            {
                packet.equipment = equipList;
                slots.Add(MakeSlot("equipment", FormatEquipment(packet)));
            }

            // Let expansion integrations inject their data
            OnAssemblePawnPacket?.Invoke(pawn, packet, slots);

            // Weather / Season / Biome (lightweight, attached to pawn state)
            if (pawn.Map != null)
            {
                string weather = $"[Environment] {GenLocalDate.Season(pawn.Map)}, " +
                    $"{pawn.Map.weatherManager?.curWeather?.label ?? "clear"}, " +
                    $"{pawn.Map.Biome?.label ?? "unknown biome"}";
                slots.Add(MakeSlot("weather", weather));
            }
        }

        // ────────────────────────────────────────────────────────
        //  Colony data builder (Tier 3)
        // ────────────────────────────────────────────────────────

        private static ColonyPacket BuildColonyPacket()
        {
            var map = Find.CurrentMap;
            if (map == null) return null;

            var packet = new ColonyPacket
            {
                colonistCount = map.mapPawns?.FreeColonistsCount ?? 0,
                wealthTotal = map.wealthWatcher?.WealthTotal ?? 0f,
                season = GenLocalDate.Season(map).ToString(),
                weather = map.weatherManager?.curWeather?.label ?? "unknown",
                biome = map.Biome?.label ?? "unknown",
                dangerLevel = map.dangerWatcher?.DangerRating.ToString() ?? "unknown",
                recentEvents = new List<string>(),
            };

            // Current research
            try
            {
                var currentProject = Find.ResearchManager?.GetProject();
                if (currentProject != null)
                {
                    packet.currentResearch = currentProject.label;
                    packet.researchProgress = currentProject.ProgressPercent;
                }
            }
            catch { /* Research access can vary */ }

            // Recent letters/events (last N)
            try
            {
                var letters = Find.LetterStack?.LettersListForReading;
                if (letters != null)
                {
                    int start = Math.Max(0, letters.Count - 5);
                    for (int i = start; i < letters.Count; i++)
                    {
                        packet.recentEvents.Add(letters[i].Label);
                    }
                }
            }
            catch { /* Letter access can vary */ }

            return packet;
        }

        // ────────────────────────────────────────────────────────
        //  World / Faction builder (Tier 4)
        // ────────────────────────────────────────────────────────

        private static WorldPacket BuildWorldPacket()
        {
            var packet = new WorldPacket
            {
                factions = new List<FactionEntry>(),
                activeQuestNames = new List<string>(),
            };

            // Factions
            try
            {
                var allFactions = Find.FactionManager?.AllFactionsVisible;
                if (allFactions != null)
                {
                    foreach (var faction in allFactions)
                    {
                        if (faction.IsPlayer) continue;

                        packet.factions.Add(new FactionEntry
                        {
                            factionName = faction.Name,
                            factionType = faction.def?.label ?? "unknown",
                            relationKind = faction.PlayerRelationKind.ToString(),
                            goodwill = faction.PlayerGoodwill,
                            leaderName = faction.leader?.Name?.ToStringShort,
                            // Enriched fields left null — Storyteller populates these
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                SynapseLogger.Message($"Faction enumeration error: {ex.Message}");
            }

            // Active quests
            try
            {
                var quests = Find.QuestManager?.QuestsListForReading;
                if (quests != null)
                {
                    foreach (var quest in quests.Where(q => !q.Historical && !q.dismissed))
                    {
                        packet.activeQuestNames.Add(quest.name);
                    }
                }
            }
            catch { /* Quest access can vary */ }

            return packet;
        }

        // ────────────────────────────────────────────────────────
        //  Tier 5: Synthetic data (companion mod, null-safe)
        // ────────────────────────────────────────────────────────

        private static void FillSyntheticData(ContextPacket packet, Pawn pawn, List<ContextSlot> slots)
        {
            if (pawn == null) return;

            // Psychology mod data — read via TryGetComp (null if mod absent)
            // We access by string name to avoid hard dependency
            try
            {
                var comps = pawn.AllComps;
                if (comps != null)
                {
                    foreach (var comp in comps)
                    {
                        var compType = comp.GetType();
                        if (compType.Name == "SynapsePawnComp")
                        {
                            // Read memories
                            var memoriesField = compType.GetField("memories");
                            if (memoriesField?.GetValue(comp) is System.Collections.IList memList)
                            {
                                var entries = new List<MemoryEntry>();
                                foreach (var mem in memList)
                                {
                                    var memType = mem.GetType();
                                    var summary = memType.GetField("summary")?.GetValue(mem) as string;
                                    var weight = memType.GetField("weight")?.GetValue(mem);
                                    var mType = memType.GetField("memoryType")?.GetValue(mem) as string;

                                    if (summary != null && weight != null)
                                    {
                                        entries.Add(new MemoryEntry
                                        {
                                            summary = summary,
                                            weight = (float)weight,
                                            memoryType = mType,
                                        });
                                    }
                                }

                                if (entries.Count > 0)
                                {
                                    // Sort by weight descending, take top N
                                    entries.Sort((a, b) => b.weight.CompareTo(a.weight));
                                    packet.sourcePawn.memories = entries.Take(5).ToList();
                                    slots.Add(MakeSlot("memories", FormatMemories(entries)));
                                }
                            }

                            // Read personality summary
                            var personalityField = compType.GetField("personalitySummary");
                            if (personalityField?.GetValue(comp) is string personality &&
                                !string.IsNullOrEmpty(personality))
                            {
                                packet.sourcePawn.personalitySummary = personality;
                                slots.Add(MakeSlot("personalitySummary",
                                    $"[Personality] {personality}"));
                            }

                            break; // Found the comp, stop searching
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SynapseLogger.Message($"Could not read Psychology comp data: {ex.Message}");
            }

            // Storyteller narrative threads — read from WorldComponent
            try
            {
                if (Find.World?.components != null)
                {
                    foreach (var comp in Find.World.components)
                    {
                        if (comp.GetType().Name == "SynapseWorldComponent")
                        {
                            var threadsField = comp.GetType().GetField("narrativeThreads");
                            if (threadsField?.GetValue(comp) is System.Collections.IList threadList)
                            {
                                var entries = new List<NarrativeThreadEntry>();
                                foreach (var thread in threadList)
                                {
                                    var tType = thread.GetType();
                                    var keyword = tType.GetField("keyword")?.GetValue(thread) as string;
                                    var category = tType.GetField("category")?.GetValue(thread) as string;
                                    var desc = tType.GetField("description")?.GetValue(thread) as string;
                                    var weight = tType.GetField("weight")?.GetValue(thread);
                                    var resolved = tType.GetField("isResolved")?.GetValue(thread);

                                    if (keyword != null)
                                    {
                                        entries.Add(new NarrativeThreadEntry
                                        {
                                            keyword = keyword,
                                            category = category,
                                            description = desc,
                                            weight = weight != null ? (float)weight : 0f,
                                            isResolved = resolved != null && (bool)resolved,
                                        });
                                    }
                                }

                                // Only include active (unresolved) threads
                                var active = entries.Where(t => !t.isResolved).ToList();
                                if (active.Count > 0)
                                {
                                    packet.narrativeThreads = active;
                                    slots.Add(MakeSlot("narrativeThreads", FormatThreads(active)));
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SynapseLogger.Message($"Could not read Storyteller world data: {ex.Message}");
            }
        }

        // ────────────────────────────────────────────────────────
        //  Formatting helpers (convert data to human-readable text)
        // ────────────────────────────────────────────────────────

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
