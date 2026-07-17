using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse.UI
{
    public class Dialog_ScriptDebugger : Window
    {
        private static string _scriptJsonInput = "";
        private static List<string> _executionLogs = new List<string>();
        private Vector2 _logScrollPosition = Vector2.zero;
        private Vector2 _inputScrollPosition = Vector2.zero;

        private static readonly string ViktorSuicideTemplate = @"{
  ""scriptName"": ""Viktor Knife Suicide"",
  ""steps"": [
    {
      ""type"": ""possess_colonist"",
      ""arguments"": {
        ""pawnName"": ""Viktor"",
        ""action"": ""equip"",
        ""targetItemName"": ""knife""
      }
    },
    {
      ""type"": ""wait_until"",
      ""arguments"": {
        ""condition"": ""has_weapon"",
        ""pawnName"": ""Viktor"",
        ""timeoutTicks"": 6000
      }
    },
    {
      ""type"": ""damage_self_with_equipped"",
      ""arguments"": {
        ""pawnName"": ""Viktor"",
        ""targetBodyPart"": ""Heart"",
        ""damageMultiplier"": 4.0
      }
    }
  ]
}";

        private static readonly string ViktorMoveTemplate = @"{
  ""scriptName"": ""Viktor Pathing Test"",
  ""steps"": [
    {
      ""type"": ""possess_colonist"",
      ""arguments"": {
        ""pawnName"": ""Viktor"",
        ""action"": ""move"",
        ""targetX"": 115,
        ""targetZ"": 120
      }
    },
    {
      ""type"": ""wait_until"",
      ""arguments"": {
        ""condition"": ""reached_cell"",
        ""pawnName"": ""Viktor"",
        ""targetX"": 115,
        ""targetZ"": 120,
        ""timeoutTicks"": 8000
      }
    }
  ]
}";

        public Dialog_ScriptDebugger()
        {
            this.forcePause = false;
            this.doCloseX = true;
            this.doCloseButton = true;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = false;
            this.resizeable = true;
            this.draggable = true;
            this.preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(700f, 650f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "Synapse Storyteller Script Tester & Debugger");
            Text.Font = GameFont.Small;

            float curY = 40f;

            Widgets.Label(new Rect(0f, curY, inRect.width, 40f), 
                "Select a script template or paste custom JSON below. Click 'Run Script' to execute it step-by-step and inspect logs.");
            curY += 45f;

            // Template Buttons
            Rect templateLabelRect = new Rect(0f, curY, 120f, 30f);
            Widgets.Label(templateLabelRect, "Templates:");
            
            Rect suicideBtnRect = new Rect(130f, curY, 200f, 30f);
            if (Widgets.ButtonText(suicideBtnRect, "Load Knife Suicide"))
            {
                _scriptJsonInput = ViktorSuicideTemplate;
                _executionLogs.Add("[Debugger] Loaded Knife Suicide template. Edit 'Viktor' to your pawn's name.");
            }

            Rect moveBtnRect = new Rect(340f, curY, 200f, 30f);
            if (Widgets.ButtonText(moveBtnRect, "Load Pathing Test"))
            {
                _scriptJsonInput = ViktorMoveTemplate;
                _executionLogs.Add("[Debugger] Loaded Pathing Test template. Edit name and coordinates.");
            }
            curY += 35f;

            // JSON input area
            Widgets.Label(new Rect(0f, curY, inRect.width, 20f), "JSON Script Input:");
            curY += 22f;

            float inputHeight = 150f;
            Rect inputRect = new Rect(0f, curY, inRect.width, inputHeight);
            _scriptJsonInput = Widgets.TextArea(inputRect, _scriptJsonInput);
            curY += inputHeight + 10f;

            // Action Buttons
            Rect runBtnRect = new Rect(0f, curY, 150f, 35f);
            if (Widgets.ButtonText(runBtnRect, "Run Script"))
            {
                ExecuteScript();
            }

            Rect clearLogsBtnRect = new Rect(160f, curY, 150f, 35f);
            if (Widgets.ButtonText(clearLogsBtnRect, "Clear Debug Logs"))
            {
                _executionLogs.Clear();
            }
            curY += 45f;

            // Logs output
            Widgets.Label(new Rect(0f, curY, inRect.width, 20f), "Script Runner Logs:");
            curY += 22f;

            float remainingHeight = inRect.height - curY - 55f;
            float scrollWidth = inRect.width - 20f;

            float totalLogHeight = 10f;
            foreach (var log in _executionLogs)
            {
                totalLogHeight += Text.CalcHeight(log, scrollWidth) + 2f;
            }

            Rect outRect = new Rect(0f, curY, inRect.width, remainingHeight);
            Rect viewRect = new Rect(0f, 0f, scrollWidth, Math.Max(remainingHeight, totalLogHeight));

            Widgets.BeginScrollView(outRect, ref _logScrollPosition, viewRect);
            float logY = 0f;
            foreach (var log in _executionLogs)
            {
                float height = Text.CalcHeight(log, scrollWidth);
                Widgets.Label(new Rect(0f, logY, scrollWidth, height), log);
                logY += height + 2f;
            }
            Widgets.EndScrollView();
        }

        private void ExecuteScript()
        {
            if (string.IsNullOrEmpty(_scriptJsonInput.Trim()))
            {
                _executionLogs.Add("[Error] Script JSON input is empty.");
                return;
            }

            try
            {
                _executionLogs.Add("[Debugger] Instantiating script execution request...");
                RimSynapseAPI.ExecuteScript(_scriptJsonInput, msg =>
                {
                    _executionLogs.Add(msg);
                });
            }
            catch (Exception ex)
            {
                _executionLogs.Add($"[Error] Deserialization failure: {ex.Message}");
            }
        }
    }
}
