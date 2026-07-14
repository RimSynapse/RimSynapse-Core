using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using RimSynapse.Utils;

namespace RimSynapse.Patches
{
    /// <summary>
    /// Harmony patch on MainTabWindow_Quests.DoSelectedQuestInfo to add a "Voice Over"
    /// button that reads the selected quest details aloud.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Quests), "DoSelectedQuestInfo")]
    public static class Patch_MainTabWindow_Quests_DoSelectedQuestInfo
    {
        // Cache mapping: questId -> Tuple(voice, instruct)
        private static readonly Dictionary<int, Tuple<string, string>> _questVoiceCache = new Dictionary<int, Tuple<string, string>>();
        
        private static int _currentlyReadingQuestId = -1;
        private static bool _isLoadingVoice = false;

        public static void Postfix(MainTabWindow_Quests __instance, Rect rect, Quest ___selected)
        {
            Quest quest = ___selected;
            if (quest == null) return;

            // Render a neat small Voice Over action button at the top-right toolbar of the Quest details pane
            Rect voiceBtnRect = new Rect(rect.xMax - 140f, rect.y + 2f, 130f, 22f);
            
            string label = "Voice Over";
            if (_isLoadingVoice && _currentlyReadingQuestId == quest.id)
            {
                label = "Loading Voice...";
            }
            else if (AudioPlaybackManager.IsPlaying && _currentlyReadingQuestId == quest.id)
            {
                label = "Stop Reading";
            }

            if (Widgets.ButtonText(voiceBtnRect, label))
            {
                if (AudioPlaybackManager.IsPlaying && _currentlyReadingQuestId == quest.id)
                {
                    AudioPlaybackManager.StopPlayback();
                    _currentlyReadingQuestId = -1;
                }
                else
                {
                    StartQuestVoiceOver(quest);
                }
            }
        }

        private static void StartQuestVoiceOver(Quest quest)
        {
            AudioPlaybackManager.StopPlayback();
            _currentlyReadingQuestId = quest.id;
            _isLoadingVoice = true;

            // If we have cached the selected voice and instruct properties, play it immediately
            if (_questVoiceCache.TryGetValue(quest.id, out var cached))
            {
                GenerateAndPlayQuestAudio(quest, cached.Item1, cached.Item2);
                return;
            }

            // Resolve the current provider's available voices and consult the LLM
            ResolveVoicesAndPrompt(quest);
        }

        private static void ResolveVoicesAndPrompt(Quest quest)
        {
            string routeId = RimSynapseMod.Instance.Settings.defaultRoutingAudio;
            
            if (routeId == RoutingId.OpenAI)
            {
                var openAIVoices = new List<string> { "alloy", "echo", "fable", "onyx", "nova", "shimmer" };
                PromptLlmForVoiceSelection(quest, openAIVoices, routeId);
            }
            else if (routeId == RoutingId.ElevenLabs)
            {
                string apiKey = RimSynapseMod.Instance.Settings.elevenLabsApiKey;
                Internal.HttpEngine.FetchProviderVoicesAsync(ApiProvider.ElevenLabs, apiKey, (success, voices, err) =>
                {
                    var voiceList = (success && voices != null && voices.Count > 0) ? voices : new List<string> { "pNInz6obpgDQGcFmaJgB" };
                    PromptLlmForVoiceSelection(quest, voiceList, routeId);
                });
            }
            else if (routeId == RoutingId.Voicebox)
            {
                string url = RimSynapseMod.Instance.Settings.voiceboxUrl;
                string apiKey = RimSynapseMod.Instance.Settings.voiceboxApiKey;
                Internal.HttpEngine.FetchProviderModelsAsync(ApiProvider.Voicebox, url, apiKey, (success, profiles, err) =>
                {
                    var profileList = (success && profiles != null && profiles.Count > 0) ? profiles : new List<string> { "kokoro" };
                    PromptLlmForVoiceSelection(quest, profileList, routeId);
                });
            }
            else
            {
                PromptLlmForVoiceSelection(quest, new List<string> { "default" }, routeId);
            }
        }

        private static void PromptLlmForVoiceSelection(Quest quest, List<string> availableVoices, string routeId)
        {
            string voicesString = string.Join("\n- ", availableVoices);

            string systemPrompt = @"You are an expert voice casting assistant for RimWorld.
Analyze the quest details (title, description, faction, context) and select the most appropriate voice from the list of available voices to read it aloud.
Also, provide a short style or delivery instruction (such as 'calm, slow', 'whispered, high pitch', 'gravelly, deep voice', 'panicked, fast') to fit the tone.

You MUST respond strictly in valid JSON format:
{
  ""voice"": ""the_exact_selected_voice_name_from_the_list"",
  ""instruct"": ""short delivery instruction (under 5 words)""
}";

            string userMsg = $"Quest Title: {quest.name}\nQuest Description:\n{quest.description}\n\nAvailable Voices:\n- {voicesString}";

            SynapseClient.PromptAsync(
                RimSynapseMod.ModHandle,
                systemPrompt,
                userMsg,
                result =>
                {
                    string selectedVoice = availableVoices[0];
                    string instruct = "";

                    if (result.success && !string.IsNullOrEmpty(result.content))
                    {
                        try
                        {
                            string json = JsonHelper.ExtractJson(result.content);
                            if (json != null)
                            {
                                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                                if (parsed != null)
                                {
                                    if (parsed.TryGetValue("voice", out var v) && !string.IsNullOrEmpty(v))
                                    {
                                        var matched = availableVoices.Find(x => x.Equals(v.Trim(), StringComparison.OrdinalIgnoreCase));
                                        if (matched != null)
                                        {
                                            selectedVoice = matched;
                                        }
                                    }
                                    if (parsed.TryGetValue("instruct", out var inst))
                                    {
                                        instruct = inst;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            SynapseLogger.Warning($"Failed to parse LLM voice selection JSON: {ex.Message}");
                        }
                    }

                    // Cache results
                    _questVoiceCache[quest.id] = new Tuple<string, string>(selectedVoice, instruct);
                    
                    if (_currentlyReadingQuestId == quest.id)
                    {
                        GenerateAndPlayQuestAudio(quest, selectedVoice, instruct);
                    }
                },
                new ChatOptions { priority = 3, requestName = "Quest Narrator Selection", targetName = quest.name }
            );
        }

        private static void GenerateAndPlayQuestAudio(Quest quest, string voice, string instruct)
        {
            string questText = $"{quest.name}. {quest.description}";
            questText = StripXmlTags(questText);

            var audioReq = new LlmAudioRequest
            {
                InputText = questText,
                Voice = voice,
                ResponseFormat = "pcm",
                Instruct = instruct
            };

            SynapseClient.SendAudioAsync(
                RimSynapseMod.ModHandle,
                audioReq,
                new ChatOptions { priority = 3, requestName = "Quest Voice Over", targetName = quest.name },
                result =>
                {
                    _isLoadingVoice = false;
                    if (result.success && !string.IsNullOrEmpty(result.base64Audio))
                    {
                        if (_currentlyReadingQuestId == quest.id)
                        {
                            AudioPlaybackManager.PlayBase64Pcm(result.base64Audio);
                        }
                    }
                    else
                    {
                        if (_currentlyReadingQuestId == quest.id)
                        {
                            _currentlyReadingQuestId = -1;
                        }
                        string err = result.error ?? "Unknown error";
                        Messages.Message($"Quest voice over generation failed: {err}", MessageTypeDefOf.RejectInput, false);
                    }
                }
            );
        }

        private static string StripXmlTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return System.Text.RegularExpressions.Regex.Replace(input, "<[^>]*>", "");
        }
    }
}
