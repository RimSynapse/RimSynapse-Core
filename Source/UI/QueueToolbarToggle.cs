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

        private static Texture2D GenerateIconFromMask(string[] mask)
        {
            int size = 24;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            var body = new Color32(150, 150, 150, 255);
            var black = new Color32(0, 0, 0, 255);

            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            bool[,] isBody = new bool[size, size];
            for (int y = 0; y < size; y++)
            {
                int texY = size - 1 - y;
                if (y < mask.Length)
                {
                    for (int x = 0; x < size && x < mask[y].Length; x++)
                    {
                        if (mask[y][x] != ' ') isBody[x, texY] = true;
                    }
                }
            }

            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    if (isBody[x, y])
                    {
                        pixels[y * size + x] = body;
                    }
                    else
                    {
                        bool nearBody = false;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                                {
                                    if (isBody[nx, ny]) nearBody = true;
                                }
                            }
                        }
                        if (nearBody) pixels[y * size + x] = black;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D GenerateIconForPatch()
        {
            string[] aiMask = new string[]
            {
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "      xxxx      xxxx    ",
                "     xxxxxx     xxxx    ",
                "    xx    xx     xx     ",
                "    xx    xx     xx     ",
                "   xx      xx    xx     ",
                "   xx      xx    xx     ",
                "   xxxxxxxxxx    xx     ",
                "   xxxxxxxxxx    xx     ",
                "   xx      xx    xx     ",
                "   xx      xx    xx     ",
                "   xx      xx    xx     ",
                "   xx      xx   xxxx    ",
                "   xx      xx   xxxx    ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        "
            };
            return GenerateIconFromMask(aiMask);
        }
    }
}
