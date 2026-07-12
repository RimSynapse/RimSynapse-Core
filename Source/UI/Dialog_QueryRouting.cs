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
                        listing.Label("  (No specific query types registered. Defaults to Local Only.)", tooltip: "This mod does not support advanced routing yet.");
                    }
                    else
                    {
                        foreach (var kvp in mod.RegisteredQueries)
                        {
                            string queryId = kvp.Key;
                            string queryName = kvp.Value.displayName;
                            LlmCapabilities reqCaps = kvp.Value.requiredCaps;
                            string key = $"{mod.ModId}:{queryId}";

                            if (!settings.queryRouting.TryGetValue(key, out var currentRouting))
                            {
                                currentRouting = ProviderRouting.LocalOnly;
                            }

                            var rowRect = listing.GetRect(30f);
                            var labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width * 0.5f, rowRect.height);
                            var btnRect = new Rect(rowRect.x + rowRect.width * 0.5f, rowRect.y, rowRect.width * 0.5f, rowRect.height);

                            Widgets.Label(labelRect, "  " + queryName);

                            string btnLabel = currentRouting.ToString().Replace("_", " ");
                            if (Widgets.ButtonText(btnRect, btnLabel))
                            {
                                var list = new List<FloatMenuOption>();
                                foreach (ProviderRouting route in System.Enum.GetValues(typeof(ProviderRouting)))
                                {
                                    if (route != ProviderRouting.FirstAvailable)
                                    {
                                        LlmCapabilities providerCaps = LlmCapabilities.None;
                                        switch (route)
                                        {
                                            case ProviderRouting.LocalOnly: providerCaps = settings.capsLocal; break;
                                            case ProviderRouting.Specific_OpenAI: providerCaps = settings.capsOpenAi; break;
                                            case ProviderRouting.Specific_Gemini: providerCaps = settings.capsGemini; break;
                                            case ProviderRouting.Specific_Claude: providerCaps = settings.capsClaude; break;
                                            case ProviderRouting.Specific_Custom: providerCaps = settings.capsCustom; break;
                                        }
                                        if ((providerCaps & reqCaps) != reqCaps)
                                        {
                                            continue; // Skip provider if it lacks required capabilities
                                        }
                                    }

                                    ProviderRouting localRoute = route; // capture
                                    string label = localRoute.ToString().Replace("_", " ");
                                    list.Add(new FloatMenuOption(label, () =>
                                    {
                                        settings.queryRouting[key] = localRoute;
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
                    }
                }
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private float CalculateViewHeight()
        {
            float h = 0f;
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
