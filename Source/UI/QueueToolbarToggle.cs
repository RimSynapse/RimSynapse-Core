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
            
            if (RimSynapseMod.Instance?.Settings?.showQueueMonitorIcon == false) return;

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

            // Draw an "AI" icon
            // Background box
            for (int y = 4; y <= 20; y++)
                for (int x = 4; x <= 20; x++)
                    pixels[y * size + x] = body;

            // Black letters for 'AI'
            var black = new Color32(0, 0, 0, 255);
            
            // Draw 'A' (x: 6-11, y: 7-17)
            // Left leg
            for(int y=7; y<=17; y++) pixels[y * size + 6] = black;
            for(int y=7; y<=17; y++) pixels[y * size + 7] = black;
            // Right leg
            for(int y=7; y<=17; y++) pixels[y * size + 10] = black;
            for(int y=7; y<=17; y++) pixels[y * size + 11] = black;
            // Top bar
            pixels[16 * size + 8] = black; pixels[16 * size + 9] = black;
            pixels[17 * size + 8] = black; pixels[17 * size + 9] = black;
            // Middle bar
            pixels[12 * size + 8] = black; pixels[12 * size + 9] = black;
            pixels[11 * size + 8] = black; pixels[11 * size + 9] = black;

            // Draw 'I' (x: 14-17, y: 7-17)
            // Top bar
            pixels[16 * size + 14] = black; pixels[16 * size + 15] = black; pixels[16 * size + 16] = black; pixels[16 * size + 17] = black;
            pixels[17 * size + 14] = black; pixels[17 * size + 15] = black; pixels[17 * size + 16] = black; pixels[17 * size + 17] = black;
            // Bottom bar
            pixels[7 * size + 14] = black; pixels[7 * size + 15] = black; pixels[7 * size + 16] = black; pixels[7 * size + 17] = black;
            pixels[8 * size + 14] = black; pixels[8 * size + 15] = black; pixels[8 * size + 16] = black; pixels[8 * size + 17] = black;
            // Stem
            for(int y=9; y<=15; y++) pixels[y * size + 15] = black;
            for(int y=9; y<=15; y++) pixels[y * size + 16] = black;

            // Cyan AI dot in bottom right
            pixels[17 * size + 18] = cyan;
            pixels[17 * size + 19] = cyan;
            pixels[18 * size + 18] = cyan;
            pixels[18 * size + 19] = cyan;

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
