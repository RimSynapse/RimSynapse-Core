using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimSynapse.UI
{
    public class Dialog_QueryRouting : Window
    {
        private Vector2 scrollPosition;

        private Dictionary<string, bool> isFetchingModels = new Dictionary<string, bool>();
        private Dictionary<string, List<string>> fetchedModels = new Dictionary<string, List<string>>();
        private Dictionary<string, bool> autoOpenMenu = new Dictionary<string, bool>();

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
                            var labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width * 0.35f, rowRect.height);
                            var btnRect = new Rect(rowRect.x + rowRect.width * 0.35f + 4f, rowRect.y, rowRect.width * 0.30f - 8f, rowRect.height);
                            var modelRect = new Rect(rowRect.x + rowRect.width * 0.65f, rowRect.y, rowRect.width * 0.35f, rowRect.height);

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

                            DrawModelFieldWithLookup(modelRect, currentRouting, key, currentRouting == "Default");

                            listing.Gap(6f);
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

            DrawModelFieldWithLookup(modelRect, currentVal, modelKey, false);
        }

        private void DrawModelFieldWithLookup(Rect modelRect, string currentVal, string modelKey, bool isDefaultRoute)
        {
            var settings = RimSynapseMod.Instance.Settings;

            string placeholder = GetActiveModelForKey(modelKey, currentVal);
            if (string.IsNullOrEmpty(placeholder)) placeholder = "default model";
            else placeholder = $"({placeholder})";

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

            // Determine provider details
            string pName = null;
            ApiProvider? pEnum = null;
            string pUrl = null;
            string pKey = null;

            if (currentVal == RoutingId.LocalOnly) { pName = "Local LM Studio"; pEnum = ApiProvider.Local_LMStudio; pUrl = settings.lmStudioUrl; pKey = settings.lmStudioApiKey; }
            else if (currentVal == RoutingId.OpenAI) { pName = "OpenAI"; pEnum = ApiProvider.OpenAI; pUrl = settings.openAiUrl; pKey = settings.openAiApiKey; }
            else if (currentVal == RoutingId.Gemini) { pName = "Google Gemini"; pEnum = ApiProvider.Google_Gemini; pUrl = settings.geminiUrl; pKey = settings.geminiApiKey; }
            else if (currentVal == RoutingId.Claude) { pName = "Anthropic Claude"; pEnum = ApiProvider.Anthropic_Claude; pUrl = settings.claudeUrl; pKey = settings.claudeApiKey; }
            else if (currentVal == RoutingId.Pollinations) { pName = "Pollinations.ai"; pEnum = ApiProvider.Pollinations; pUrl = "https://image.pollinations.ai/prompt"; pKey = ""; }
            else if (currentVal != null && currentVal.StartsWith(RoutingId.CustomPrefix))
            {
                string customId = currentVal.Substring(RoutingId.CustomPrefix.Length);
                var custom = settings.customProviders.Find(c => c.id == customId);
                if (custom != null) { pName = custom.name; pEnum = ApiProvider.Custom; pUrl = custom.url; pKey = custom.apiKey; }
            }

            bool showFetch = pEnum.HasValue;
            float fetchWidth = showFetch ? 24f : 0f;
            Rect btnFetchRect = new Rect(modelRect.x, modelRect.y, fetchWidth, modelRect.height);
            Rect textRect = new Rect(modelRect.x + fetchWidth + (showFetch ? 4f : 0f), modelRect.y, modelRect.width - fetchWidth - (showFetch ? 4f : 0f), modelRect.height);

            if (showFetch && pName != null)
            {
                bool isFetching = isFetchingModels.ContainsKey(pName) && isFetchingModels[pName];
                bool hasFetched = fetchedModels.ContainsKey(pName) && fetchedModels[pName] != null;

                if (autoOpenMenu.ContainsKey(pName) && autoOpenMenu[pName] && hasFetched)
                {
                    autoOpenMenu[pName] = false;
                    var list = new List<FloatMenuOption>();
                    foreach (var m in fetchedModels[pName])
                    {
                        string localM = m;
                        string keyToSet = modelKey;
                        list.Add(new FloatMenuOption(localM, () => {
                            settings.queryRoutingModels[keyToSet] = localM;
                            RimSynapseMod.Instance.WriteSettings();
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(list));
                }

                if (isFetching)
                {
                    bool previousEnabledFetch = GUI.enabled;
                    GUI.enabled = false;
                    Widgets.ButtonText(btnFetchRect, "...");
                    GUI.enabled = previousEnabledFetch;
                }
                else
                {
                    if (Widgets.ButtonText(btnFetchRect, "..."))
                    {
                        if (hasFetched)
                        {
                            autoOpenMenu[pName] = true;
                        }
                        else
                        {
                            isFetchingModels[pName] = true;
                            string fetchUrl = pUrl;
                            string fetchKey = pKey;
                            string fetchPName = pName;
                            ApiProvider fetchPEnum = pEnum.Value;
                            RimSynapse.Internal.HttpEngine.FetchProviderModelsAsync(fetchPEnum, fetchUrl, fetchKey, (ok, modelsList, msg) =>
                            {
                                RimSynapse.SynapseGameComponent.Enqueue(() => {
                                    isFetchingModels[fetchPName] = false;
                                    if (ok)
                                    {
                                        fetchedModels[fetchPName] = modelsList;
                                        autoOpenMenu[fetchPName] = true;
                                    }
                                    else
                                    {
                                        Verse.Log.Error($"[RimSynapse] Failed to fetch models for {fetchPName}: {msg}");
                                    }
                                });
                            });
                        }
                    }
                }
            }

            string modelVal = "";
            if (settings.queryRoutingModels.TryGetValue(modelKey, out var mv)) modelVal = mv;
            
            string newModelVal = Widgets.TextField(textRect, modelVal);
            if (newModelVal != modelVal)
            {
                settings.queryRoutingModels[modelKey] = newModelVal;
                RimSynapseMod.Instance.WriteSettings();
            }

            if (string.IsNullOrEmpty(newModelVal))
            {
                Color oldColor = GUI.color;
                GUI.color = Color.grey;
                Widgets.Label(new Rect(textRect.x + 4f, textRect.y + 2f, textRect.width, textRect.height), placeholder);
                GUI.color = oldColor;
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
            if (routeId.StartsWith(RoutingId.CustomPrefix))
            {
                string customId = routeId.Substring(RoutingId.CustomPrefix.Length);
                var custom = settings.customProviders.Find(c => c.id == customId);
                if (custom != null) return custom.model;
            }
            return "";
        }

        private string GetInheritedRoute(string modelKey)
        {
            var settings = RimSynapseMod.Instance.Settings;
            if (modelKey.Contains(":"))
            {
                string[] parts = modelKey.Split(':');
                if (parts.Length == 2)
                {
                    string modId = parts[0];
                    string queryId = parts[1];
                    var mod = Internal.ModRegistry.All.FirstOrDefault(m => m.ModId == modId);
                    if (mod != null && mod.RegisteredQueries.TryGetValue(queryId, out var queryDef))
                    {
                        var reqCaps = queryDef.requiredCaps;
                        if ((reqCaps & LlmCapabilities.Image) == LlmCapabilities.Image) return settings.defaultRoutingImage;
                        if ((reqCaps & LlmCapabilities.Vision) == LlmCapabilities.Vision) return settings.defaultRoutingVision;
                        if ((reqCaps & LlmCapabilities.Audio) == LlmCapabilities.Audio) return settings.defaultRoutingAudio;
                        return settings.defaultRoutingText;
                    }
                }
            }
            return RoutingId.LocalOnly;
        }

        private string GetActiveModelForKey(string modelKey, string currentRoute)
        {
            var settings = RimSynapseMod.Instance.Settings;
            
            if (currentRoute == "Default")
            {
                string inheritedRoute = GetInheritedRoute(modelKey);
                string capKey = "default_text";
                if (modelKey.Contains(":"))
                {
                    string[] parts = modelKey.Split(':');
                    if (parts.Length == 2)
                    {
                        var mod = Internal.ModRegistry.All.FirstOrDefault(m => m.ModId == parts[0]);
                        if (mod != null && mod.RegisteredQueries.TryGetValue(parts[1], out var qDef))
                        {
                            var reqCaps = qDef.requiredCaps;
                            if ((reqCaps & LlmCapabilities.Image) == LlmCapabilities.Image) capKey = "default_image";
                            else if ((reqCaps & LlmCapabilities.Vision) == LlmCapabilities.Vision) capKey = "default_vision";
                            else if ((reqCaps & LlmCapabilities.Audio) == LlmCapabilities.Audio) capKey = "default_audio";
                        }
                    }
                }
                
                if (settings.queryRoutingModels.TryGetValue(capKey, out var capModel) && !string.IsNullOrEmpty(capModel))
                {
                    return capModel;
                }
                return GetDefaultModelForRoute(inheritedRoute);
            }
            
            return GetDefaultModelForRoute(currentRoute);
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
                else h += (mod.RegisteredQueries.Count * 36f); // 30f row + 6f gap
            }
            return Mathf.Max(h, 500f);
        }
    }
}
