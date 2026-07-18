using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Newtonsoft.Json;

namespace RimSynapse
{
    public class SynapseScript
    {
        public string scriptName;
        public List<SynapseScriptStep> steps;
    }

    public class SynapseScriptStep
    {
        public string type;
        public Dictionary<string, object> arguments;
    }

    public static class SynapseScriptRunner
    {
        private class ActiveScript
        {
            public SynapseScript script;
            public int currentStepIndex = 0;
            public bool isWaiting = false;
            public string waitCondition = null;
            public string waitPawnName = null;
            public int waitTimeoutTicks = 0;
            public int waitStartTick = 0;
            public Action<string> logCallback;
            public Action onFinished;
        }

        private static readonly List<ActiveScript> _activeScripts = new List<ActiveScript>();
        private static readonly List<ActiveScript> _toRemove = new List<ActiveScript>();
        
        private static readonly Dictionary<string, Func<Pawn, Dictionary<string, object>, bool>> _customConditions = 
            new Dictionary<string, Func<Pawn, Dictionary<string, object>, bool>>(StringComparer.OrdinalIgnoreCase);

        public static void RegisterWaitCondition(string conditionName, Func<Pawn, Dictionary<string, object>, bool> evaluator)
        {
            _customConditions[conditionName] = evaluator;
        }

        public static int ActiveScriptsCount => _activeScripts.Count;

        public static List<string> GetActiveScriptNames()
        {
            var list = new List<string>();
            foreach (var s in _activeScripts)
            {
                list.Add(s.script?.scriptName ?? "Unnamed");
            }
            return list;
        }

        public static void StartScript(SynapseScript script, Action<string> logCallback, Action onFinished = null)
        {
            if (script == null || script.steps == null || script.steps.Count == 0) return;
            
            var active = new ActiveScript
            {
                script = script,
                currentStepIndex = 0,
                logCallback = logCallback,
                onFinished = onFinished
            };
            
            _activeScripts.Add(active);
            logCallback?.Invoke($"[Script Runner] Starting script '{script.scriptName}' with {script.steps.Count} steps.");
            ExecuteNextStep(active);
        }

        public static void Tick()
        {
            if (_activeScripts.Count == 0) return;
            if (Find.TickManager == null) return;

            int currentTick = Find.TickManager.TicksGame;
            _toRemove.Clear();

            // We make a copy of active scripts to iterate safely in case the collection changes during execution
            var listCopy = _activeScripts.ToList();
            foreach (var active in listCopy)
            {
                if (active.isWaiting)
                {
                    bool conditionMet = false;
                    bool timeout = (currentTick - active.waitStartTick) >= active.waitTimeoutTicks;

                    if (!timeout)
                    {
                        var step = active.script.steps[active.currentStepIndex];
                        conditionMet = CheckCondition(active.waitCondition, active.waitPawnName, step.arguments);
                    }

                    if (conditionMet)
                    {
                        active.logCallback?.Invoke($"[Script Runner] Condition '{active.waitCondition}' met for '{active.waitPawnName}'. Resuming script.");
                        active.isWaiting = false;
                        active.currentStepIndex++;
                        ExecuteNextStep(active);
                    }
                    else if (timeout)
                    {
                        active.logCallback?.Invoke($"[Script Runner] Warning: Condition '{active.waitCondition}' timed out after {active.waitTimeoutTicks} ticks. Skipping step.");
                        active.isWaiting = false;
                        active.currentStepIndex++;
                        ExecuteNextStep(active);
                    }
                }
            }

            foreach (var remove in _toRemove)
            {
                _activeScripts.Remove(remove);
            }
        }

        private static void ExecuteNextStep(ActiveScript active)
        {
            while (active.currentStepIndex < active.script.steps.Count && !active.isWaiting)
            {
                var step = active.script.steps[active.currentStepIndex];
                if (step == null)
                {
                    active.currentStepIndex++;
                    continue;
                }

                // Alias Normalization
                if (step.type.Equals("equip_item", StringComparison.OrdinalIgnoreCase) || 
                    step.type.Equals("equip_weapon", StringComparison.OrdinalIgnoreCase) ||
                    step.type.Equals("equip", StringComparison.OrdinalIgnoreCase))
                {
                    step.type = "possess_colonist";
                    if (step.arguments == null) step.arguments = new Dictionary<string, object>();
                    step.arguments["action"] = "equip";
                    if (step.arguments.TryGetValue("weaponName", out var wn)) step.arguments["targetItemName"] = wn;
                    if (step.arguments.TryGetValue("itemName", out var itn)) step.arguments["targetItemName"] = itn;
                    if (step.arguments.TryGetValue("weaponDef", out var wd)) step.arguments["targetItemDef"] = wd;
                    if (step.arguments.TryGetValue("itemDef", out var itd)) step.arguments["targetItemDef"] = itd;
                    if (!step.arguments.ContainsKey("commandName")) step.arguments["commandName"] = "Equipping Weapon";
                }
                else if (step.type.Equals("damage_self", StringComparison.OrdinalIgnoreCase))
                {
                    step.type = "damage_self_with_equipped";
                }
                else if (step.type.Equals("clear_queue", StringComparison.OrdinalIgnoreCase) || 
                         step.type.Equals("stop_movement", StringComparison.OrdinalIgnoreCase))
                {
                    step.type = "possess_colonist";
                    if (step.arguments == null) step.arguments = new Dictionary<string, object>();
                    step.arguments["action"] = "clear";
                    if (!step.arguments.ContainsKey("commandName")) step.arguments["commandName"] = "Stopping Command";
                }

                if (step.type.Equals("wait_until", StringComparison.OrdinalIgnoreCase))
                {
                    string condition = step.arguments != null && step.arguments.TryGetValue("condition", out var cVal) ? cVal?.ToString() : null;
                    string pawnName = step.arguments != null && step.arguments.TryGetValue("pawnName", out var pVal) ? pVal?.ToString() : null;
                    int timeout = 3000;
                    if (step.arguments != null && step.arguments.TryGetValue("timeoutTicks", out var tVal) && tVal != null)
                    {
                        int.TryParse(tVal.ToString(), out timeout);
                    }

                    active.isWaiting = true;
                    active.waitCondition = condition;
                    active.waitPawnName = pawnName;
                    active.waitTimeoutTicks = timeout;
                    active.waitStartTick = Find.TickManager.TicksGame;
                    active.logCallback?.Invoke($"[Script Runner] Pausing script. Waiting for condition '{condition}' on pawn '{pawnName}' (Timeout: {timeout} ticks).");
                }
                else
                {
                    active.logCallback?.Invoke($"[Script Runner] Executing step {active.currentStepIndex + 1}: {step.type}");
                    try
                    {
                        string argsJson = JsonConvert.SerializeObject(step.arguments);
                        string result = SynapseToolRegistry.ExecuteTool(step.type, argsJson);
                        active.logCallback?.Invoke($"[Result] {result}");
                    }
                    catch (Exception ex)
                    {
                        active.logCallback?.Invoke($"[Error] Execution failed: {ex.Message}");
                    }
                    active.currentStepIndex++;
                }
            }

            if (active.currentStepIndex >= active.script.steps.Count)
            {
                active.logCallback?.Invoke($"[Script Runner] Script '{active.script.scriptName}' finished.");
                _toRemove.Add(active);
                try
                {
                    active.onFinished?.Invoke();
                }
                catch (Exception ex)
                {
                    active.logCallback?.Invoke($"[Error] onFinished callback failed: {ex.Message}");
                }
            }
        }

        private static bool CheckCondition(string condition, string pawnName, Dictionary<string, object> arguments)
        {
            if (Find.CurrentMap == null) return false;
            
            Pawn pawn = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p => p.LabelShort.Equals(pawnName, StringComparison.OrdinalIgnoreCase));
            if (pawn == null) return false;

            if (_customConditions.TryGetValue(condition, out var customEvaluator))
            {
                try
                {
                    return customEvaluator(pawn, arguments);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimSynapse] Exception in custom script condition '{condition}': {ex.Message}");
                    return false;
                }
            }

            if (condition.Equals("has_ranged_weapon", StringComparison.OrdinalIgnoreCase))
            {
                return pawn.equipment?.Primary != null && pawn.equipment.Primary.def.IsRangedWeapon;
            }
            else if (condition.Equals("has_equipped_weapon", StringComparison.OrdinalIgnoreCase) || 
                     condition.Equals("has_weapon", StringComparison.OrdinalIgnoreCase) || 
                     condition.Equals("has_any_weapon", StringComparison.OrdinalIgnoreCase))
            {
                return pawn.equipment?.Primary != null;
            }
            else if (condition.Equals("reached_cell", StringComparison.OrdinalIgnoreCase))
            {
                if (arguments != null && arguments.TryGetValue("targetX", out var xVal) && arguments.TryGetValue("targetZ", out var zVal))
                {
                    if (int.TryParse(xVal.ToString(), out int tx) && int.TryParse(zVal.ToString(), out int tz))
                    {
                        var cell = new IntVec3(tx, 0, tz);
                        return pawn.Position.DistanceToSquared(cell) <= 4f || (pawn.CurJob != null && pawn.CurJob.def != JobDefOf.Goto && pawn.Position.DistanceToSquared(cell) <= 9f);
                    }
                }
                return pawn.CurJob == null || pawn.CurJob.def != JobDefOf.Goto;
            }
            else if (condition.Equals("pawn_downed", StringComparison.OrdinalIgnoreCase))
            {
                return pawn.Downed;
            }

            return false;
        }
    }
}
