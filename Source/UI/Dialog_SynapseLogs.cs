using UnityEngine;
using Verse;
using RimSynapse.Internal;

namespace RimSynapse.UI
{
    public class Dialog_SynapseLogs : Window
    {
        private Vector2 scrollPosition;
        private string logContent;

        public override Vector2 InitialSize => new Vector2(800f, 600f);

        public Dialog_SynapseLogs()
        {
            doCloseButton = true;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            RefreshLog();
        }

        private void RefreshLog()
        {
            logContent = SessionLogger.GetCurrentLogContent();
            if (string.IsNullOrEmpty(logContent))
            {
                logContent = "Log is empty or disabled.";
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "RimSynapse Session Log");
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(new Rect(inRect.width - 120f, 0, 100f, 30f), "Refresh"))
            {
                RefreshLog();
            }

            Rect textRect = new Rect(0, 40f, inRect.width, inRect.height - 100f);
            
            float textHeight = Text.CalcHeight(logContent, textRect.width - 20f);
            Rect viewRect = new Rect(0, 0, textRect.width - 20f, Mathf.Max(textHeight, textRect.height));

            Widgets.BeginScrollView(textRect, ref scrollPosition, viewRect);
            Widgets.Label(viewRect, logContent);
            Widgets.EndScrollView();
        }
    }
}
