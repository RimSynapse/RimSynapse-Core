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

        public override Vector2 InitialSize => new Vector2(900f, 600f);

        public Dialog_QueueMonitor()
        {
            this.doCloseX = true;
            this.forcePause = false;
            this.absorbInputAroundWindow = false;
            this.closeOnClickedOutside = false;
            this.preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var queueSnapshot = RequestQueue.GetQueueSnapshot();
            var activeRequest = RequestQueue.ActiveRequest;
            var sw = RequestQueue.ActiveRequestStopwatch;

            // Header
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "LLM Queue Monitor (Linux top-style)");
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

            // Content Area
            Rect outRect = new Rect(0, 110f, inRect.width, inRect.height - 110f);
            
            // Calculate height
            float viewHeight = 0f;
            if (activeRequest != null) viewHeight += GetRowHeight(activeRequest);
            foreach (var req in queueSnapshot)
            {
                viewHeight += GetRowHeight(req);
            }

            Rect viewRect = new Rect(0, 0, outRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            float curY = 0f;

            // Draw Active
            if (activeRequest != null)
            {
                Rect rowRect = new Rect(0, curY, viewRect.width, GetRowHeight(activeRequest));
                GUI.color = Color.green;
                DrawRequestRow(rowRect, activeRequest, $"ACTIVE ({sw.ElapsedMilliseconds}ms)");
                GUI.color = Color.white;
                curY += rowRect.height;
            }

            // Draw Queued
            foreach (var req in queueSnapshot.OrderByDescending(r => r.CurrentScore))
            {
                Rect rowRect = new Rect(0, curY, viewRect.width, GetRowHeight(req));
                DrawRequestRow(rowRect, req, "WAITING");
                curY += rowRect.height;
            }

            Widgets.EndScrollView();
        }

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
