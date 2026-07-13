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

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            SynapseGameComponent.ProcessMainThreadQueue();
        }

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

            DrawDefaultRoutingDropdown(listing, "Text Queries", settings.defaultRoutingText, v => settings.defaultRoutingText = v, LlmCapabilities.Text, "default_text");
            listing.Gap(6f);
            DrawDefaultRoutingDropdown(listing, "Vision Queries", settings.defaultRoutingVision, v => settings.defaultRoutingVision = v, LlmCapabilities.Vision, "default_vision");
            listing.Gap(6f);
            DrawDefaultRoutingDropdown(listing, "Image Generation", settings.defaultRoutingImage, v => settings.defaultRoutingImage = v, LlmCapabilities.Image, "default_image");
            listing.Gap(6f);
            DrawDefaultRoutingDropdown(listing, "Audio Generation", settings.defaultRoutingAudio, v => settings.defaultRoutingAudio = v, LlmCapabilities.Audio, "default_audio");
            listing.Gap(6f);
            
            listing.Gap(24f);

            // --- Registered Queries ---
            listing.Label("Registered Queries", tooltip: "List of all queries registered by companion mods and their required capabilities.");
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
                            var rowRect = listing.GetRect(30f);
                            var labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width * 0.5f, rowRect.height);
                            var typeRect = new Rect(rowRect.x + rowRect.width * 0.5f, rowRect.y, rowRect.width * 0.5f, rowRect.height);

                            Widgets.Label(labelRect, "  " + queryName);

                            string capType = "Text Model";
                            if ((reqCaps & LlmCapabilities.Image) == LlmCapabilities.Image) capType = "Image Model";
                            else if ((reqCaps & LlmCapabilities.Vision) == LlmCapabilities.Vision) capType = "Vision Model";
                            else if ((reqCaps & LlmCapabilities.Audio) == LlmCapabilities.Audio) capType = "Audio Model";
                            
                            Color oldColor = GUI.color;
                            GUI.color = Color.grey;
                            Widgets.Label(typeRect, "Uses: " + capType);
                            GUI.color = oldColor;

                            listing.Gap(2f);
                        }
                    }
                }
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawDefaultRoutingDropdown(Listing_Standard listing, string labelText, string currentVal, System.Action<string> setter, LlmCapabilities reqCaps, string modelKey)
        {
            var settings = RimSynapseMod.Instance.Settings;
            var rowRect = listing.GetRect(30f);
            var labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width * 0.35f, rowRect.height);
            var btnRect = new Rect(rowRect.x + rowRect.width * 0.35f + 4f, rowRect.y, rowRect.width * 0.30f - 8f, rowRect.height);
            var modelRect = new Rect(rowRect.x + rowRect.width * 0.65f, rowRect.y, rowRect.width * 0.35f, rowRect.height);

            Widgets.Label(labelRect, "  " + labelText);

            string btnLabel = GetRoutingName(currentVal);
            if (Widgets.ButtonText(btnRect, btnLabel))
            {
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

            DrawModelFieldWithLookup(modelRect, currentVal, modelKey, false, reqCaps);
        }

        private static Dictionary<string, string> pendingModelSelections = new Dictionary<string, string>();

        private void DrawModelFieldWithLookup(Rect modelRect, string currentVal, string modelKey, bool isDefaultRoute, LlmCapabilities reqCaps = LlmCapabilities.None)
        {
            var settings = RimSynapseMod.Instance.Settings;

            string placeholder = "default model";

            if (isDefaultRoute)
            {
                GUI.enabled = false;
                Widgets.TextField(modelRect, "");
                Color oldColor = GUI.color;
                GUI.color = Color.grey;
                Widgets.Label(new Rect(modelRect.x + 4f, modelRect.y + 2f, modelRect.width, modelRect.height), placeholder);
                GUI.color = oldColor;
                GUI.enabled = true;
                return;
            }
            
            float selectBtnWidth = 80f;
            Rect modelFieldRect = new Rect(modelRect.x, modelRect.y, modelRect.width - selectBtnWidth - 4f, modelRect.height);
            Rect selectBtnRect = new Rect(modelRect.xMax - selectBtnWidth, modelRect.y, selectBtnWidth, modelRect.height);
            
            if (pendingModelSelections.TryGetValue(modelKey, out string newM))
            {
                settings.queryRoutingModels[modelKey] = newM;
                RimSynapseMod.Instance.WriteSettings();
                pendingModelSelections.Remove(modelKey);
            }

            string modelVal = "";
            if (settings.queryRoutingModels.TryGetValue(modelKey, out var mv)) modelVal = mv;
            
            string newModelVal = Widgets.TextField(modelFieldRect, modelVal);
            if (newModelVal != modelVal)
            {
                settings.queryRoutingModels[modelKey] = newModelVal;
                RimSynapseMod.Instance.WriteSettings();
            }

            if (string.IsNullOrEmpty(newModelVal))
            {
                Color oldColor = GUI.color;
                GUI.color = Color.grey;
                Widgets.Label(new Rect(modelFieldRect.x + 4f, modelFieldRect.y + 2f, modelFieldRect.width, modelFieldRect.height), placeholder);
                GUI.color = oldColor;
            }
            
            if (Widgets.ButtonText(selectBtnRect, "Select..."))
            {
                ApiProvider? pEnum = null;
                if (currentVal == RoutingId.LocalOnly) pEnum = ApiProvider.Local_LMStudio;
                else if (currentVal == RoutingId.OpenAI) pEnum = ApiProvider.OpenAI;
                else if (currentVal == RoutingId.Gemini) pEnum = ApiProvider.Google_Gemini;
                else if (currentVal == RoutingId.Claude) pEnum = ApiProvider.Anthropic_Claude;
                else if (currentVal == RoutingId.Pollinations) pEnum = ApiProvider.Pollinations;
                else if (currentVal == RoutingId.ElevenLabs) pEnum = ApiProvider.ElevenLabs;
                else if (currentVal != null && currentVal.StartsWith(RoutingId.CustomPrefix)) pEnum = ApiProvider.Custom;
                
                if (pEnum.HasValue)
                {
                    RimSynapse.Internal.ModelDefUtility.ShowModelSelector(pEnum.Value, reqCaps, (selectedModel) => {
                        pendingModelSelections[modelKey] = selectedModel;
                    });
                }
            }
        }

        private string GetDefaultModelForRoute(string routeId)
        {
            var settings = RimSynapseMod.Instance.Settings;
            if (string.IsNullOrEmpty(routeId) || routeId == "Default") return "";
            if (routeId == RoutingId.LocalOnly) return settings.modelLocal;
            if (routeId == RoutingId.OpenAI) return settings.modelOpenAi;
            if (routeId == RoutingId.Gemini) return settings.modelGemini;
            if (routeId == RoutingId.Claude) return settings.modelClaude;
            if (routeId == RoutingId.Pollinations) return settings.modelPollinations;
            if (routeId == RoutingId.ElevenLabs) return settings.modelElevenLabs;
            if (routeId.StartsWith(RoutingId.CustomPrefix))
            {
                string customId = routeId.Substring(RoutingId.CustomPrefix.Length);
                var custom = settings.customProviders.Find(c => c.id == customId);
                if (custom != null) return custom.model;
            }
            return "";
        }

        private string GetRoutingName(string id)
        {
            if (string.IsNullOrEmpty(id) || id == "Default") return "Default";
            if (id == RoutingId.LocalOnly) return "Local LM Studio";
            if (id == RoutingId.OpenAI) return "OpenAI";
            if (id == RoutingId.Gemini) return "Google Gemini";
            if (id == RoutingId.Claude) return "Anthropic Claude";
            if (id == RoutingId.ElevenLabs) return "ElevenLabs";
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
            if ((reqCaps & LlmCapabilities.Image) == reqCaps) routes[RoutingId.Pollinations] = "Pollinations.ai";
            
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
            float h = 250f; // Capability Defaults section
            foreach (var mod in Internal.ModRegistry.All)
            {
                h += 12f + 30f + 12f; // Gap, Title, Line
                if (mod.RegisteredQueries.Count == 0) h += 36f;
                else h += (mod.RegisteredQueries.Count * 32f); // 30f row + 2f gap
            }
            return Mathf.Max(h, 500f);
        }
    }
}
