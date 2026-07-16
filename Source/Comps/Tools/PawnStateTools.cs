using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Tool handler: modify_pawn_state
    /// Applies direct modifications to colonist health, traits, skills, and ideology.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterPawnStateTools()
        {
            RegisterTool(
                "modify_pawn_state",
                "Apply direct modifications to a colonist's health (hediffs), traits, skills, or conversion to an ideology.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["pawnName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The name of the target colonist."
                        },
                        ["action"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The modification type to perform: 'add_hediff', 'remove_body_part', 'convert', 'add_trait', 'remove_trait', 'set_skill'."
                        },
                        ["hediffName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The Def name of the hediff to add (e.g. Cut, WoundInfection, Flu, Catatonic)."
                        },
                        ["bodyPart"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The Def name of the body part to modify or remove (e.g. LeftArm, RightEye, Brain)."
                        },
                        ["severity"] = new Dictionary<string, string>
                        {
                            ["type"] = "number",
                            ["description"] = "Optional severity for added hediffs (usually 0.0 to 1.0)."
                        },
                        ["ideoName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The name of the target ideology for conversion."
                        },
                        ["traitName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The Def name of the trait to add or remove."
                        },
                        ["degree"] = new Dictionary<string, string>
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional degree for added traits."
                        },
                        ["skillName"] = new Dictionary<string, string>
                        {
                            ["type"] = "string",
                            ["description"] = "The Def name of the skill to set."
                        },
                        ["level"] = new Dictionary<string, string>
                        {
                            ["type"] = "integer",
                            ["description"] = "The level of the skill to set (0 to 20)."
                        }
                    },
                    ["required"] = new List<string> { "pawnName", "action" }
                },
                args =>
                {
                    if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";
                    try
                    {
                        var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(args);
                        if (parsedArgs == null || !parsedArgs.TryGetValue("pawnName", out var pawnVal) || !parsedArgs.TryGetValue("action", out var actionVal))
                        {
                            return "{\"success\": false, \"reason\": \"Missing required arguments 'pawnName' or 'action'.\"}";
                        }

                        string pawnName = pawnVal?.ToString();
                        string action = actionVal?.ToString();

                        Pawn pawn = Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault(p => p.LabelShort.Equals(pawnName, StringComparison.OrdinalIgnoreCase));
                        if (pawn == null)
                        {
                            return $"{{\"success\": false, \"reason\": \"Colonist '{pawnName}' not found.\"}}";
                        }

                        return ExecutePawnStateAction(pawn, pawnName, action, parsedArgs);
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"success\": false, \"reason\": \"Modifying pawn state failed: {ex.Message}\"}}";
                    }
                }
            );
        }

        private static string ExecutePawnStateAction(Pawn pawn, string pawnName, string action, Dictionary<string, object> parsedArgs)
        {
            string hediffName = parsedArgs.TryGetValue("hediffName", out var hn) ? hn?.ToString() : null;
            string bodyPart = parsedArgs.TryGetValue("bodyPart", out var bp) ? bp?.ToString() : null;
            float? severity = null;
            if (parsedArgs.TryGetValue("severity", out var sevVal) && sevVal != null && float.TryParse(sevVal.ToString(), out float fSev)) severity = fSev;
            
            string ideoName = parsedArgs.TryGetValue("ideoName", out var idn) ? idn?.ToString() : null;
            string traitName = parsedArgs.TryGetValue("traitName", out var trn) ? trn?.ToString() : null;
            int? degree = null;
            if (parsedArgs.TryGetValue("degree", out var degVal) && degVal != null && int.TryParse(degVal.ToString(), out int iDeg)) degree = iDeg;

            string skillName = parsedArgs.TryGetValue("skillName", out var skn) ? skn?.ToString() : null;
            int? level = null;
            if (parsedArgs.TryGetValue("level", out var lvlVal) && lvlVal != null && int.TryParse(lvlVal.ToString(), out int iLvl)) level = iLvl;

            if (action.Equals("add_hediff", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(hediffName)) return "{\"success\": false, \"reason\": \"Missing 'hediffName' parameter.\"}";
                HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffName);
                if (hediffDef == null) return $"{{\"success\": false, \"reason\": \"HediffDef '{hediffName}' not found.\"}}";

                BodyPartRecord part = null;
                if (!string.IsNullOrEmpty(bodyPart))
                {
                    part = pawn.RaceProps.body.AllParts.FirstOrDefault(p => p.def.defName.Equals(bodyPart, StringComparison.OrdinalIgnoreCase));
                    if (part == null) return $"{{\"success\": false, \"reason\": \"BodyPartRecord '{bodyPart}' not found on pawn.\"}}";
                }

                Hediff hediff = HediffMaker.MakeHediff(hediffDef, pawn, part);
                if (severity.HasValue) hediff.Severity = severity.Value;
                pawn.health.AddHediff(hediff, part, null, null);

                return $"{{\"success\": true, \"message\": \"Successfully added hediff '{hediffName}' to {pawnName}.\"}}";
            }
            else if (action.Equals("remove_body_part", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(bodyPart)) return "{\"success\": false, \"reason\": \"Missing 'bodyPart' parameter.\"}";
                BodyPartRecord part = pawn.RaceProps.body.AllParts.FirstOrDefault(p => p.def.defName.Equals(bodyPart, StringComparison.OrdinalIgnoreCase));
                if (part == null) return $"{{\"success\": false, \"reason\": \"BodyPartRecord '{bodyPart}' not found on pawn.\"}}";

                pawn.health.AddHediff(HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn, part), part, null, null);
                return $"{{\"success\": true, \"message\": \"Successfully amputated/removed body part '{bodyPart}' from {pawnName}.\"}}";
            }
            else if (action.Equals("convert", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(ideoName)) return "{\"success\": false, \"reason\": \"Missing 'ideoName' parameter.\"}";
                if (!ModsConfig.IdeologyActive || pawn.Ideo == null) return "{\"success\": false, \"reason\": \"Ideology DLC is not active or target has no ideology.\"}";

                Ideo targetIdeo = Find.IdeoManager.IdeosListForReading.FirstOrDefault(i => i.name.Equals(ideoName, StringComparison.OrdinalIgnoreCase));
                if (targetIdeo == null) return $"{{\"success\": false, \"reason\": \"Ideology '{ideoName}' not found.\"}}";

                pawn.ideo.SetIdeo(targetIdeo);
                return $"{{\"success\": true, \"message\": \"Successfully converted {pawnName} to {ideoName}.\"}}";
            }
            else if (action.Equals("add_trait", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(traitName)) return "{\"success\": false, \"reason\": \"Missing 'traitName' parameter.\"}";
                TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(traitName);
                if (traitDef == null) return $"{{\"success\": false, \"reason\": \"TraitDef '{traitName}' not found.\"}}";

                pawn.story.traits.GainTrait(new Trait(traitDef, degree ?? 0));
                return $"{{\"success\": true, \"message\": \"Successfully added trait '{traitName}' to {pawnName}.\"}}";
            }
            else if (action.Equals("remove_trait", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(traitName)) return "{\"success\": false, \"reason\": \"Missing 'traitName' parameter.\"}";
                TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(traitName);
                if (traitDef == null) return $"{{\"success\": false, \"reason\": \"TraitDef '{traitName}' not found.\"}}";

                if (pawn.story.traits.HasTrait(traitDef))
                {
                    var trait = pawn.story.traits.GetTrait(traitDef);
                    pawn.story.traits.allTraits.Remove(trait);
                    return $"{{\"success\": true, \"message\": \"Successfully removed trait '{traitName}' from {pawnName}.\"}}";
                }
                return $"{{\"success\": false, \"reason\": \"{pawnName} does not have trait '{traitName}'.\"}}";
            }
            else if (action.Equals("set_skill", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(skillName)) return "{\"success\": false, \"reason\": \"Missing 'skillName' parameter.\"}";
                if (!level.HasValue) return "{\"success\": false, \"reason\": \"Missing 'level' parameter.\"}";

                SkillDef skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skillName);
                if (skillDef == null) return $"{{\"success\": false, \"reason\": \"SkillDef '{skillName}' not found.\"}}";

                var record = pawn.skills.GetSkill(skillDef);
                if (record == null) return $"{{\"success\": false, \"reason\": \"Skill record '{skillName}' not found on {pawnName}.\"}}";

                record.Level = level.Value;
                return $"{{\"success\": true, \"message\": \"Successfully set skill '{skillName}' level to {level.Value} for {pawnName}.\"}}";
            }

            return $"{{\"success\": false, \"reason\": \"Unknown action '{action}'.\"}}";
        }
    }
}
