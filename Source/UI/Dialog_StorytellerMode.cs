using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Newtonsoft.Json;
using RimSynapse.Comps;
using RimSynapse.Models;

namespace RimSynapse.UI
{
    public class Dialog_StorytellerMode : Window
    {
        private static string _commandInput = "Make Fred have 20 Shooting, remove the Bipolar trait from Sarah, and start a fire on the solar generator.";
        private static string _statusText = "Ready";
        private static Color _statusColor = Color.white;
        private static int _activeQueriesCount = 0;
        private static string _selectedRoutingId = RimSynapse.RoutingId.LocalOnly;
        private static List<string> _executionLog = new List<string>();
        private Vector2 _logScrollPosition = Vector2.zero;
        private static bool _showFeatureRequestButton = false;
        private static string _rawJsonCallLog = "";

        public Dialog_StorytellerMode()
        {
            this.forcePause = false;
            this.doCloseX = true;
            this.doCloseButton = true;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = false;
            this.resizeable = true;
            this.draggable = true;
            this.preventCameraMotion = false;
            this.closeOnAccept = false;
        }

        public override Vector2 InitialSize => new Vector2(650f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "Synapse Storyteller Mode (LLM Direct Action Console)");
            Text.Font = GameFont.Small;

            float curY = 40f;

            // Description
            Widgets.Label(new Rect(0f, curY, inRect.width, 40f), 
                "Type a command in plain English. The Storyteller LLM will translate it into tool calls to execute changes on pawns or objects directly in-game.");
            curY += 45f;

            // Target Provider Selector
            Rect providerRect = new Rect(0f, curY, 260f, 30f);
            if (Widgets.ButtonText(providerRect, $"Target Provider: {_selectedRoutingId}"))
            {
                var list = new List<FloatMenuOption>();
                list.Add(new FloatMenuOption(RoutingId.LocalOnly, () => _selectedRoutingId = RoutingId.LocalOnly));
                list.Add(new FloatMenuOption(RoutingId.Jan, () => _selectedRoutingId = RoutingId.Jan));
                list.Add(new FloatMenuOption("OpenAI", () => _selectedRoutingId = RoutingId.OpenAI));
                list.Add(new FloatMenuOption(RoutingId.Gemini, () => _selectedRoutingId = RoutingId.Gemini));
                Find.WindowStack.Add(new FloatMenu(list));
            }

            // Enable Debug/Cheating Actions checkbox
            var settings = RimSynapseMod.Instance?.Settings;
            if (settings != null)
            {
                Rect debugToggleRect = new Rect(280f, curY, 300f, 30f);
                Widgets.CheckboxLabeled(debugToggleRect, "Enable Debug/Cheating Actions", ref settings.enableCheatingActions);
            }
            curY += 35f;

            // Text Area for input command
            Widgets.Label(new Rect(0f, curY, inRect.width, 20f), "Command Input (Press [Enter] to submit):");
            curY += 22f;
            
            Rect inputRect = new Rect(0f, curY, inRect.width, 40f);

            // Listen for Enter key press on the input box
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    if (!string.IsNullOrEmpty(_commandInput.Trim()))
                    {
                        string cmd = _commandInput;
                        _commandInput = ""; // Clear input box immediately
                        ExecuteCommand(cmd);
                        Event.current.Use(); // Consume the event to prevent adding a newline
                    }
                }
            }

            _commandInput = Widgets.TextArea(inputRect, _commandInput);
            curY += 45f;

            // Execute Button
            Rect execRect = new Rect(0f, curY, 150f, 35f);
            if (Widgets.ButtonText(execRect, _activeQueriesCount > 0 ? "Execute (Busy)" : "Execute Command"))
            {
                if (!string.IsNullOrEmpty(_commandInput.Trim()))
                {
                    string cmd = _commandInput;
                    _commandInput = ""; // Clear input box
                    ExecuteCommand(cmd);
                }
            }

            // Clear Log Button
            Rect clearLogRect = new Rect(165f, curY, 120f, 35f);
            if (Widgets.ButtonText(clearLogRect, "Clear Log"))
            {
                _executionLog.Clear();
            }

            // Status label next to buttons or Submit Feature Request button
            float statusX = 295f;
            if (_showFeatureRequestButton)
            {
                Rect requestRect = new Rect(295f, curY, 180f, 35f);
                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.9f, 0.45f, 0.15f); // Accent orange
                if (Widgets.ButtonText(requestRect, "Submit Feature Request"))
                {
                    SubmitFeatureRequest();
                }
                GUI.backgroundColor = oldBg;
                statusX = 485f;
            }

            Rect statusRect = new Rect(statusX, curY + 8f, inRect.width - statusX, 25f);
            GUI.color = _statusColor;
            Widgets.Label(statusRect, _statusText);
            GUI.color = Color.white;
            curY += 45f;

            // Scrollable execution log
            Widgets.Label(new Rect(0f, curY, inRect.width, 20f), "Execution Log:");
            curY += 22f;

            float totalLogHeight = 10f;
            float scrollWidth = inRect.width - 20f;
            foreach (var line in _executionLog)
            {
                totalLogHeight += Text.CalcHeight(line, scrollWidth) + 2f;
            }

            float remainingHeight = inRect.height - curY - 55f; // Leave space for close button
            Rect outRect = new Rect(0f, curY, inRect.width, remainingHeight);
            Rect viewRect = new Rect(0f, 0f, scrollWidth, Math.Max(remainingHeight, totalLogHeight));

            Widgets.BeginScrollView(outRect, ref _logScrollPosition, viewRect);
            float logY = 0f;
            foreach (var line in _executionLog)
            {
                float height = Text.CalcHeight(line, scrollWidth);
                Widgets.Label(new Rect(0f, logY, scrollWidth, height), line);
                logY += height + 2f;
            }
            Widgets.EndScrollView();
        }

        private void SubmitFeatureRequest()
        {
            string activeModel = Internal.ModelManager.ActiveModel ?? "Unknown (Local)";
            int contextLimit = RimSynapseMod.Instance?.Settings?.modelContextLimit ?? 8192;
            string dlcStatus = $"{(ModsConfig.IdeologyActive ? "Ideology " : "")}{(ModsConfig.RoyaltyActive ? "Royalty " : "")}{(ModsConfig.BiotechActive ? "Biotech " : "")}{(ModsConfig.AnomalyActive ? "Anomaly " : "")}";
            if (string.IsNullOrEmpty(dlcStatus)) dlcStatus = "None";

            string logMarkdown = $@"# RimSynapse Storyteller Mode Feature Request Log

**Command Entered:** `{_commandInput}`
**Active Model:** `{activeModel}`
**Model Context Limit:** `{contextLimit} tokens`
**Active DLCs:** `{dlcStatus.Trim()}`
**RimWorld Version:** `1.6.4871`

## Resolved Tool Calls JSON
```json
{_rawJsonCallLog}
```

## Verbose Execution Log
```
{string.Join("\n", _executionLog.Where(l => !l.StartsWith("> Request:")))}
```

Please copy this report and post it to the [RimSynapse Project](https://github.com/rimsynapse/Core/issues) to request support for this action sequence.";

            Find.WindowStack.Add(new Dialog_MessageBox(
                text: logMarkdown,
                buttonAText: "Copy to Clipboard",
                buttonAAction: () => {
                    GUIUtility.systemCopyBuffer = logMarkdown;
                    Messages.Message("Feature request log copied to clipboard!", RimWorld.MessageTypeDefOf.PositiveEvent, false);
                },
                buttonBText: "Cancel",
                title: "Submit Feature Request to RimSynapse"
            ));
        }

        private void ExecuteCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return;

            _activeQueriesCount++;
            _showFeatureRequestButton = false;
            _statusText = "Processing...";
            _statusColor = new Color(0.7f, 0.7f, 1f);

            RimSynapseAPI.ExecuteNaturalLanguageCommand(
                cmd,
                logMsg =>
                {
                    _executionLog.Add(logMsg);
                    if (logMsg.Contains("Invoking LLM"))
                    {
                        _statusText = "Invoking LLM...";
                    }
                    else if (logMsg.Contains("Executing step") || logMsg.Contains("[Executing]"))
                    {
                        _statusText = "Executing actions...";
                    }
                },
                (success, finalSummary) =>
                {
                    _activeQueriesCount--;
                    if (_activeQueriesCount < 0) _activeQueriesCount = 0;

                    if (_activeQueriesCount == 0)
                    {
                        if (success)
                        {
                            _statusText = "Done";
                            _statusColor = Color.green;
                        }
                        else
                        {
                            _statusText = "Failed";
                            _statusColor = Color.red;
                        }
                    }

                    bool hasFailure = _executionLog.Any(l => l.Contains("\"success\":false") || l.Contains("\"success\": false") || l.Contains("Warning") || l.Contains("Error"));
                    if (hasFailure)
                    {
                        _showFeatureRequestButton = true;
                    }
                }
            );
        }
    }
}
