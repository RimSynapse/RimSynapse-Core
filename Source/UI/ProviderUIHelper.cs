using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimSynapse.Internal;

namespace RimSynapse.UI
{
    public static class ProviderUIHelper
    {
        private static Dictionary<string, string> testStatus = new Dictionary<string, string>();
        private static Dictionary<string, Color> testColor = new Dictionary<string, Color>();
        private static Dictionary<string, string> pendingModelSelections = new Dictionary<string, string>();

        public static void DrawCapabilitiesRow(Listing_Standard listing, ref LlmCapabilities caps, float indent)
        {
            Rect rect = listing.GetRect(24f);
            rect.xMin += indent;

            float curX = rect.x;
            float spacing = 10f;

            bool hasText = (caps & LlmCapabilities.Text) != 0;
            bool hasImage = (caps & LlmCapabilities.Image) != 0;
            bool hasVision = (caps & LlmCapabilities.Vision) != 0;
            bool hasAudio = (caps & LlmCapabilities.Audio) != 0;
            
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

            caps = LlmCapabilities.None;
            if (hasText) caps |= LlmCapabilities.Text;
            if (hasImage) caps |= LlmCapabilities.Image;
            if (hasVision) caps |= LlmCapabilities.Vision;
            if (hasAudio) caps |= LlmCapabilities.Audio;
        }

        public static void DrawProviderSection(Listing_Standard listing, ref string providerName, ApiProvider? providerEnum, ref string url, ref string key, ref string model, ref LlmCapabilities caps, System.Action onDelete = null)
        {
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
            
            if (url == null) url = "";
            url = Widgets.TextField(new Rect(urlRect.x + labelWidth, urlRect.y, urlRect.width - labelWidth, urlRect.height), url);
            
            listing.Gap(4f);
            
            if (providerName != "Pollinations.ai")
            {
                Rect modelRect = listing.GetRect(24f);
                modelRect.xMin += indent;
                Widgets.Label(new Rect(modelRect.x, modelRect.y, labelWidth, modelRect.height), "Model:");
                
                float selectBtnWidth = 80f;
                bool isCustom = providerEnum == ApiProvider.Custom;
                Rect modelFieldRect = new Rect(modelRect.x + labelWidth, modelRect.y, modelRect.width - labelWidth - selectBtnWidth - 8f, modelRect.height);
                Rect selectBtnRect = new Rect(modelRect.xMax - selectBtnWidth, modelRect.y, selectBtnWidth, modelRect.height);
                
                if (pendingModelSelections.TryGetValue(providerName, out string newM))
                {
                    model = newM;
                    pendingModelSelections.Remove(providerName);
                }

                if (model == null) model = "";
                model = Widgets.TextField(modelFieldRect, model);
                
                if (Widgets.ButtonText(selectBtnRect, "Select..."))
                {
                    if (providerEnum.HasValue)
                    {
                        string pName = providerName;
                        RimSynapse.Internal.ModelDefUtility.ShowModelSelector(providerEnum.Value, LlmCapabilities.None, (selectedModel) => {
                            pendingModelSelections[pName] = selectedModel;
                        });
                    }
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
            
            if (providerEnum == ApiProvider.Custom)
            {
                DrawCapabilitiesRow(listing, ref caps, indent);
            }

            if (providerEnum.HasValue)
            {
                listing.Gap(4f);
                Rect testRect = listing.GetRect(24f);
                testRect.xMin += indent;
                
                float btnWidth = 140f;
                Rect btnRect = new Rect(testRect.x, testRect.y, btnWidth, testRect.height);
                Rect statusRect = new Rect(btnRect.xMax + 10f, testRect.y, testRect.width - btnWidth - 10f, testRect.height);

                string keyName = providerName;

                if (Widgets.ButtonText(btnRect, "Test Connection"))
                {
                    testStatus[keyName] = "Testing...";
                    testColor[keyName] = Color.yellow;
                    
                    RimSynapse.Internal.HttpEngine.TestConnectionAsync(providerEnum.Value, url, key, model, (ok, msg) =>
                    {
                        testStatus[keyName] = ok ? "Success!" : msg;
                        testColor[keyName] = ok ? Color.green : Color.red;
                    });
                }
                
                if (testStatus.TryGetValue(keyName, out var status))
                {
                    Color oldColor = GUI.color;
                    GUI.color = testColor.TryGetValue(keyName, out var col) ? col : Color.white;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(statusRect, status);
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = oldColor;
                }
            }
        }
    }
}




