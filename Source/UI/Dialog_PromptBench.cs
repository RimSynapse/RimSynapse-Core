using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSynapse.UI
{
    public class Dialog_PromptBench : Window
    {
        private static SynapseModHandle _testHandle;
        private static string _testStatus = "";
        private static Color _testStatusColor = Color.white;
        private static string _customPrompt = "What are three tips for surviving a RimWorld raid?";
        private static bool _testBusy;
        private static string _selectedRoutingId = RimSynapse.RoutingId.LocalOnly;

        public Dialog_PromptBench()
        {
            this.forcePause = true;
            this.doCloseX = true;
            this.doCloseButton = true;
            this.closeOnClickedOutside = true;
            this.absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(600f, 400f);

        public override void DoWindowContents(Rect inRect)
        {
            var settings = RimSynapseMod.Instance.Settings;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "Prompt Bench");
            Text.Font = GameFont.Small;

            var listing = new Listing_Standard();
            listing.Begin(new Rect(0, 40f, inRect.width, inRect.height - 40f));

            if (listing.ButtonText($"Target Provider: {_selectedRoutingId}"))
            {
                var list = new List<FloatMenuOption>();
                list.Add(new FloatMenuOption(RoutingId.LocalOnly, () => _selectedRoutingId = RoutingId.LocalOnly));
                list.Add(new FloatMenuOption(RoutingId.OpenAI, () => _selectedRoutingId = RoutingId.OpenAI));
                list.Add(new FloatMenuOption(RoutingId.Gemini, () => _selectedRoutingId = RoutingId.Gemini));
                list.Add(new FloatMenuOption(RoutingId.Claude, () => _selectedRoutingId = RoutingId.Claude));
                foreach(var custom in settings.customProviders)
                {
                    string id = RoutingId.CustomPrefix + custom.id;
                    list.Add(new FloatMenuOption($"Custom: {custom.name}", () => _selectedRoutingId = id));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }

            listing.Gap(10f);

            if (listing.ButtonText(_testBusy ? "Sending..." : "Quick Test: \"Tell me a joke\""))
            {
                if (!_testBusy)
                    RunTestPrompt("You are a witty comedian. Reply with one short joke.", "Tell me a joke.");
            }

            listing.Gap(4f);
            listing.Label("Custom Prompt:");
            _customPrompt = listing.TextEntry(_customPrompt, 2);
            listing.Gap(4f);

            if (listing.ButtonText(_testBusy ? "Sending..." : "Send Custom Prompt"))
            {
                if (!_testBusy && !string.IsNullOrWhiteSpace(_customPrompt))
                    RunTestPrompt("You are a helpful assistant. Be concise.", _customPrompt);
            }

            if (!string.IsNullOrEmpty(_testStatus))
            {
                listing.Gap(10f);
                var prevColor = GUI.color;
                GUI.color = _testStatusColor;
                listing.Label(_testStatus);
                GUI.color = prevColor;
            }

            listing.End();
        }

        private void RunTestPrompt(string systemPrompt, string userMessage)
        {
            _testBusy = true;
            string thinkingLabel = RimSynapseMod.Instance.Settings.disableThinking ? "OFF" : "ON";
            _testStatus = $"Sending prompt to {_selectedRoutingId} (thinking: {thinkingLabel})...";
            _testStatusColor = Color.yellow;

            if (_testHandle == null)
                _testHandle = SynapseCore.Register("rimsynapse.test", "RimSynapse Test");

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Internal.HttpEngine.EnsureInitialized();
                    var messages = new List<ChatMessage>
                    {
                        ChatMessage.System(systemPrompt),
                        ChatMessage.User(userMessage),
                    };

                    var options = ChatOptions.Default;
                    // Force the routing for this specific call by using a temporary override or modifying the routing dictionary for the test handle
                    // Wait, currently we just do PostChatCompletionSync. How to force a route?
                    // We can temporarily set the test handle's routing in Settings.
                    RimSynapseMod.Instance.Settings.queryRoutingIds["rimsynapse.test:default"] = _selectedRoutingId;
                    
                    var result = Internal.HttpEngine.PostChatCompletionSync(_testHandle, messages, options);

                    if (result.success)
                    {
                        string preview = result.content;
                        if (preview != null && preview.Length > 200)
                            preview = preview.Substring(0, 200) + "...";

                        _testStatus = $"[{result.durationMs}ms | {result.model} | {result.promptTokens}p/{result.completionTokens}c tokens | thinking: {thinkingLabel}]\n{preview}";
                        _testStatusColor = Color.green;
                    }
                    else
                    {
                        _testStatus = $"Error: {result.error}";
                        _testStatusColor = Color.red;
                    }
                }
                catch (Exception ex)
                {
                    _testStatus = $"Error: {ex.Message}";
                    _testStatusColor = Color.red;
                }
                finally
                {
                    _testBusy = false;
                }
            });
        }
    }
}
