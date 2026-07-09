using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using RimSynapse.UI;

namespace RimSynapse.UI
{
    /// <summary>
    /// Harmony postfix on PlaySettings.DoPlaySettingsGlobalControls.
    /// Adds our LLM Queue toggle icon to the toolbar row.
    /// </summary>
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    [StaticConstructorOnStartup]
    internal static class QueueToolbarToggle
    {
        private static readonly Texture2D _cachedIcon = GenerateIconForPatch();

        [HarmonyPostfix]
        static void Postfix(WidgetRow row, bool worldView)
        {
            // Only show in colony view
            if (worldView || row == null) return;

            bool isOpen = Find.WindowStack.IsOpen<Dialog_QueueMonitor>();
            bool wasOpen = isOpen;

            row.ToggleableIcon(ref isOpen, _cachedIcon,
                "Toggle RimSynapse LLM Queue Monitor\n\n" +
                "Shows real-time status of pending LLM requests.",
                SoundDefOf.Mouseover_ButtonToggle);

            if (isOpen != wasOpen)
            {
                if (isOpen)
                {
                    Find.WindowStack.Add(new Dialog_QueueMonitor());
                }
                else
                {
                    var window = Find.WindowStack.WindowOfType<Dialog_QueueMonitor>();
                    if (window != null)
                    {
                        window.Close();
                    }
                }
            }
        }

        private static Texture2D GenerateIconForPatch()
        {
            const int size = 24;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;

            var body = new Color32(220, 220, 220, 255);
            var cyan = new Color32(0, 255, 255, 255);
            var clear = new Color32(0, 0, 0, 0);

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            // Draw a tiny "list" icon with a glowing cyan dot (representing AI thinking)
            // Document background
            for (int y = 4; y <= 20; y++)
                for (int x = 6; x <= 18; x++)
                    pixels[y * size + x] = body;

            // Lines on document
            var lineC = new Color32(100, 100, 100, 255);
            for (int y = 7; y <= 17; y += 3)
            {
                for (int x = 8; x <= 16; x++)
                    pixels[y * size + x] = lineC;
            }

            // Cyan AI dot in bottom right of doc
            pixels[17 * size + 15] = cyan;
            pixels[17 * size + 16] = cyan;
            pixels[18 * size + 15] = cyan;
            pixels[18 * size + 16] = cyan;

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
