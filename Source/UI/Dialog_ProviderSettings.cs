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



        private void DoWindowContentsInner(Rect inRect)
        {
            var settings = RimSynapseMod.Instance.Settings;
            
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "Customize LLM Providers");
            Text.Font = GameFont.Small;
            
            Rect helpRect = new Rect(0, 35f, inRect.width, 24f);
            Widgets.Label(helpRect, "Configure endpoints, API keys, and models for text generation.");
            
            var outRect = new Rect(0, 70f, inRect.width, inRect.height - 120f);
            // viewHeight is updated dynamically at the end of drawing
            var viewRect = new Rect(0, 0, inRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // --- Default Providers ---
            string nLocal = "Local LM Studio";
            string nOpenAi = "OpenAI";
            string nGemini = "Google Gemini";
            string nClaude = "Anthropic Claude";
            string nEleven = "ElevenLabs";
            string nPolli = "Pollinations.ai";

            ProviderUIHelper.DrawProviderSection(listing, ref nLocal, ApiProvider.Local_LMStudio, ref settings.lmStudioUrl, ref settings.lmStudioApiKey, ref settings.modelLocal, ref settings.capsLocal, null);
            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(12f);
            
            ProviderUIHelper.DrawProviderSection(listing, ref nOpenAi, ApiProvider.OpenAI, ref settings.openAiUrl, ref settings.openAiApiKey, ref settings.modelOpenAi, ref settings.capsOpenAi, null);
            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(12f);
            
            ProviderUIHelper.DrawProviderSection(listing, ref nGemini, ApiProvider.Google_Gemini, ref settings.geminiUrl, ref settings.geminiApiKey, ref settings.modelGemini, ref settings.capsGemini, null);
            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(12f);
            
            ProviderUIHelper.DrawProviderSection(listing, ref nClaude, ApiProvider.Anthropic_Claude, ref settings.claudeUrl, ref settings.claudeApiKey, ref settings.modelClaude, ref settings.capsClaude, null);
            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(12f);

            ProviderUIHelper.DrawProviderSection(listing, ref nEleven, ApiProvider.ElevenLabs, ref settings.elevenLabsUrl, ref settings.elevenLabsApiKey, ref settings.modelElevenLabs, ref settings.capsElevenLabs, null);
            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(12f);
            
            string dummyUrl = "https://image.pollinations.ai/prompt";
            string dummyKey = "";
            LlmCapabilities dummyCaps = LlmCapabilities.Image;
            ProviderUIHelper.DrawProviderSection(listing, ref nPolli, ApiProvider.Pollinations, ref dummyUrl, ref dummyKey, ref settings.modelPollinations, ref dummyCaps, null);
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

                    ProviderUIHelper.DrawProviderSection(listing, ref custom.name, ApiProvider.Custom, ref custom.url, ref custom.apiKey, ref custom.model, ref custom.caps, () => {
                        toDelete = custom;
                    });
                    
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
            viewHeight = listing.CurHeight + 20f;
            Widgets.EndScrollView();
        }
    }
}
