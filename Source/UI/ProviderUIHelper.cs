using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimSynapse.Internal;

namespace RimSynapse.UI
{
    public static class ProviderUIHelper
    {
        public static void DrawCapabilitiesRow(Listing_Standard listing, ref LlmCapabilities caps, float indent, bool isFrontierModel)
        {
            Rect rect = listing.GetRect(24f);
            rect.xMin += indent;

            float curX = rect.x;
            float spacing = 10f;

            bool hasText = (caps & LlmCapabilities.Text) != 0;
            bool hasImage = (caps & LlmCapabilities.Image) != 0;
            bool hasVision = (caps & LlmCapabilities.Vision) != 0;
            bool hasAudio = (caps & LlmCapabilities.Audio) != 0;

            bool previousEnabled = GUI.enabled;
            if (isFrontierModel)
            {
                GUI.enabled = false;
            }
            
            Vector2 textSz = Text.CalcSize("Text");
            Widgets.Label(new Rect(curX, rect.y, textSz.x, rect.height), "Text");
            Widgets.Checkbox(new Vector2(curX + textSz.x + 4f, rect.y), ref hasText);
            curX += textSz.x + 28f + spacing;

            Vector2 imgSz = Text.CalcSize("Image");
            Widgets.Label(new Rect(curX, rect.y, imgSz.x, rect.height), "Image");
            Widgets.Checkbox(new Vector2(curX + imgSz.x + 4f, rect.y), ref hasImage);
            curX += imgSz.x + 28f + spacing;

            Vector2 visSz = Text.CalcSize("Vision");
            Widgets.Label(new Rect(curX, rect.y, visSz.x, rect.height), "Vision");
            Widgets.Checkbox(new Vector2(curX + visSz.x + 4f, rect.y), ref hasVision);
            curX += visSz.x + 28f + spacing;

            Vector2 audSz = Text.CalcSize("Audio");
            Widgets.Label(new Rect(curX, rect.y, audSz.x, rect.height), "Audio");
            Widgets.Checkbox(new Vector2(curX + audSz.x + 4f, rect.y), ref hasAudio);
            
            GUI.enabled = previousEnabled;

            if (!isFrontierModel)
            {
                caps = LlmCapabilities.None;
                if (hasText) caps |= LlmCapabilities.Text;
                if (hasImage) caps |= LlmCapabilities.Image;
                if (hasVision) caps |= LlmCapabilities.Vision;
                if (hasAudio) caps |= LlmCapabilities.Audio;
            }
        }

        public static void DrawProviderSection(Listing_Standard listing, ref string providerName, ApiProvider? providerEnum, ref string url, ref string key, ref string model, ref LlmCapabilities caps, bool isFrontierModel, System.Action onDelete = null, Dictionary<string, bool> isFetchingModels = null, Dictionary<string, List<string>> fetchedModels = null, Dictionary<string, bool> autoOpenMenu = null)
        {
            if (isFetchingModels == null) isFetchingModels = new Dictionary<string, bool>();
            if (fetchedModels == null) fetchedModels = new Dictionary<string, List<string>>();
            if (autoOpenMenu == null) autoOpenMenu = new Dictionary<string, bool>();

            if (providerName == null) providerName = "Custom Provider";
            Text.Font = GameFont.Medium;
            Rect headerRect = listing.GetRect(30f);
            
            if (providerEnum == ApiProvider.Custom)
            {
                float textWidth = UnityEngine.Mathf.Max(Text.CalcSize(providerName).x + 40f, 200f);
                providerName = Widgets.TextField(new Rect(headerRect.x, headerRect.y, UnityEngine.Mathf.Min(textWidth, headerRect.width - 40f), 28f), providerName);
                if (onDelete != null)
                {
                    Rect trashRect = new Rect(headerRect.x + UnityEngine.Mathf.Min(textWidth, headerRect.width - 40f) + 10f, headerRect.y + 2f, 24f, 24f);
                    if (Widgets.ButtonImage(trashRect, Verse.TexButton.Delete))
                    {
                        onDelete();
                    }
                }
            }
            else
            {
                Widgets.Label(headerRect, providerName);
            }
            Text.Font = GameFont.Small;

            if (providerEnum.HasValue)
            {
                var settings = RimSynapseMod.Instance.Settings;
                int p = 0, c = 0;
                switch(providerEnum.Value) {
                    case ApiProvider.Local_LMStudio: p = settings.tokensPromptLocal; c = settings.tokensCompletionLocal; break;
                    case ApiProvider.OpenAI: p = settings.tokensPromptOpenAi; c = settings.tokensCompletionOpenAi; break;
                    case ApiProvider.Google_Gemini: p = settings.tokensPromptGemini; c = settings.tokensCompletionGemini; break;
                    case ApiProvider.Anthropic_Claude: p = settings.tokensPromptClaude; c = settings.tokensCompletionClaude; break;
                    case ApiProvider.Custom: p = settings.tokensPromptCustom; c = settings.tokensCompletionCustom; break;
                }

                if (p > 0 || c > 0)
                {
                    string usageText = "Usage: " + p + " prompt / " + c + " comp tokens";
                    var oldColor = UnityEngine.GUI.color;
                    UnityEngine.GUI.color = UnityEngine.Color.gray;
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(headerRect, usageText);
                    UnityEngine.GUI.color = oldColor;
                    Text.Anchor = TextAnchor.UpperLeft;
                }
            }
            
            float indent = 20f;
            float labelWidth = 70f;
            
            bool previousEnabled = GUI.enabled;
            
            Rect urlRect = listing.GetRect(24f);
            urlRect.xMin += indent;
            Widgets.Label(new Rect(urlRect.x, urlRect.y, labelWidth, urlRect.height), "Endpoint:");
            
            if (isFrontierModel) GUI.enabled = false;
            if (url == null) url = "";
            url = Widgets.TextField(new Rect(urlRect.x + labelWidth, urlRect.y, urlRect.width - labelWidth, urlRect.height), url);
            GUI.enabled = previousEnabled;
            
            listing.Gap(4f);
            
            if (providerName != "Pollinations.ai")
            {
                Rect modelRect = listing.GetRect(24f);
                modelRect.xMin += indent;
                Widgets.Label(new Rect(modelRect.x, modelRect.y, labelWidth, modelRect.height), "Model:");
                
                float revertBtnWidth = 60f;
                bool isCustom = providerEnum == ApiProvider.Custom;
                float fetchBtnWidth = isCustom ? 0f : 24f;
                Rect fetchBtnRect = new Rect(modelRect.x + labelWidth, modelRect.y, fetchBtnWidth, modelRect.height);
                Rect modelFieldRect = new Rect(fetchBtnRect.xMax + (isCustom ? 0f : 4f), modelRect.y, modelRect.width - labelWidth - fetchBtnWidth - revertBtnWidth - (isCustom ? 4f : 8f), modelRect.height);
                Rect revertBtnRect = new Rect(modelRect.xMax - revertBtnWidth, modelRect.y, revertBtnWidth, modelRect.height);
                
                bool isFetching = isFetchingModels.ContainsKey(providerName) && isFetchingModels[providerName];
                bool hasFetched = fetchedModels.ContainsKey(providerName) && fetchedModels[providerName] != null;

                if (autoOpenMenu.ContainsKey(providerName) && autoOpenMenu[providerName] && hasFetched)
                {
                    autoOpenMenu[providerName] = false;
                    var list = new List<FloatMenuOption>();
                    foreach (var m in fetchedModels[providerName])
                    {
                        string localM = m;
                        string pName = providerName;
                        list.Add(new FloatMenuOption(localM, () => {
                            var settings = RimSynapseMod.Instance.Settings;
                            if (pName == "OpenAI") settings.modelOpenAi = localM;
                            else if (pName == "Google Gemini") settings.modelGemini = localM;
                            else if (pName == "Anthropic Claude") settings.modelClaude = localM;
                            else if (pName == "Local LM Studio") settings.modelLocal = localM;
                            else if (pName == "Custom / Proxy" || pName == "Custom Provider") settings.modelCustom = localM;
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(list));
                }

                if (!isCustom)
                {
                    if (isFetching)
                    {
                        bool previousEnabledFetch = GUI.enabled;
                        GUI.enabled = false;
                        Widgets.ButtonText(fetchBtnRect, "...");
                        GUI.enabled = previousEnabledFetch;
                    }
                    else
                    {
                        if (Widgets.ButtonText(fetchBtnRect, "?"))
                        {
                            if (hasFetched)
                            {
                                autoOpenMenu[providerName] = true;
                            }
                            else
                            {
                                isFetchingModels[providerName] = true;
                                string fetchUrl = url;
                                string fetchKey = key;
                                string pName = providerName;
                                if (providerEnum.HasValue)
                                {
                                    RimSynapse.Internal.HttpEngine.FetchProviderModelsAsync(providerEnum.Value, fetchUrl, fetchKey, (ok, modelsList, msg) =>
                                    {
                                        RimSynapse.SynapseGameComponent.Enqueue(() => {
                                            isFetchingModels[pName] = false;
                                            if (ok)
                                            {
                                                fetchedModels[pName] = modelsList;
                                                autoOpenMenu[pName] = true;
                                            }
                                            else
                                            {
                                                Verse.Log.Error($"[RimSynapse] Failed to fetch models for {pName}: {msg}");
                                            }
                                        });
                                    });
                                }
                            }
                        }
                    }
                }
                
                if (model == null) model = "";
                model = Widgets.TextField(modelFieldRect, model);
                
                if (Widgets.ButtonText(revertBtnRect, "Revert"))
                {
                    if (providerName == "OpenAI") model = "gpt-4o-mini";
                    else if (providerName == "Google Gemini") model = "gemini-1.5-flash";
                    else if (providerName == "Anthropic Claude") model = "claude-3-5-haiku-latest";
                    else if (providerName == "Local LM Studio") model = RimSynapse.Internal.ModelManager.ActiveModel ?? "local-model";
                    else if (isCustom) model = "";
                }
                
                listing.Gap(4f);
            }
            
            Rect keyRect = listing.GetRect(24f);
            keyRect.xMin += indent;
            Widgets.Label(new Rect(keyRect.x, keyRect.y, labelWidth, keyRect.height), "Key:");
            
            Rect keyTextFieldRect = new Rect(keyRect.x + labelWidth, keyRect.y, keyRect.width - labelWidth, keyRect.height);
            
            if (key == null) key = "";
            key = Widgets.TextField(keyTextFieldRect, key);
            
            if (string.IsNullOrEmpty(key) )
            {
                Color oldColor = GUI.color;
                GUI.color = Color.grey;
                Widgets.Label(new Rect(keyTextFieldRect.x + 4f, keyTextFieldRect.y + 2f, keyTextFieldRect.width, keyTextFieldRect.height), "(enter api key here)");
                GUI.color = oldColor;
            }
            
            listing.Gap(4f);
            
            DrawCapabilitiesRow(listing, ref caps, indent, isFrontierModel);
        }
    }
}




