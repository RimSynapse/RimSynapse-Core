using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimSynapse.Internal;

namespace RimSynapse.UI
{
    public class Dialog_QueueMonitor : Window
    {
        private Vector2 scrollPosition = Vector2.zero;
        // Toggles moved to RimSynapseSettings

        private enum SortColumn { Score, Status, Mod, Prio, Age, Timeout, Task, Target, Tokens }
        private SortColumn mainSortColumn = SortColumn.Score;
        private bool mainSortAscending = false;

        private enum OppSortColumn { Weight, Status, Task, Prio, Runs, Cooldown, Mod }
        private OppSortColumn oppSortColumn = OppSortColumn.Weight;
        private bool oppSortAscending = false;

        // Resizable column state
        // main: Prio, Mod, Target, Task, Age, Status, Score, Timeout, Tokens, Prompt, Response
        private float[] mainWidths = { 40f, 120f, 150f, 150f, 60f, 130f, 60f, 60f, 80f, 300f, 300f };
        private int draggingMainCol = -1;
        private float dragStartX = 0f;
        private float dragStartWidth = 0f;

        // opp: Status, Task, Prio, Weight, Runs, Cooldown, Mod
        private float[] oppWidths = { 100f, 200f, 60f, 120f, 80f, 100f, 180f };
        private int draggingOppCol = -1;

        public override Vector2 InitialSize => new Vector2(1200f, 750f);

        public Dialog_QueueMonitor()
        {
            this.doCloseX = true;
            this.forcePause = false;
            this.absorbInputAroundWindow = false;
            this.closeOnClickedOutside = false;
            this.preventCameraMotion = false;
            this.draggable = true;
            this.resizeable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            HandleColumnDragging();

            var queueSnapshot = RequestQueue.GetQueueSnapshot();
            var activeRequest = RequestQueue.ActiveRequest;
            var historySnapshot = RequestQueue.GetHistorySnapshot();
            var sw = RequestQueue.ActiveRequestStopwatch;

            // Header
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "LLM Queue Monitor");
            Text.Font = GameFont.Small;

            // Global stats
            Rect statsRect = new Rect(0, 40f, 650f, 20f);
            string stats = $"Depth: {RequestQueue.QueueDepth}  |  Throttle: {RequestQueue.ThrottleLevel:P0}  |  Avg: {RequestQueue.AverageResponseMs:F0}ms  |  TOPS: {RequestQueue.GlobalTops:F1}";
            Widgets.Label(statsRect, stats);

            // Provider Token Stats
            Rect pStatsRect = new Rect(0, 60f, inRect.width, 20f);
            var set = RimSynapseMod.Instance.Settings;
            string pStats = $"Tokens:  Local: {set.tokensPromptLocal}p/{set.tokensCompletionLocal}c  |  OpenAI: {set.tokensPromptOpenAi}p/{set.tokensCompletionOpenAi}c  |  Gemini: {set.tokensPromptGemini}p/{set.tokensCompletionGemini}c  |  Claude: {set.tokensPromptClaude}p/{set.tokensCompletionClaude}c";
            Widgets.Label(pStatsRect, pStats);

            // Columns Menu
            Rect t1 = new Rect(550f, 40f, 150f, 25f);
            if (Widgets.ButtonText(t1, "Columns..."))
            {
                var s = RimSynapseMod.Instance.Settings;
                var floatMenu = new List<FloatMenuOption>
                {
                    new FloatMenuOption($"Prio ({(s.qmShowPrio ? "ON" : "OFF")})", () => s.qmShowPrio = !s.qmShowPrio),
                    new FloatMenuOption($"Mod ({(s.qmShowMod ? "ON" : "OFF")})", () => s.qmShowMod = !s.qmShowMod),
                    new FloatMenuOption($"Target ({(s.qmShowTarget ? "ON" : "OFF")})", () => s.qmShowTarget = !s.qmShowTarget),
                    new FloatMenuOption($"Task ({(s.qmShowTask ? "ON" : "OFF")})", () => s.qmShowTask = !s.qmShowTask),
                    new FloatMenuOption($"Age ({(s.qmShowAge ? "ON" : "OFF")})", () => s.qmShowAge = !s.qmShowAge),
                    new FloatMenuOption($"Status ({(s.qmShowStatus ? "ON" : "OFF")})", () => s.qmShowStatus = !s.qmShowStatus),
                    new FloatMenuOption($"Score ({(s.qmShowScore ? "ON" : "OFF")})", () => s.qmShowScore = !s.qmShowScore),
                    new FloatMenuOption($"Timeout ({(s.qmShowTimeout ? "ON" : "OFF")})", () => s.qmShowTimeout = !s.qmShowTimeout),
                    new FloatMenuOption($"Tokens ({(s.qmShowTokens ? "ON" : "OFF")})", () => s.qmShowTokens = !s.qmShowTokens),
                    new FloatMenuOption($"Prompt ({(s.qmShowPrompt ? "ON" : "OFF")})", () => s.qmShowPrompt = !s.qmShowPrompt),
                    new FloatMenuOption($"Response ({(s.qmShowResponse ? "ON" : "OFF")})", () => s.qmShowResponse = !s.qmShowResponse)
                };
                Find.WindowStack.Add(new FloatMenu(floatMenu));
            }

            // TTL Toggle
            Rect t3 = new Rect(870f, 40f, 200f, 25f);
            if (Widgets.ButtonText(t3, $"History TTL: {RequestQueue.HistoryRetentionSeconds}s"))
            {
                var floatMenu = new List<FloatMenuOption>
                {
                    new FloatMenuOption("5 seconds", () => RequestQueue.HistoryRetentionSeconds = 5f),
                    new FloatMenuOption("15 seconds", () => RequestQueue.HistoryRetentionSeconds = 15f),
                    new FloatMenuOption("30 seconds", () => RequestQueue.HistoryRetentionSeconds = 30f),
                    new FloatMenuOption("60 seconds", () => RequestQueue.HistoryRetentionSeconds = 60f)
                };
                Find.WindowStack.Add(new FloatMenu(floatMenu));
            }

            Widgets.DrawLineHorizontal(0, 85f, inRect.width);

            // Table Header
            Rect tableHeaderRect = new Rect(0, 95f, inRect.width - 16f, 25f);
            DrawMainHeader(tableHeaderRect);

            // Content Area — split between main queue and opportunistic
            float mainQueueEndY = 125f;
            float mainQueueHeight = (inRect.height - 125f) * 0.6f;
            Rect mainOutRect = new Rect(0, mainQueueEndY, inRect.width, mainQueueHeight);
            
            // Calculate main queue view height
            float mainViewHeight = 0f;
            if (activeRequest != null) mainViewHeight += GetRowHeight(activeRequest);
            foreach (var req in historySnapshot) mainViewHeight += GetRowHeight(req);
            foreach (var req in queueSnapshot) mainViewHeight += GetRowHeight(req);
            
            if (mainViewHeight < 25f) mainViewHeight = 25f;

            Rect mainViewRect = new Rect(0, 0, GetTotalMainWidth(), mainViewHeight);
            Widgets.BeginScrollView(mainOutRect, ref scrollPosition, mainViewRect);

            float curY = 0f;

            // 1. Draw Active
            if (activeRequest != null)
            {
                Rect rowRect = new Rect(0, curY, mainViewRect.width, GetRowHeight(activeRequest));
                GUI.color = Color.green;
                DrawRequestRow(rowRect, activeRequest, $"ACTIVE ({sw.ElapsedMilliseconds}ms)");
                GUI.color = Color.white;
                curY += rowRect.height;
            }

            // 2. Draw Queued (Sorted)
            IEnumerable<RequestQueue.QueuedRequest> sortedMain = queueSnapshot;
            switch (mainSortColumn)
            {
                case SortColumn.Mod: sortedMain = mainSortAscending ? sortedMain.OrderBy(r => r.Mod?.DisplayName) : sortedMain.OrderByDescending(r => r.Mod?.DisplayName); break;
                case SortColumn.Prio: sortedMain = mainSortAscending ? sortedMain.OrderBy(r => r.Priority) : sortedMain.OrderByDescending(r => r.Priority); break;
                case SortColumn.Age: sortedMain = mainSortAscending ? sortedMain.OrderBy(r => r.EnqueuedAt) : sortedMain.OrderByDescending(r => r.EnqueuedAt); break;
                case SortColumn.Timeout: sortedMain = mainSortAscending ? sortedMain.OrderBy(r => r.Options?.maxWaitMs ?? int.MaxValue) : sortedMain.OrderByDescending(r => r.Options?.maxWaitMs ?? int.MaxValue); break;
                case SortColumn.Task: sortedMain = mainSortAscending ? sortedMain.OrderBy(r => r.Options?.requestName) : sortedMain.OrderByDescending(r => r.Options?.requestName); break;
                case SortColumn.Target: sortedMain = mainSortAscending ? sortedMain.OrderBy(r => r.Options?.targetName) : sortedMain.OrderByDescending(r => r.Options?.targetName); break;
                case SortColumn.Tokens: sortedMain = mainSortAscending ? sortedMain.OrderBy(r => r.Result?.completionTokens ?? 0) : sortedMain.OrderByDescending(r => r.Result?.completionTokens ?? 0); break;
                case SortColumn.Score: 
                default: sortedMain = mainSortAscending ? sortedMain.OrderBy(r => r.CurrentScore) : sortedMain.OrderByDescending(r => r.CurrentScore); break;
            }

            foreach (var req in sortedMain)
            {
                Rect rowRect = new Rect(0, curY, mainViewRect.width, GetRowHeight(req));
                DrawRequestRow(rowRect, req, "WAITING");
                curY += rowRect.height;
            }

            // 3. Draw Completed (History)
            foreach (var req in historySnapshot.OrderByDescending(r => r.CompletedAt))
            {
                Rect rowRect = new Rect(0, curY, mainViewRect.width, GetRowHeight(req));
                GUI.color = new Color(0.6f, 0.6f, 0.6f); // Dimmed
                string duration = req.Result != null ? $"{req.Result.durationMs}ms" : "?";
                string status = req.Result != null && req.Result.success ? $"COMPLETED ({duration})" : $"FAILED ({duration})";
                DrawRequestRow(rowRect, req, status);
                GUI.color = Color.white;
                curY += rowRect.height;
            }

            Widgets.EndScrollView();

            // ── Opportunistic Tasks Section ────────────────────────────
            float oppY = mainOutRect.yMax + 10f;
            float oppHeight = inRect.height - oppY;

            if (oppHeight < 60f) return;

            Widgets.DrawLineHorizontal(0, oppY, inRect.width);
            oppY += 5f;

            string throttleModeLabel = GetThrottleModeLabel();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, oppY, inRect.width, 30f), $"Opportunistic Tasks  [{throttleModeLabel}]");
            Text.Font = GameFont.Small;
            oppY += 35f;

            Rect oppTableHeader = new Rect(0, oppY, inRect.width - 16f, 25f);
            DrawOpportunisticHeader(oppTableHeader);
            oppY += 25f;

            var tasks = OpportunisticTaskManager.GetTaskSnapshot();
            int currentTick = (Current.ProgramState == ProgramState.Playing && Find.TickManager != null) ? Find.TickManager.TicksGame : 0;

            IEnumerable<OpportunisticTaskManager.TaskEntry> sortedOpp = tasks;
            switch (oppSortColumn)
            {
                case OppSortColumn.Status: sortedOpp = oppSortAscending ? sortedOpp.OrderBy(t => t.Enabled) : sortedOpp.OrderByDescending(t => t.Enabled); break;
                case OppSortColumn.Task: sortedOpp = oppSortAscending ? sortedOpp.OrderBy(t => t.Label) : sortedOpp.OrderByDescending(t => t.Label); break;
                case OppSortColumn.Prio: sortedOpp = oppSortAscending ? sortedOpp.OrderBy(t => t.Priority) : sortedOpp.OrderByDescending(t => t.Priority); break;
                case OppSortColumn.Runs: sortedOpp = oppSortAscending ? sortedOpp.OrderBy(t => t.TimesRun) : sortedOpp.OrderByDescending(t => t.TimesRun); break;
                case OppSortColumn.Cooldown: sortedOpp = oppSortAscending ? sortedOpp.OrderBy(t => currentTick - t.LastRunTick < t.CooldownTicks ? t.CooldownTicks - (currentTick - t.LastRunTick) : 0) : sortedOpp.OrderByDescending(t => currentTick - t.LastRunTick < t.CooldownTicks ? t.CooldownTicks - (currentTick - t.LastRunTick) : 0); break;
                case OppSortColumn.Mod: sortedOpp = oppSortAscending ? sortedOpp.OrderBy(t => t.Mod?.DisplayName) : sortedOpp.OrderByDescending(t => t.Mod?.DisplayName); break;
                case OppSortColumn.Weight:
                default: sortedOpp = oppSortAscending ? sortedOpp.OrderBy(t => t.EffectiveWeight) : sortedOpp.OrderByDescending(t => t.EffectiveWeight); break;
            }

            foreach (var task in sortedOpp)
            {
                if (oppY + 25f > inRect.height) break;
                Rect taskRow = new Rect(0, oppY, inRect.width - 16f, 25f);

                bool onCooldown = currentTick - task.LastRunTick < task.CooldownTicks;
                if (!task.Enabled) GUI.color = Color.gray;
                else if (onCooldown) GUI.color = Color.yellow;
                else GUI.color = new Color(0.5f, 1f, 0.5f);

                string status = !task.Enabled ? "DISABLED" : onCooldown ? "COOLDOWN" : "READY";
                int ticksRemaining = onCooldown ? task.CooldownTicks - (currentTick - task.LastRunTick) : 0;
                string cooldownStr = onCooldown ? $"{ticksRemaining / 2500f:F1}h" : "—";
                string weightStr = $"{task.EffectiveWeight:F2} / {task.BaseWeight:F1}";

                DrawOpportunisticRow(taskRow, status, task.Label, task.Priority.ToString(),
                    weightStr, task.TimesRun.ToString(), cooldownStr, task.Mod?.DisplayName ?? "?");

                GUI.color = Color.white;
                oppY += 25f;
            }

            if (tasks.Count == 0)
            {
                Widgets.Label(new Rect(10f, oppY, inRect.width, 25f), "No opportunistic tasks registered.");
            }
        }

        private void HandleColumnDragging()
        {
            if (Event.current.type == EventType.MouseUp)
            {
                draggingMainCol = -1;
                draggingOppCol = -1;
            }

            if (draggingMainCol >= 0 && Event.current.type == EventType.MouseDrag)
            {
                float newWidth = dragStartWidth + (Event.current.mousePosition.x - dragStartX);
                mainWidths[draggingMainCol] = Mathf.Max(30f, newWidth);
                Event.current.Use();
            }

            if (draggingOppCol >= 0 && Event.current.type == EventType.MouseDrag)
            {
                float newWidth = dragStartWidth + (Event.current.mousePosition.x - dragStartX);
                oppWidths[draggingOppCol] = Mathf.Max(30f, newWidth);
                Event.current.Use();
            }
        }

        private float GetTotalMainWidth()
        {
            var s = RimSynapseMod.Instance.Settings;
            bool[] colsVisible = { s.qmShowPrio, s.qmShowMod, s.qmShowTarget, s.qmShowTask, s.qmShowAge, s.qmShowStatus, s.qmShowScore, s.qmShowTimeout, s.qmShowTokens, s.qmShowPrompt, s.qmShowResponse };
            float w = 0;
            for (int i = 0; i < 11; i++) 
            {
                if (colsVisible[i]) w += mainWidths[i];
            }
            return w;
        }

        private void DrawMainHeader(Rect rect)
        {
            SortColumn[] cols = { SortColumn.Prio, SortColumn.Mod, SortColumn.Target, SortColumn.Task, SortColumn.Age, SortColumn.Status, SortColumn.Score, SortColumn.Timeout, SortColumn.Tokens };
            string[] labels = { "PRIO", "MOD", "TARGET", "TASK", "AGE(ms)", "STATUS", "SCORE", "TIMEOUT", "TOKENS" };
            
            float curX = rect.x - scrollPosition.x;
            GUI.color = Color.gray;

            var s = RimSynapseMod.Instance.Settings;
            bool[] colsVisible = { s.qmShowPrio, s.qmShowMod, s.qmShowTarget, s.qmShowTask, s.qmShowAge, s.qmShowStatus, s.qmShowScore, s.qmShowTimeout, s.qmShowTokens };

            // Draw standard 9 columns
            for (int i = 0; i < 9; i++)
            {
                if (!colsVisible[i]) continue;
                Rect cellRect = new Rect(curX, rect.y, mainWidths[i], 25f);
                string label = labels[i];
                if (mainSortColumn == cols[i]) label += mainSortAscending ? " ▲" : " ▼";
                
                Widgets.Label(cellRect, label);
                if (Widgets.ButtonInvisible(cellRect))
                {
                    if (mainSortColumn == cols[i]) mainSortAscending = !mainSortAscending;
                    else { mainSortColumn = cols[i]; mainSortAscending = false; }
                }

                // Vertical Line / Splitter
                Rect splitter = new Rect(curX + mainWidths[i] - 2f, rect.y, 4f, 25f);
                Widgets.DrawLineVertical(curX + mainWidths[i], rect.y, rect.height);
                Widgets.DrawHighlightIfMouseover(splitter);
                TooltipHandler.TipRegion(splitter, "Drag to resize");
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && splitter.Contains(Event.current.mousePosition))
                {
                    draggingMainCol = i;
                    dragStartX = Event.current.mousePosition.x;
                    dragStartWidth = mainWidths[i];
                    Event.current.Use();
                }
                
                curX += mainWidths[i];
            }

            // Optional Prompt column
            if (s.qmShowPrompt)
            {
                Rect cellRect = new Rect(curX, rect.y, mainWidths[9], 25f);
                Widgets.Label(cellRect, "PROMPT");
                Rect splitter = new Rect(curX + mainWidths[9] - 2f, rect.y, 4f, 25f);
                Widgets.DrawLineVertical(curX + mainWidths[9], rect.y, rect.height);
                Widgets.DrawHighlightIfMouseover(splitter);
                TooltipHandler.TipRegion(splitter, "Drag to resize");
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && splitter.Contains(Event.current.mousePosition))
                {
                    draggingMainCol = 9;
                    dragStartX = Event.current.mousePosition.x;
                    dragStartWidth = mainWidths[9];
                    Event.current.Use();
                }
                curX += mainWidths[9];
            }

            // Optional Response column
            if (s.qmShowResponse)
            {
                Rect cellRect = new Rect(curX, rect.y, mainWidths[10], 25f);
                Widgets.Label(cellRect, "RESPONSE");
                Rect splitter = new Rect(curX + mainWidths[10] - 2f, rect.y, 4f, 25f);
                Widgets.DrawLineVertical(curX + mainWidths[10], rect.y, rect.height);
                Widgets.DrawHighlightIfMouseover(splitter);
                TooltipHandler.TipRegion(splitter, "Drag to resize");
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && splitter.Contains(Event.current.mousePosition))
                {
                    draggingMainCol = 10;
                    dragStartX = Event.current.mousePosition.x;
                    dragStartWidth = mainWidths[10];
                    Event.current.Use();
                }
                curX += mainWidths[10];
            }

            GUI.color = Color.white;
        }

        private void DrawOpportunisticHeader(Rect rect)
        {
            OppSortColumn[] cols = { OppSortColumn.Status, OppSortColumn.Task, OppSortColumn.Prio, OppSortColumn.Weight, OppSortColumn.Runs, OppSortColumn.Cooldown, OppSortColumn.Mod };
            string[] labels = { "STATUS", "TASK", "PRIO", "WEIGHT", "RUNS", "COOLDOWN", "MOD" };
            
            float curX = rect.x;
            GUI.color = Color.gray;
            for (int i = 0; i < 7; i++)
            {
                Rect cellRect = new Rect(curX, rect.y, oppWidths[i], 25f);
                string label = labels[i];
                if (oppSortColumn == cols[i]) label += oppSortAscending ? " ▲" : " ▼";

                Widgets.Label(cellRect, label);
                if (Widgets.ButtonInvisible(cellRect))
                {
                    if (oppSortColumn == cols[i]) oppSortAscending = !oppSortAscending;
                    else { oppSortColumn = cols[i]; oppSortAscending = false; }
                }

                Rect splitter = new Rect(curX + oppWidths[i] - 2f, rect.y, 4f, 25f);
                Widgets.DrawLineVertical(curX + oppWidths[i], rect.y, rect.height);
                Widgets.DrawHighlightIfMouseover(splitter);
                TooltipHandler.TipRegion(splitter, "Drag to resize");
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && splitter.Contains(Event.current.mousePosition))
                {
                    draggingOppCol = i;
                    dragStartX = Event.current.mousePosition.x;
                    dragStartWidth = oppWidths[i];
                    Event.current.Use();
                }

                curX += oppWidths[i];
            }
            GUI.color = Color.white;
        }

        private void DrawRequestRow(Rect rect, RequestQueue.QueuedRequest req, string status)
        {
            float curX = rect.x;
            string ageMs = (DateTime.UtcNow - req.EnqueuedAt).TotalMilliseconds.ToString("F0");
            string score = req.CurrentScore.ToString("F0");
            string timeout = req.Options?.maxWaitMs?.ToString() ?? "INF";
            string modName = req.Mod?.DisplayName ?? "Unknown";
            string prio = req.Priority.ToString();
            string taskName = req.Options?.requestName ?? "—";
            string targetName = req.Options?.targetName ?? "—";
            string tokens = req.Result != null ? $"{req.Result.promptTokens} / {req.Result.completionTokens}" : "—";

            var s = RimSynapseMod.Instance.Settings;
            bool[] colsVisible = { s.qmShowPrio, s.qmShowMod, s.qmShowTarget, s.qmShowTask, s.qmShowAge, s.qmShowStatus, s.qmShowScore, s.qmShowTimeout, s.qmShowTokens };

            string[] vals = { prio, modName, targetName, taskName, ageMs, status, score, timeout, tokens };
            for (int i = 0; i < 9; i++)
            {
                if (!colsVisible[i]) continue;
                Rect r = new Rect(curX, rect.y, mainWidths[i] - 4f, rect.height);
                Widgets.Label(r, vals[i].Truncate(r.width));
                Widgets.DrawLineVertical(curX + mainWidths[i], rect.y, rect.height);
                curX += mainWidths[i];
            }

            if (s.qmShowPrompt)
            {
                Rect r = new Rect(curX, rect.y, mainWidths[9] - 4f, rect.height);
                string textDump = req.Messages != null ? string.Join("\n\n", req.Messages.Select(m => $"[{m.role.ToUpper()}]\n{m.content}")) : "";
                Widgets.Label(r, textDump);
                Widgets.DrawLineVertical(curX + mainWidths[9], rect.y, rect.height);
                curX += mainWidths[9];
            }

            if (s.qmShowResponse)
            {
                Rect r = new Rect(curX, rect.y, mainWidths[10] - 4f, rect.height);
                string resultStr = req.Result != null ? (req.Result.success ? req.Result.content : req.Result.error) : "Pending...";
                Widgets.Label(r, resultStr);
                Widgets.DrawLineVertical(curX + mainWidths[10], rect.y, rect.height);
                curX += mainWidths[10];
            }
            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
        }

        private void DrawOpportunisticRow(Rect rect, string status, string task, string prio,
            string weight, string runs, string cooldown, string mod)
        {
            float curX = rect.x;
            string[] vals = { status, task, prio, weight, runs, cooldown, mod };
            for (int i = 0; i < 7; i++)
            {
                Rect r = new Rect(curX, rect.y, oppWidths[i] - 4f, 25f);
                Widgets.Label(r, vals[i].Truncate(r.width));
                Widgets.DrawLineVertical(curX + oppWidths[i], rect.y, rect.height);
                curX += oppWidths[i];
            }
            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
        }

        private float GetRowHeight(RequestQueue.QueuedRequest req)
        {
            float height = 25f;
            var s = RimSynapseMod.Instance.Settings;
            if (s.qmShowPrompt && req.Messages != null)
            {
                string textDump = string.Join("\n\n", req.Messages.Select(m => $"[{m.role.ToUpper()}]\n{m.content}"));
                float promptH = Text.CalcHeight(textDump, mainWidths[9] - 4f);
                if (promptH + 10f > height) height = promptH + 10f;
            }
            if (s.qmShowResponse && req.Result != null)
            {
                string resultStr = req.Result.success ? req.Result.content : req.Result.error;
                float resH = Text.CalcHeight(resultStr, mainWidths[10] - 4f);
                if (resH + 10f > height) height = resH + 10f;
            }
            return height;
        }

        private string GetThrottleModeLabel()
        {
            var settings = RimSynapseMod.Instance?.Settings;
            int mode = settings?.opportunisticThrottleMode ?? -1;
            switch (mode)
            {
                case 0: return "Aggressive";
                case 1: return "Balanced";
                case 2: return "Conservative";
                default:
                    bool isRemote = settings?.IsRemoteUrl ?? false;
                    return isRemote ? "Auto → Conservative" : "Auto → Aggressive";
            }
        }
    }
}
