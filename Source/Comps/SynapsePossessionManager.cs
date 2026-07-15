using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSynapse
{
    public class PossessionState
    {
        public Pawn pawn;
        public List<string> releaseConditions = new List<string>();
        public int maxTicks;
        public int elapsedTicks;
        public int? targetX;
        public int? targetZ;
        public string commandName;

        public PossessionState(Pawn pawn, List<string> conditions, int maxTicks, int? targetX, int? targetZ, string commandName)
        {
            this.pawn = pawn;
            this.releaseConditions = conditions ?? new List<string>();
            this.maxTicks = maxTicks;
            this.elapsedTicks = 0;
            this.targetX = targetX;
            this.targetZ = targetZ;
            this.commandName = commandName;
        }
    }

    public static class SynapsePossessionManager
    {
        public static List<PossessionState> ActivePossessions = new List<PossessionState>();
        public static bool IsExecutingPossessionJob = false;

        public static bool IsPossessed(Pawn pawn)
        {
            if (pawn == null) return false;
            return ActivePossessions.Any(p => p.pawn == pawn);
        }

        public static void Possess(Pawn pawn, List<string> conditions, int maxTicks, int? targetX, int? targetZ, string commandName)
        {
            if (pawn == null) return;
            
            // Release existing if any
            Release(pawn, "Replaced by new possession");

            var state = new PossessionState(pawn, conditions, maxTicks, targetX, targetZ, commandName);
            ActivePossessions.Add(state);

            // Send standard alert message
            Messages.Message($"[RimSynapse] {pawn.LabelShort} is under psychic possession: {commandName}.", pawn, MessageTypeDefOf.CautionInput, false);
        }

        public static void Release(Pawn pawn, string reason = "Condition met")
        {
            if (pawn == null) return;
            var state = ActivePossessions.FirstOrDefault(p => p.pawn == pawn);
            if (state != null)
            {
                ActivePossessions.Remove(state);
                
                // Clear any running job immediately
                pawn.jobs?.ClearQueuedJobs();
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);

                Messages.Message($"[RimSynapse] Possession of {pawn.LabelShort} released ({reason}).", pawn, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        public static void OnPawnTookDamage(Pawn pawn)
        {
            var state = ActivePossessions.FirstOrDefault(p => p.pawn == pawn);
            if (state != null && state.releaseConditions.Any(c => c.Equals("Damage", StringComparison.OrdinalIgnoreCase)))
            {
                Release(pawn, "Took damage");
            }
        }

        public static void Tick()
        {
            if (ActivePossessions.Count == 0) return;

            // Tick active states
            for (int i = ActivePossessions.Count - 1; i >= 0; i--)
            {
                var state = ActivePossessions[i];
                var pawn = state.pawn;

                if (pawn == null || pawn.Dead)
                {
                    ActivePossessions.RemoveAt(i);
                    continue;
                }

                state.elapsedTicks++;

                // 1. Instant check: Downed state check is always instantaneous
                if (pawn.Downed)
                {
                    Release(pawn, "Downed");
                    continue;
                }

                bool released = false;

                // 2. High-Frequency Checks: evaluated every 4 ticks (responsive spatial/hazard checks)
                if (state.elapsedTicks % 4 == 0)
                {
                    foreach (var cond in state.releaseConditions)
                    {
                        if (cond.Equals("Timer", StringComparison.OrdinalIgnoreCase))
                        {
                            if (state.elapsedTicks >= state.maxTicks)
                            {
                                Release(pawn, "Time duration limit reached");
                                released = true;
                                break;
                            }
                        }
                        else if (cond.Equals("EnemyNearby", StringComparison.OrdinalIgnoreCase))
                        {
                            if (pawn.Map != null)
                            {
                                var hostiles = pawn.Map.mapPawns.AllPawns
                                    .Where(p => p != pawn && !p.Dead && p.Spawned && p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer));
                                foreach (var h in hostiles)
                                {
                                    if (pawn.Position.DistanceTo(h.Position) <= 15f)
                                    {
                                        Release(pawn, "Enemy nearby");
                                        released = true;
                                        break;
                                    }
                                }
                            }
                            if (released) break;
                        }
                        else if (cond.Equals("TargetReached", StringComparison.OrdinalIgnoreCase))
                        {
                            if (state.targetX.HasValue && state.targetZ.HasValue)
                            {
                                var dest = new IntVec3(state.targetX.Value, 0, state.targetZ.Value);
                                if (pawn.Position.DistanceTo(dest) <= 1.5f)
                                {
                                    if (state.commandName == "Psychic compulsion (departing colony)")
                                    {
                                        pawn.ExitMap(true, Rot4.Random);
                                        Messages.Message($"[RimSynapse] {pawn.LabelShort} has abandoned the colony permanently.", MessageTypeDefOf.NegativeEvent, false);
                                        ActivePossessions.RemoveAt(i);
                                        released = true;
                                        break;
                                    }

                                    Release(pawn, "Target destination reached");
                                    released = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (released) continue;

                // 3. Low-Frequency Checks: evaluated every 60 ticks (hourly status checks)
                if (state.elapsedTicks % 60 == 0)
                {
                    foreach (var cond in state.releaseConditions)
                    {
                        if (cond.Equals("ExtremeMood", StringComparison.OrdinalIgnoreCase))
                        {
                            float mood = pawn.needs?.mood?.CurLevel ?? 1f;
                            float threshold = pawn.mindState?.mentalBreaker?.BreakThresholdExtreme ?? 0.05f;
                            if (mood < threshold)
                            {
                                Release(pawn, "Extreme mood break threshold hit");
                                released = true;
                                break;
                            }
                        }
                        else if (cond.Equals("Hunger", StringComparison.OrdinalIgnoreCase))
                        {
                            float hunger = pawn.needs?.food?.CurLevelPercentage ?? 1f;
                            if (hunger < 0.1f)
                            {
                                Release(pawn, "Starving");
                                released = true;
                                break;
                            }
                        }
                        else if (cond.Equals("Exhaustion", StringComparison.OrdinalIgnoreCase))
                        {
                            float rest = pawn.needs?.rest?.CurLevelPercentage ?? 1f;
                            if (rest < 0.1f)
                            {
                                Release(pawn, "Exhausted");
                                released = true;
                                break;
                            }
                        }
                        else if (cond.Equals("Bleeding", StringComparison.OrdinalIgnoreCase))
                        {
                            if (pawn.health?.hediffSet?.BleedRateTotal > 0.01f)
                            {
                                Release(pawn, "Started bleeding");
                                released = true;
                                break;
                            }
                        }
                    }

                    if (released) continue;

                    // Fallback absolute limit
                    if (state.elapsedTicks >= state.maxTicks)
                    {
                        Release(pawn, "Maximum possession limit reached");
                    }
                }
            }
        }
    }
}
