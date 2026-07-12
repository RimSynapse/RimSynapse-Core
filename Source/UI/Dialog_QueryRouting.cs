using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimSynapse.UI
{
    public class Dialog_QueryRouting : Window
    {
        private Vector2 scrollPosition;

        public Dialog_QueryRouting()
        {
            this.forcePause = true;
            this.doCloseX = true;
            this.doCloseButton = true;
            this.closeOnClickedOutside = true;
            this.absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(600f, 700f);

        public override void DoWindowContents(Rect inRect)
        {
            var settings = RimSynapseMod.Instance.Settings;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "LLM Query Routing");
            Text.Font = GameFont.Small;

            var outRect = new Rect(0, 40f, inRect.width, inRect.height - 90f);
            var viewRect = new Rect(0, 0, inRect.width - 16f, CalculateViewHeight());

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // --- Capability Defaults ---
            listing.Label("Capability Defaults", tooltip: "Default provider for unrouted queries needing specific capabilities.");
            listing.GapLine();

            DrawDefaultRoutingDropdown(listing, "Text Queries", settings.defaultRoutingText, v => settings.defaultRoutingText = v, LlmCapabilities.Text);
            DrawDefaultRoutingDropdown(listing, "Vision Queries", settings.defaultRoutingVision, v => settings.defaultRoutingVision = v, LlmCapabilities.Vision);
            DrawDefaultRoutingDropdown(listing, "Image Generation", settings.defaultRoutingImage, v => settings.defaultRoutingImage = v, LlmCapabilities.Image);
            DrawDefaultRoutingDropdown(listing, "Audio Generation", settings.defaultRoutingAudio, v => settings.defaultRoutingAudio = v, LlmCapabilities.Audio);
            
            listing.Gap(24f);

            // --- Specific Overrides ---
            listing.Label("Specific Overrides", tooltip: "Override the default routing for specific queries from companion mods.");
            listing.GapLine();

            if (Internal.ModRegistry.All.Count == 0)
            {
                listing.Label("No companion mods are currently registered.");
            }
            else
            {
                foreach (var mod in Internal.ModRegistry.All.OrderBy(m => m.DisplayName))
                {
                    listing.Gap(12f);
                    Text.Font = GameFont.Medium;
                    listing.Label(mod.DisplayName);
                    Text.Font = GameFont.Small;
                    listing.GapLine();

                    if (mod.RegisteredQueries.Count == 0)
                    {
                        listing.Label("  (No specific query types registered.)", tooltip: "This mod does not support advanced routing yet.");
                    }
                    else
                    {
                        foreach (var kvp in mod.RegisteredQueries)
                        {
                            string queryId = kvp.Key;
                            string queryName = kvp.Value.displayName;
                            LlmCapabilities reqCaps = kvp.Value.requiredCaps;
                            string key = $"{mod.ModId}:{queryId}";

                            if (!settings.queryRoutingIds.TryGetValue(key, out var currentRouting))
                            {
                                currentRouting = "Default";
                            }

                            var rowRect = listing.GetRect(30f);
                            var labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width * 0.5f, rowRect.height);
                            var btnRect = new Rect(rowRect.x + rowRect.width * 0.5f, rowRect.y, rowRect.width * 0.5f, rowRect.height);

                            Widgets.Label(labelRect, "  " + queryName);

                            string btnLabel = GetRoutingName(currentRouting);
                            if (Widgets.ButtonText(btnRect, btnLabel))
                            {
                                var list = new List<FloatMenuOption>();
                                list.Add(new FloatMenuOption("Default", () => { settings.queryRoutingIds.Remove(key); RimSynapseMod.Instance.WriteSettings(); }));
                                
                                foreach (var route in GetCapableRoutes(reqCaps, settings))
                                {
                                    string localRoute = route.Key;
                                    string label = route.Value;
                                    list.Add(new FloatMenuOption(label, () =>
                                    {
                                        settings.queryRoutingIds[key] = localRoute;
                                        RimSynapseMod.Instance.WriteSettings();
                                    }));
                                }
                                
                                if (list.Count == 1) // only "Default"
                                {
                                    list.Add(new FloatMenuOption("No capable providers found", null));
                                }
                                Find.WindowStack.Add(new FloatMenu(list));
                            }
                        }
                    }
                }
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawDefaultRoutingDropdown(Listing_Standard listing, string labelText, string currentVal, System.Action<string> setter, LlmCapabilities reqCaps)
        {
            var rowRect = listing.GetRect(30f);
            var labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width * 0.5f, rowRect.height);
            var btnRect = new Rect(rowRect.x + rowRect.width * 0.5f, rowRect.y, rowRect.width * 0.5f, rowRect.height);

            Widgets.Label(labelRect, "  " + labelText);

            string btnLabel = GetRoutingName(currentVal);
            if (Widgets.ButtonText(btnRect, btnLabel))
            {
                var settings = RimSynapseMod.Instance.Settings;
                var list = new List<FloatMenuOption>();
                foreach (var route in GetCapableRoutes(reqCaps, settings))
                {
                    string localRoute = route.Key;
                    string optionLabel = route.Value;
                    list.Add(new FloatMenuOption(optionLabel, () =>
                    {
                        setter(localRoute);
                        RimSynapseMod.Instance.WriteSettings();
                    }));
                }
                
                if (list.Count == 0)
                {
                    list.Add(new FloatMenuOption("No capable providers found", null));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }
        }

        private string GetRoutingName(string id)
        {
            if (string.IsNullOrEmpty(id) || id == "Default") return "Default";
            if (id == RoutingId.LocalOnly) return "Local LM Studio";
            if (id == RoutingId.OpenAI) return "OpenAI";
            if (id == RoutingId.Gemini) return "Google Gemini";
            if (id == RoutingId.Claude) return "Anthropic Claude";
            if (id.StartsWith(RoutingId.CustomPrefix))
            {
                string customId = id.Substring(RoutingId.CustomPrefix.Length);
                var custom = RimSynapseMod.Instance.Settings.customProviders.Find(c => c.id == customId);
                if (custom != null) return $"Custom: {custom.name}";
                return "Custom (Unknown)";
            }
            return id;
        }

        private Dictionary<string, string> GetCapableRoutes(LlmCapabilities reqCaps, RimSynapseSettings settings)
        {
            var routes = new Dictionary<string, string>();
            if ((settings.capsLocal & reqCaps) == reqCaps) routes[RoutingId.LocalOnly] = "Local LM Studio";
            if ((settings.capsOpenAi & reqCaps) == reqCaps) routes[RoutingId.OpenAI] = "OpenAI";
            if ((settings.capsGemini & reqCaps) == reqCaps) routes[RoutingId.Gemini] = "Google Gemini";
            if ((settings.capsClaude & reqCaps) == reqCaps) routes[RoutingId.Claude] = "Anthropic Claude";
            
            foreach (var custom in settings.customProviders)
            {
                if ((custom.caps & reqCaps) == reqCaps)
                {
                    routes[RoutingId.CustomPrefix + custom.id] = $"Custom: {custom.name}";
                }
            }
            return routes;
        }

        private float CalculateViewHeight()
        {
            float h = 200f; // Capability Defaults section
            foreach (var mod in Internal.ModRegistry.All)
            {
                h += 12f + 30f + 12f; // Gap, Title, Line
                if (mod.RegisteredQueries.Count == 0) h += 24f;
                else h += (mod.RegisteredQueries.Count * 30f);
            }
            return Mathf.Max(h, 500f);
        }
    }
}
