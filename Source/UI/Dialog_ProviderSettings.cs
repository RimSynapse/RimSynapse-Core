using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimSynapse.Internal;

namespace RimSynapse.UI
{
    public class Dialog_ProviderSettings : Window
    {
        public override Vector2 InitialSize => new Vector2(700f, 800f);

        private Vector2 scrollPosition = Vector2.zero;
        private float viewHeight = 1200f;
        
        private Dictionary<string, bool> isFetchingModels = new Dictionary<string, bool>();
        private Dictionary<string, List<string>> fetchedModels = new Dictionary<string, List<string>>();
        private Dictionary<string, bool> autoOpenMenu = new Dictionary<string, bool>();

        public Dialog_ProviderSettings()
        {
            this.doCloseButton = true;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.closeOnClickedOutside = false;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            SynapseGameComponent.ProcessMainThreadQueue();
        }

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                DoWindowContentsInner(inRect);
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[RimSynapse] Exception in DoWindowContents: {ex}");
            }
        }

        private float CalculateViewHeight(Rect outRect)
        {
            var settings = RimSynapseMod.Instance.Settings;
            float h = 0f;
            // 4 default standard providers (Local, OpenAI, Gemini, Claude)
            h += 202f * 4;
            // Pollinations (no model field, no test button)
            h += 146f;
            // Custom providers
            if (settings.customProviders != null)
            {
                h += 202f * settings.customProviders.Count;
            }
            // Add Custom Provider button
            h += 30f;
            // Extra padding
            h += 40f;
            return UnityEngine.Mathf.Max(h, outRect.height);
        }

        private void DoWindowContentsInner(Rect inRect)
        {
            var settings = RimSynapseMod.Instance.Settings;
            
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "Customize LLM Providers");
            Text.Font = GameFont.Small;
            
            Rect helpRect = new Rect(0, 35f, inRect.width, 24f);
            Widgets.Label(helpRect, "Configure endpoints, API keys, and models for text generation.");
            
            var outRect = new Rect(0, 70f, inRect.width, inRect.height - 120f);
            viewHeight = CalculateViewHeight(outRect);
            var viewRect = new Rect(0, 0, inRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // --- Default Providers ---
            string nLocal = "Local LM Studio";
            string nOpenAi = "OpenAI";
            string nGemini = "Google Gemini";
            string nClaude = "Anthropic Claude";
            string nPolli = "Pollinations.ai";

            ProviderUIHelper.DrawProviderSection(listing, ref nLocal, ApiProvider.Local_LMStudio, ref settings.lmStudioUrl, ref settings.lmStudioApiKey, ref settings.modelLocal, ref settings.capsLocal, false, null, isFetchingModels, fetchedModels, autoOpenMenu);
            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(12f);
            
            ProviderUIHelper.DrawProviderSection(listing, ref nOpenAi, ApiProvider.OpenAI, ref settings.openAiUrl, ref settings.openAiApiKey, ref settings.modelOpenAi, ref settings.capsOpenAi, true, null, isFetchingModels, fetchedModels, autoOpenMenu);
            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(12f);
            
            ProviderUIHelper.DrawProviderSection(listing, ref nGemini, ApiProvider.Google_Gemini, ref settings.geminiUrl, ref settings.geminiApiKey, ref settings.modelGemini, ref settings.capsGemini, true, null, isFetchingModels, fetchedModels, autoOpenMenu);
            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(12f);
            
            ProviderUIHelper.DrawProviderSection(listing, ref nClaude, ApiProvider.Anthropic_Claude, ref settings.claudeUrl, ref settings.claudeApiKey, ref settings.modelClaude, ref settings.capsClaude, true, null, isFetchingModels, fetchedModels, autoOpenMenu);
            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(12f);
            
            string dummyUrl = "https://image.pollinations.ai/prompt";
            string dummyKey = "";
            LlmCapabilities dummyCaps = LlmCapabilities.Image;
            ProviderUIHelper.DrawProviderSection(listing, ref nPolli, ApiProvider.Pollinations, ref dummyUrl, ref dummyKey, ref settings.modelPollinations, ref dummyCaps, false, null, isFetchingModels, fetchedModels, autoOpenMenu);
            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(12f);

            // --- Custom Providers Loop ---
            CustomProviderSettings toDelete = null;
            if (settings.customProviders != null)
            {
                for (int i = 0; i < settings.customProviders.Count; i++)
                {
                    var custom = settings.customProviders[i];
                    if (custom == null) continue; // Safety check against malformed XML

                    ProviderUIHelper.DrawProviderSection(listing, ref custom.name, ApiProvider.Custom, ref custom.url, ref custom.apiKey, ref custom.model, ref custom.caps, false, () => {
                        toDelete = custom;
                    }, isFetchingModels, fetchedModels, autoOpenMenu);
                    
                    listing.Gap(12f);
                    listing.GapLine();
                    listing.Gap(12f);
                }

                if (toDelete != null)
                {
                    settings.customProviders.Remove(toDelete);
                }
            }

            var oldColor2 = GUI.color;
            GUI.color = new Color(0.9f, 0.45f, 0.15f);
            if (listing.ButtonText("Add Custom Provider"))
            {
                if (settings.customProviders == null) settings.customProviders = new List<CustomProviderSettings>();
                settings.customProviders.Add(new CustomProviderSettings());
            }
            GUI.color = oldColor2;

            listing.End();
            Widgets.EndScrollView();
        }
    }
}
