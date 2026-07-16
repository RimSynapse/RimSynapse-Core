using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;
using RimSynapse.Models;

namespace RimSynapse.Patches
{
    public class PreRolledQualityInfo
    {
        public int jobId;
        public QualityCategory quality;
        public string tributeText;
        public Texture2D artTexture;
        public string filePath;
        public bool isReady = false;
        public bool isGenerating = false;
        public bool showImmediatelyOnReady = false;
        public Thing finishedItem;
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class Patch_Pawn_Tick_Legendary
    {
        public static Dictionary<Pawn, PreRolledQualityInfo> preRolledQualities = new Dictionary<Pawn, PreRolledQualityInfo>();

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (__instance.Spawned)
            {
                if (!__instance.Dead)
                {
                    CheckEarlyLegendaryRoll(__instance);
                }
            }
        }

        private static void CheckEarlyLegendaryRoll(Pawn pawn)
        {
            if (pawn.CurJob == null) return;

            // Invalidate cached info if job ID has changed
            if (preRolledQualities.TryGetValue(pawn, out var cachedInfo))
            {
                if (cachedInfo.jobId != pawn.CurJob.loadID)
                {
                    preRolledQualities.Remove(pawn);
                }
            }

            // 1. Crafting check
            if (pawn.jobs?.curDriver is JobDriver_DoBill billDriver)
            {
                if (billDriver.workLeft > 0f)
                {
                    if (billDriver.workLeft <= 25f)
                    {
                        TryPreRollForBill(pawn, billDriver);
                    }
                }
            }

            // 2. Construction check
            if (pawn.jobs?.curDriver is JobDriver_ConstructFinishFrame constructDriver)
            {
                var frame = constructDriver.job.targetA.Thing;
                if (frame != null)
                {
                    float workLeft = Traverse.Create(frame).Field("workLeft").GetValue<float>();
                    if (workLeft > 0f)
                    {
                        if (workLeft <= 25f)
                        {
                            TryPreRollForFrame(pawn, frame);
                        }
                    }
                }
            }
        }

        private static void TryPreRollForBill(Pawn pawn, JobDriver_DoBill billDriver)
        {
            if (!SynapseClient.IsOnline) return;
            if (preRolledQualities.ContainsKey(pawn)) return;
            if (billDriver.job == null) return;
            if (billDriver.job.bill == null) return;
            if (billDriver.job.bill.recipe == null) return;

            SkillDef skill = billDriver.job.bill.recipe.workSkill;
            if (skill == null)
            {
                skill = SkillDefOf.Crafting;
            }

            QualityCategory earlyQuality = QualityUtility.GenerateQualityCreatedByPawn(pawn, skill, false);

            var info = new PreRolledQualityInfo
            {
                jobId = billDriver.job.loadID,
                quality = earlyQuality
            };
            preRolledQualities[pawn] = info;

            if (earlyQuality == QualityCategory.Legendary)
            {
                string itemLabel = billDriver.job.bill.recipe.LabelCap;
                info.isGenerating = true;
                TriggerPreRolledLegendaryTribute(pawn, itemLabel, null, info);
            }
        }

        private static void TryPreRollForFrame(Pawn pawn, Thing frame)
        {
            if (!SynapseClient.IsOnline) return;
            if (preRolledQualities.ContainsKey(pawn)) return;

            SkillDef skill = SkillDefOf.Construction;
            QualityCategory earlyQuality = QualityUtility.GenerateQualityCreatedByPawn(pawn, skill, false);

            var info = new PreRolledQualityInfo
            {
                jobId = pawn.CurJob.loadID,
                quality = earlyQuality
            };
            preRolledQualities[pawn] = info;

            if (earlyQuality == QualityCategory.Legendary)
            {
                string itemLabel = frame.Label;
                info.isGenerating = true;
                TriggerPreRolledLegendaryTribute(pawn, itemLabel, frame, info);
            }
        }

        private static void TriggerPreRolledLegendaryTribute(Pawn worker, string itemLabel, Thing originThing, PreRolledQualityInfo info)
        {
            string workerName = worker.LabelShort;
            string artDesc = "";

            if (originThing != null)
            {
                var compArt = originThing.TryGetComp<CompArt>();
                if (compArt != null)
                {
                    if (compArt.Active)
                    {
                        artDesc = compArt.GenerateImageDescription();
                    }
                }
            }

            string recentEventsList = "None recently.";
            try
            {
                var coreComp = Find.World?.GetComponent<SynapseCoreWorldComponent>();
                if (coreComp != null)
                {
                    var events = coreComp.GetMostSignificantEvents(5);
                    if (events.Any())
                    {
                        recentEventsList = string.Join("\n", events.Select(e => "- " + e.eventDescription));
                    }
                }
            }
            catch { }

            string prompt = "Write a legendary storytelling tribute for " + workerName + " who has crafted a legendary " + itemLabel + ". ";
            if (!string.IsNullOrEmpty(artDesc))
            {
                prompt += "The item features this engraved scene: " + artDesc + ". ";
            }
            prompt += "\n\nColony History / Recent Historical Events:\n" + recentEventsList + "\n\n" +
                      "You must return your response in this exact format:\n" +
                      "TRIBUTE: [A mythical, grand storytelling tribute celebrating the crafter and this work, in 2-3 sentences]\n" +
                      "PROMPT: [A 1-sentence prompt for an AI image generator depicting this item, in RimWorld vector pawn style]\n" +
                      "Do not write any other text or explanation.\n\n" +
                      "CRITICAL constraints:\n" +
                      "1. The artwork and tribute MUST be themed around or reference one of the major triumphs or extreme events from the Colony History listed above. If no history is listed, base it on the crafter's personal achievement.\n" +
                      "2. Keep the PROMPT for the image generator simple, focused, and not overly descriptive so that the AI can render it cleanly.\n" +
                      "3. The PROMPT must request a RimWorld-styled 2D vector pawn asset (e.g. flat vector, simple shapes, RimWorld style, simple body, floating head/hands, minimalist background) rather than a realistic, lifelike scene.\n" +
                      "4. If the item is a sculpture, statue, or carving, the PROMPT for the image generator MUST describe a physical, carved sculpture or statue of the scene (e.g. 'A grand marble sculpture depicting...', 'An intricate wooden carving of...'), NOT as the actual living scene. The image generated should show the object as an art piece (sculpture, carving, or engraving) on a pedestal or display, showing its physical material and details.";

            var messages = new List<ChatMessage>
            {
                ChatMessage.System("You are the legendary storytelling bard. Write grand tributes and simple, focused visual prompts in RimWorld 2D vector pawn style. The artwork must depict a historical event of the colony (such as a marriage, victory, or raid) using RimWorld's signature minimalist vector style."),
                ChatMessage.User(prompt)
            };

            var options = new ChatOptions
            {
                queryId = "LegendaryTribute",
                requestName = "Writing Legendary Tribute",
                maxTokens = 250,
                temperature = 0.7f
            };

            SynapseClient.ChatAsync(RimSynapseMod.ModHandle, messages, options, (result) =>
            {
                if (!result.success) return; // Silent fallback to vanilla behavior if request fails
                if (string.IsNullOrEmpty(result.content)) return;

                string tribute = "";
                string imagePrompt = "";

                string[] lines = result.content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("TRIBUTE:", StringComparison.OrdinalIgnoreCase))
                    {
                        tribute = line.Substring("TRIBUTE:".Length).Trim();
                    }
                    else if (line.StartsWith("PROMPT:", StringComparison.OrdinalIgnoreCase))
                    {
                        imagePrompt = line.Substring("PROMPT:".Length).Trim();
                    }
                }

                if (tribute.StartsWith("`") || tribute.EndsWith("`")) tribute = tribute.Replace("`", "");
                if (imagePrompt.StartsWith("`") || imagePrompt.EndsWith("`")) imagePrompt = imagePrompt.Replace("`", "");

                if (string.IsNullOrEmpty(tribute) || string.IsNullOrEmpty(imagePrompt)) return; // Silent fallback to vanilla if parsing fails

                // Trigger pollinations image generation in RimWorld vector style
                SynapseImageClient.GenerateAndSaveImageAsync(
                    RimSynapseMod.ModHandle, 
                    "LegendaryTributeImage", 
                    imagePrompt, 
                    "RimWorld 2D vector game art style, minimalist flat illustration, simple vector lines, solid white background, game icon", 
                    (texture, path) =>
                    {
                        if (texture == null) return; // Silent fallback if image download fails

                        info.artTexture = texture;
                        info.filePath = path;
                        info.tributeText = tribute;
                        info.isReady = true;

                        if (info.finishedItem != null)
                        {
                            var coreComp = Find.World?.GetComponent<SynapseCoreWorldComponent>();
                            if (coreComp != null)
                            {
                                coreComp.legendaryImagePaths[info.finishedItem.ThingID] = path;
                            }

                            if (info.finishedItem.def.IsArt || info.finishedItem.TryGetComp<CompArt>() != null)
                            {
                                Patch_CompQuality_SetQuality.RecordLegendaryArtWorldEvent(worker, info.finishedItem, tribute);
                            }
                        }

                        if (info.showImmediatelyOnReady)
                        {
                            Find.WindowStack.Add(new Dialog_LegendaryTribute(
                                "Legendary Masterwork: " + itemLabel.CapitalizeFirst(),
                                tribute,
                                texture,
                                path
                            ));
                        }
                    }
                );
            });
        }
    }

    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn), new Type[] { typeof(Pawn), typeof(SkillDef), typeof(bool) })]
    public static class Patch_QualityUtility_GenerateQualityCreatedByPawn
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, SkillDef relevantSkill, bool consumeInspiration, ref QualityCategory __result)
        {
            // If we have a cached pre-rolled quality for this pawn's current job, override the result
            if (Patch_Pawn_Tick_Legendary.preRolledQualities.TryGetValue(pawn, out var info))
            {
                if (pawn.CurJob != null)
                {
                    if (info.jobId == pawn.CurJob.loadID)
                    {
                        __result = info.quality;
                    }
                }
            }
        }
    }

}

