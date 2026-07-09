using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Models;

namespace RimSynapse.Internal
{
    internal static class ContextDataReaders
    {
        public static PawnPacket BuildPawnIdentity(Pawn pawn)
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

        public static void FillPawnState(PawnPacket packet, Pawn pawn, List<ContextAssembler.ContextSlot> slots)
        {
            // Mood
            if (pawn.needs?.mood != null)
            {
                packet.moodLevel = pawn.needs.mood.CurLevel;
                packet.thoughts = ThoughtFilter.FilterThoughts(pawn);
                slots.Add(ContextAssembler.MakeSlot("mood", ContextFormatters.FormatMood(packet)));
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
                slots.Add(ContextAssembler.MakeSlot("skills", ContextFormatters.FormatSkills(packet)));
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
                    slots.Add(ContextAssembler.MakeSlot("health", ContextFormatters.FormatHealth(packet)));
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
                    slots.Add(ContextAssembler.MakeSlot("relationships", ContextFormatters.FormatRelationships(packet)));
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
                        slots.Add(ContextAssembler.MakeSlot("opinions", ContextFormatters.FormatOpinions(packet)));
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
                slots.Add(ContextAssembler.MakeSlot("equipment", ContextFormatters.FormatEquipment(packet)));
            }

            // Ideology (DLC safe)
            try
            {
                if (ModsConfig.IdeologyActive && pawn.Ideo != null)
                {
                    packet.ideology = pawn.Ideo.name;
                    packet.precepts = pawn.Ideo.PreceptsListForReading?
                        .Select(p => p.Label).ToList();
                    slots.Add(ContextAssembler.MakeSlot("ideology",
                        $"[Ideology] {packet.ideology}"));
                }
            }
            catch { /* DLC not loaded */ }

            // Royalty title (DLC safe)
            try
            {
                if (ModsConfig.RoyaltyActive && pawn.royalty != null)
                {
                    var title = pawn.royalty.MostSeniorTitle;
                    if (title != null)
                    {
                        packet.royaltyTitle = title.def.label;
                    }
                }
            }
            catch { /* DLC not loaded */ }

            // Weather / Season / Biome (lightweight, attached to pawn state)
            if (pawn.Map != null)
            {
                string weather = $"[Environment] {GenLocalDate.Season(pawn.Map)}, " +
                    $"{pawn.Map.weatherManager?.curWeather?.label ?? "clear"}, " +
                    $"{pawn.Map.Biome?.label ?? "unknown biome"}";
                slots.Add(ContextAssembler.MakeSlot("weather", weather));
            }
        }

        public static ColonyPacket BuildColonyPacket()
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

        public static WorldPacket BuildWorldPacket()
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
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                SynapseLog.Debug("context", $"Faction enumeration error: {ex.Message}");
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

        public static void FillSyntheticData(ContextPacket packet, Pawn pawn, List<ContextAssembler.ContextSlot> slots)
        {
            if (pawn == null) return;

            // Psychology mod data — read via TryGetComp (null if mod absent)
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
                            if (memoriesField?.GetValue(comp) is IList memList)
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
                                    slots.Add(ContextAssembler.MakeSlot("memories", ContextFormatters.FormatMemories(entries)));
                                }
                            }

                            // Read personality summary
                            var personalityField = compType.GetField("personalitySummary");
                            if (personalityField?.GetValue(comp) is string personality &&
                                !string.IsNullOrEmpty(personality))
                            {
                                packet.sourcePawn.personalitySummary = personality;
                                slots.Add(ContextAssembler.MakeSlot("personalitySummary",
                                    $"[Personality] {personality}"));
                            }

                            break; // Found the comp, stop searching
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SynapseLog.Debug("context",
                    $"Could not read Psychology comp data: {ex.Message}");
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
                            if (threadsField?.GetValue(comp) is IList threadList)
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
                                    slots.Add(ContextAssembler.MakeSlot("narrativeThreads", ContextFormatters.FormatThreads(active)));
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SynapseLog.Debug("context",
                    $"Could not read Storyteller world data: {ex.Message}");
            }
        }
    }
}
