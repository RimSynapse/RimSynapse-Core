using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimSynapse.Patches
{
    /// <summary>
    /// Intercepts major threat letters to rewrite their text via LLM,
    /// adding Aura's spunky personality and commenting on the difficulty.
    /// </summary>
    [HarmonyPatch(typeof(LetterStack), "ReceiveLetter", new Type[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) })]
    public static class Patch_LetterStack_ReceiveLetter
    {
        // Track letters we are currently processing or have already processed
        private static System.Collections.Generic.HashSet<Letter> _processedLetters = new System.Collections.Generic.HashSet<Letter>();

        public static bool Prefix(LetterStack __instance, Letter let, string debugInfo, int delayTicks, bool playSound)
        {
            if (let == null) return true;

            // If we've already processed this letter, let vanilla handle it
            if (_processedLetters.Contains(let)) return true;

            ChoiceLetter choiceLet = let as ChoiceLetter;
            if (choiceLet == null) return true; // Only intercept choice letters

            // Only intercept if Aura (Synapse) is the storyteller
            if (Find.Storyteller?.def?.defName != "Synapse") return true;

            // Determine if it's a major threat or a quest
            bool isThreat = let.def == LetterDefOf.ThreatBig;
            bool isQuest = choiceLet.quest != null;

            if (!isThreat && !isQuest) return true; // Not something we want to rewrite

            // Attempt to find the Quest Asker
            Pawn asker = null;
            if (isQuest && choiceLet.quest.QuestLookTargets != null)
            {
                asker = choiceLet.quest.QuestLookTargets
                    .Select(t => t.Thing as Pawn)
                    .FirstOrDefault(p => p != null && p.Faction != Faction.OfPlayer && !p.Dead);
            }

            // Extract the fully resolved text
            string originalTitle = let.Label.Resolve();
            string originalText = choiceLet.Text.Resolve(); 

            // Add to processed so we don't infinitely loop when we manually inject it later
            _processedLetters.Add(let);

            // Ask the LLM to rewrite it
            string systemPrompt = @"You are Aura, the AI Storyteller in RimWorld.
A new event or quest has occurred. Rewrite the notification letter to fit your sassy, dramatic, or menacing persona.
Use the provided vanilla text as the baseline. Maintain all critical gameplay information (who, what, where, rewards, threats).
Do NOT use bracket tags like [Asker_nameFull]. Just use the resolved names provided in the vanilla text.

You MUST respond strictly in valid JSON:
{
  ""Title"": ""Your new dramatic title"",
  ""Description"": ""Your rewritten multi-paragraph description. Mention the consequences.""
}";

            if (asker != null)
            {
                string factionName = asker.Faction?.Name ?? "an unknown faction";
                // Wait, StoryTellerRoyaltyIntegration doesn't exist anymore, or maybe it's in Core? 
                // Let's just use title if available, else omit. We can check for RoyalTitle if Biotech/Royalty is active.
                string title = "representative";
                if (asker.royalty != null && asker.royalty.MainTitle() != null)
                {
                    title = asker.royalty.MainTitle().GetLabelCapFor(asker);
                }
                
                systemPrompt = $@"You are {asker.Name.ToStringShort}, a {title} of {factionName}.
You are formally contacting a RimWorld colony to offer them a quest or opportunity. 
Write the notification letter from YOUR first-person perspective ('I am {asker.Name.ToStringShort}...').
Use the provided vanilla text as the baseline. Maintain all critical gameplay information (who, what, where, rewards, threats).
Do NOT use bracket tags like [Asker_nameFull]. Just use the resolved names provided in the vanilla text.

You MUST respond strictly in valid JSON:
{{
  ""Title"": ""A formal or dramatic title for your request"",
  ""Description"": ""Your rewritten multi-paragraph letter. Speak directly to the colonists.""
}}";
            }

            string userMessage = $"Vanilla Title: {originalTitle}\nVanilla Text: {originalText}\nRewrite this event.";

            SynapseClient.PromptAsync(
                RimSynapseMod.ModHandle,
                systemPrompt,
                userMessage,
                result =>
                {
                    if (result.success)
                    {
                        try
                        {
                            string json = RimSynapse.Utils.JsonHelper.ExtractJson(result.content);
                            if (json != null)
                            {
                                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(json);
                                if (parsed != null && parsed.ContainsKey("Title") && parsed.ContainsKey("Description"))
                                {
                                    let.Label = parsed["Title"];
                                    choiceLet.Text = parsed["Description"];

                                    // If this is tied to a quest, overwrite the quest log too!
                                    if (choiceLet.quest != null)
                                    {
                                        choiceLet.quest.name = parsed["Title"];
                                        choiceLet.quest.description = parsed["Description"];
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            RimSynapse.SynapseLogger.Warn("core", $"[RimSynapse-Core] Failed to parse letter rewrite: {ex.Message}");
                        }
                    }

                    // Push the letter to the UI on the main thread
                    Verse.LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        __instance.ReceiveLetter(let, debugInfo, delayTicks, playSound);
                    });
                },
                new RimSynapse.ChatOptions { priority = 3, requestName = "Rewrite Letter", targetName = originalTitle } // High priority for UI events
            );

            // Block the vanilla ReceiveLetter call right now, because we will inject it later
            return false;
        }
    }
}
