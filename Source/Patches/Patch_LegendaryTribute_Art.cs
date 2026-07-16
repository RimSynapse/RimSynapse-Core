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
    /// <summary>
    /// Art description tab patches and quality set interception.
    /// </summary>
    [HarmonyPatch(typeof(CompQuality), nameof(CompQuality.SetQuality))]
    public static class Patch_CompQuality_SetQuality
    {
        [HarmonyPostfix]
        public static void Postfix(CompQuality __instance, QualityCategory q, ArtGenerationContext? source)
        {
            if (q == QualityCategory.Legendary)
            {
                // Find if any pawn had a pre-rolled legendary quality for their current job
                Pawn worker = null;
                PreRolledQualityInfo preRolledInfo = null;

                foreach (var pair in Patch_Pawn_Tick_Legendary.preRolledQualities)
                {
                    if (pair.Value.quality == QualityCategory.Legendary)
                    {
                        if (pair.Value.jobId == pair.Key.CurJob?.loadID)
                        {
                            worker = pair.Key;
                            preRolledInfo = pair.Value;
                            break;
                        }
                    }
                }

                if (worker != null)
                {
                    // Remove from cache now that it's completed
                    Patch_Pawn_Tick_Legendary.preRolledQualities.Remove(worker);

                    if (preRolledInfo.isReady)
                    {
                        var coreComp = Find.World?.GetComponent<SynapseCoreWorldComponent>();
                        if (coreComp != null)
                        {
                            coreComp.legendaryImagePaths[__instance.parent.ThingID] = preRolledInfo.filePath;
                        }

                        if (__instance.parent != null)
                        {
                            if (__instance.parent.def.IsArt || __instance.parent.TryGetComp<CompArt>() != null)
                            {
                                RecordLegendaryArtWorldEvent(worker, __instance.parent, preRolledInfo.tributeText);
                            }
                        }

                        // Show immediately since cache is ready!
                        Find.WindowStack.Add(new Dialog_LegendaryTribute(
                            "Legendary Masterwork: " + __instance.parent.Label.CapitalizeFirst(),
                            preRolledInfo.tributeText,
                            preRolledInfo.artTexture,
                            preRolledInfo.filePath
                        ));
                    }
                    else
                    {
                        preRolledInfo.finishedItem = __instance.parent;
                        // Signal the callback to display as soon as the image/text download finishes
                        preRolledInfo.showImmediatelyOnReady = true;
                    }
                }
            }
        }

        public static void RecordLegendaryArtWorldEvent(Pawn worker, Thing item, string tributeText)
        {
            try
            {
                if (Find.World != null)
                {
                    var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
                    if (coreComp != null)
                    {
                        var ev = new PastEvent
                        {
                            eventId = Guid.NewGuid().ToString(),
                            category = "LegendaryArtCreated",
                            eventDescription = worker.LabelShort + " created a legendary art piece: " + item.Label,
                            gameTick = Find.TickManager.TicksGame,
                            isResolved = true,
                            outcomeDescription = "Tribute: " + (tributeText ?? "A work of legendary beauty has been finished."),
                            outcome = EventOutcome.Triumph
                        };
                        coreComp.EnqueuePastEvent(ev);
                    }
                }
            }
            catch (Exception ex)
            {
                SynapseLogger.Error("Failed to record legendary art world event: " + ex.Message);
            }
        }
    }

    public class Dialog_LegendaryTribute : Window
    {
        public override Vector2 InitialSize => new Vector2(600f, 500f);

        private string title;
        private string tributeText;
        private Texture2D artTexture;
        private string filePath;

        public Dialog_LegendaryTribute(string title, string tributeText, Texture2D artTexture, string filePath)
        {
            this.title = title;
            this.tributeText = tributeText;
            this.artTexture = artTexture;
            this.filePath = filePath;
            this.forcePause = true;
            this.doCloseX = true;
            this.closeOnAccept = true;
            this.closeOnCancel = true;
            this.absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), title);
            Text.Font = GameFont.Small;

            float y = 45f;
            
            // Image
            if (artTexture != null)
            {
                Rect imgRect = new Rect((inRect.width - 256f) / 2f, y, 256f, 256f);
                GUI.DrawTexture(imgRect, artTexture, ScaleMode.ScaleToFit);
                y += 266f;
            }
            else
            {
                Rect fallbackRect = new Rect((inRect.width - 256f) / 2f, y, 256f, 40f);
                Widgets.Label(fallbackRect, "(Image not available)");
                y += 50f;
            }

            // Tribute text
            Rect textRect = new Rect(0f, y, inRect.width, inRect.height - y - 55f);
            Widgets.Label(textRect, tributeText);

            // Close Button
            if (Widgets.ButtonText(new Rect((inRect.width - 120f) / 2f, inRect.height - 45f, 120f, 35f), "Close"))
            {
                Close();
            }
        }
    }

    [HarmonyPatch(typeof(InspectTabBase), "UpdateSize")]
    public static class Patch_ITab_Art_size
    {
        [HarmonyPostfix]
        public static void Postfix(InspectTabBase __instance, ref Vector2 ___size)
        {
            if (__instance is ITab_Art tabArt)
            {
                Thing selected = Traverse.Create(tabArt).Property("SelThing").GetValue<Thing>();
                if (selected != null)
                {
                    var coreComp = Find.World?.GetComponent<SynapseCoreWorldComponent>();
                    if (coreComp != null)
                    {
                        if (coreComp.legendaryImagePaths.ContainsKey(selected.ThingID))
                        {
                            ___size = new Vector2(680f, 300f);
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(ITab_Art), "FillTab")]
    public static class Patch_ITab_Art_FillTab
    {
        private static Dictionary<string, Texture2D> loadedArtTextures = new Dictionary<string, Texture2D>();

        [HarmonyPostfix]
        public static void Postfix(ITab_Art __instance)
        {
            Thing selected = Traverse.Create(__instance).Property("SelThing").GetValue<Thing>();
            if (selected == null) return;

            var coreComp = Find.World?.GetComponent<SynapseCoreWorldComponent>();
            if (coreComp == null) return;

            if (coreComp.legendaryImagePaths.TryGetValue(selected.ThingID, out string path))
            {
                if (!loadedArtTextures.TryGetValue(selected.ThingID, out Texture2D tex))
                {
                    try
                     {
                        if (File.Exists(path))
                        {
                            byte[] bytes = File.ReadAllBytes(path);
                            tex = new Texture2D(2, 2);
                            tex.LoadImage(bytes);
                            loadedArtTextures[selected.ThingID] = tex;
                        }
                    }
                    catch (Exception ex)
                    {
                        SynapseLogger.Error("Failed to load art texture for inspect pane: " + ex.Message);
                    }
                }

                if (tex != null)
                {
                    Rect imgRect = new Rect(420f, 20f, 240f, 240f);
                    GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleToFit);
                }
            }
        }
    }
}
