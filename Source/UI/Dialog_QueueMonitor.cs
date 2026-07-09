using System;
using System.Linq;
using UnityEngine;
using Verse;
using RimSynapse.Internal;

namespace RimSynapse.UI
{
    public class Dialog_QueueMonitor : Window
    {
        private Vector2 scrollPosition = Vector2.zero;
        private bool showPrompts = false;

        public override Vector2 InitialSize => new Vector2(900f, 700f);

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
            var queueSnapshot = RequestQueue.GetQueueSnapshot();
            var activeRequest = RequestQueue.ActiveRequest;
            var sw = RequestQueue.ActiveRequestStopwatch;

            // Header
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "LLM Queue Monitor");
            Text.Font = GameFont.Small;

            // Global stats
            Rect statsRect = new Rect(0, 40f, inRect.width, 25f);
            string stats = $"Depth: {RequestQueue.QueueDepth}  |  Throttle: {RequestQueue.ThrottleLevel:P0}  |  Avg Response: {RequestQueue.AverageResponseMs:F0}ms";
            Widgets.Label(statsRect, stats);

            // Toggles
            Rect toggleRect = new Rect(inRect.width - 200f, 40f, 200f, 25f);
            Widgets.CheckboxLabeled(toggleRect, "Show Prompts (Raw)", ref showPrompts);

            Widgets.DrawLineHorizontal(0, 70f, inRect.width);

            // Table Header
            Rect tableHeaderRect = new Rect(0, 80f, inRect.width - 16f, 25f);
            GUI.color = Color.gray;
            DrawRow(tableHeaderRect, "STATUS", "MOD", "PRIO", "AGE (ms)", "DYN SCORE", "TIMEOUT (ms)", true);
            GUI.color = Color.white;

            // Content Area — split between main queue and opportunistic
            float mainQueueEndY = 110f;
            float mainQueueHeight = (inRect.height - 110f) * 0.6f; // 60% for main queue
            Rect mainOutRect = new Rect(0, mainQueueEndY, inRect.width, mainQueueHeight);
            
            // Calculate main queue view height
            float mainViewHeight = 0f;
            if (activeRequest != null) mainViewHeight += GetRowHeight(activeRequest);
            foreach (var req in queueSnapshot)
            {
                mainViewHeight += GetRowHeight(req);
            }
            if (mainViewHeight < 25f) mainViewHeight = 25f;

            Rect mainViewRect = new Rect(0, 0, mainOutRect.width - 16f, mainViewHeight);

            Widgets.BeginScrollView(mainOutRect, ref scrollPosition, mainViewRect);

            float curY = 0f;

            // Draw Active
            if (activeRequest != null)
            {
                Rect rowRect = new Rect(0, curY, mainViewRect.width, GetRowHeight(activeRequest));
                GUI.color = Color.green;
                DrawRequestRow(rowRect, activeRequest, $"ACTIVE ({sw.ElapsedMilliseconds}ms)");
                GUI.color = Color.white;
                curY += rowRect.height;
            }

            // Draw Queued
            foreach (var req in queueSnapshot.OrderByDescending(r => r.CurrentScore))
            {
                Rect rowRect = new Rect(0, curY, mainViewRect.width, GetRowHeight(req));
                DrawRequestRow(rowRect, req, "WAITING");
                curY += rowRect.height;
            }

            Widgets.EndScrollView();

            // ── Opportunistic Tasks Section ────────────────────────────
            float oppY = mainOutRect.yMax + 10f;
            float oppHeight = inRect.height - oppY;

            if (oppHeight < 60f) return; // Not enough space

            Widgets.DrawLineHorizontal(0, oppY, inRect.width);
            oppY += 5f;

            // Opportunistic header
            Text.Font = GameFont.Medium;
            string throttleModeLabel = GetThrottleModeLabel();
            Widgets.Label(new Rect(0, oppY, inRect.width, 30f), $"Opportunistic Tasks  [{throttleModeLabel}]");
            Text.Font = GameFont.Small;
            oppY += 35f;

            // Opportunistic table header
            Rect oppTableHeader = new Rect(0, oppY, inRect.width - 16f, 25f);
            GUI.color = Color.gray;
            DrawOpportunisticHeader(oppTableHeader);
            GUI.color = Color.white;
            oppY += 25f;

            // Task entries
            var tasks = OpportunisticTaskManager.GetTaskSnapshot();
            int currentTick = (Current.ProgramState == ProgramState.Playing && Find.TickManager != null)
                ? Find.TickManager.TicksGame : 0;

            foreach (var task in tasks.OrderByDescending(t => t.Priority).ThenByDescending(t => t.EffectiveWeight))
            {
                if (oppY + 25f > inRect.height) break; // Clip if out of space

                Rect taskRow = new Rect(0, oppY, inRect.width - 16f, 25f);

                // Status color
                bool onCooldown = currentTick - task.LastRunTick < task.CooldownTicks;
                if (!task.Enabled)
                {
                    GUI.color = Color.gray;
                }
                else if (onCooldown)
                {
                    GUI.color = Color.yellow;
                }
                else
                {
                    GUI.color = new Color(0.5f, 1f, 0.5f); // Light green = ready
                }

                string status = !task.Enabled ? "DISABLED" :
                                onCooldown ? "COOLDOWN" : "READY";
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

        private void DrawOpportunisticHeader(Rect rect)
        {
            float[] widths = { 100f, 200f, 60f, 120f, 80f, 100f, 180f };
            float curX = rect.x;
            string[] labels = { "STATUS", "TASK", "PRIO", "WEIGHT (eff/base)", "RUNS", "COOLDOWN", "MOD" };
            for (int i = 0; i < labels.Length && i < widths.Length; i++)
            {
                Widgets.Label(new Rect(curX, rect.y, widths[i], 25f), labels[i]);
                curX += widths[i];
            }
        }

        private void DrawOpportunisticRow(Rect rect, string status, string task, string prio,
            string weight, string runs, string cooldown, string mod)
        {
            float[] widths = { 100f, 200f, 60f, 120f, 80f, 100f, 180f };
            float curX = rect.x;
            string[] vals = { status, task, prio, weight, runs, cooldown, mod };
            for (int i = 0; i < vals.Length && i < widths.Length; i++)
            {
                Widgets.Label(new Rect(curX, rect.y, widths[i], 25f), vals[i]);
                curX += widths[i];
            }
        }

        // ── Original helpers (unchanged) ────────────────────────────

        private float GetRowHeight(RequestQueue.QueuedRequest req)
        {
            if (!showPrompts || req.Messages == null) return 25f;
            
            float height = 25f;
            string textDump = string.Join("\n---\n", req.Messages.Select(m => $"[{m.role.ToUpper()}]\n{m.content}"));
            height += Text.CalcHeight(textDump, InitialSize.x - 32f) + 10f;
            return height;
        }

        private void DrawRow(Rect rect, string status, string mod, string prio, string age, string score, string timeout, bool isHeader = false)
        {
            float[] widths = { 150f, 200f, 80f, 100f, 100f, 100f };
            float curX = rect.x;

            Widgets.Label(new Rect(curX, rect.y, widths[0], 25f), status); curX += widths[0];
            Widgets.Label(new Rect(curX, rect.y, widths[1], 25f), mod); curX += widths[1];
            Widgets.Label(new Rect(curX, rect.y, widths[2], 25f), prio); curX += widths[2];
            Widgets.Label(new Rect(curX, rect.y, widths[3], 25f), age); curX += widths[3];
            Widgets.Label(new Rect(curX, rect.y, widths[4], 25f), score); curX += widths[4];
            Widgets.Label(new Rect(curX, rect.y, widths[5], 25f), timeout);
        }

        private void DrawRequestRow(Rect rect, RequestQueue.QueuedRequest req, string status)
        {
            string ageMs = (DateTime.UtcNow - req.EnqueuedAt).TotalMilliseconds.ToString("F0");
            string score = req.CurrentScore.ToString("F0");
            string timeout = req.Options?.maxWaitMs?.ToString() ?? "INF";
            string modName = req.Mod?.DisplayName ?? "Unknown";
            string prio = req.Priority.ToString();

            DrawRow(rect, status, modName, prio, ageMs, score, timeout);

            if (showPrompts && req.Messages != null)
            {
                string textDump = string.Join("\n\n", req.Messages.Select(m => $"[{m.role.ToUpper()}]\n{m.content}"));
                Rect dumpRect = new Rect(rect.x, rect.y + 25f, rect.width, rect.height - 25f);
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                Widgets.Label(dumpRect, textDump);
                GUI.color = Color.white;
            }
        }
    }
}
