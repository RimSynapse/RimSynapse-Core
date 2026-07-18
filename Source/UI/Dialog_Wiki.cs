using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimSynapse.UI
{
    public class WikiFile
    {
        public string Title;
        public string FilePath;
        public string ModName;
    }

    /// <summary>
    /// Draggable, resizeable in-game Wiki window that displays guide Markdown files
    /// translated on-the-fly to Unity Rich Text for standard formatting.
    /// </summary>
    public class Dialog_Wiki : Window
    {
        private List<WikiFile> wikiFiles = new List<WikiFile>();
        private WikiFile selectedFile;
        private string parsedText = "";

        private Vector2 leftScrollPosition = Vector2.zero;
        private Vector2 rightScrollPosition = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(820f, 600f);

        public Dialog_Wiki()
        {
            doCloseX = true;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = false;

            LoadWikiFiles();
            if (wikiFiles.Count > 0)
            {
                SelectFile(wikiFiles[0]);
            }
        }

        private void LoadWikiFiles()
        {
            wikiFiles.Clear();
            foreach (var mod in LoadedModManager.RunningMods)
            {
                string wikiPath = Path.Combine(mod.RootDir, "Learning");
                if (Directory.Exists(wikiPath))
                {
                    foreach (var file in Directory.GetFiles(wikiPath, "*.md"))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        string title = fileName.Replace('_', ' ');
                        wikiFiles.Add(new WikiFile
                        {
                            Title = title,
                            FilePath = file,
                            ModName = mod.Name
                        });
                    }
                }
            }
            // Sort by mod name first, then file title
            wikiFiles = wikiFiles.OrderBy(f => f.ModName).ThenBy(f => f.Title).ToList();
        }

        private void SelectFile(WikiFile file)
        {
            selectedFile = file;
            parsedText = "";
            rightScrollPosition = Vector2.zero;

            if (file != null && File.Exists(file.FilePath))
            {
                try
                {
                    string rawText = File.ReadAllText(file.FilePath);
                    parsedText = ParseMarkdownToRichText(rawText);
                }
                catch (Exception ex)
                {
                    parsedText = $"<color=red>Error loading file: {ex.Message}</color>";
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Header
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "RimSynapse Encyclopedia");
            Text.Font = GameFont.Small;

            float topY = 45f;
            float leftPaneWidth = 230f;
            float margin = 10f;
            float rightPaneX = leftPaneWidth + margin;
            float rightPaneWidth = inRect.width - rightPaneX;
            float paneHeight = inRect.height - topY - 15f;

            Rect leftRect = new Rect(0f, topY, leftPaneWidth, paneHeight);
            Rect rightRect = new Rect(rightPaneX, topY, rightPaneWidth, paneHeight);

            // Dividing line
            Widgets.DrawLineVertical(leftPaneWidth + 5f, topY, paneHeight);

            // 1. Draw Left Panel (Sidebar lists grouped by Mod Name)
            var grouped = wikiFiles.GroupBy(f => f.ModName).ToList();
            float leftContentHeight = wikiFiles.Count * 32f + grouped.Count * 28f;
            Rect leftViewRect = new Rect(0f, 0f, leftPaneWidth - 16f, leftContentHeight);

            Widgets.BeginScrollView(leftRect, ref leftScrollPosition, leftViewRect);
            float curY = 0f;
            foreach (var group in grouped)
            {
                // Group Header (Mod Name)
                Rect groupHeaderRect = new Rect(0f, curY, leftPaneWidth - 16f, 24f);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.6f, 0.8f, 0.6f, 0.9f);
                Widgets.Label(groupHeaderRect, group.Key.ToUpper());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                curY += 24f;

                foreach (var file in group)
                {
                    Rect rowRect = new Rect(5f, curY, leftPaneWidth - 21f, 28f);
                    
                    if (selectedFile == file)
                    {
                        Widgets.DrawHighlightSelected(rowRect);
                    }
                    else
                    {
                        Widgets.DrawHighlightIfMouseover(rowRect);
                    }

                    if (Widgets.ButtonInvisible(rowRect, true))
                    {
                        SelectFile(file);
                    }

                    Rect labelRect = new Rect(rowRect.x + 5f, rowRect.y + 4f, rowRect.width - 10f, 22f);
                    Widgets.Label(labelRect, file.Title);

                    curY += 32f;
                }
            }
            Widgets.EndScrollView();

            // 2. Draw Right Panel (Content View)
            if (selectedFile != null)
            {
                float textWidth = rightPaneWidth - 20f;
                float labelHeight = Text.CalcHeight(parsedText, textWidth);
                Rect rightViewRect = new Rect(0f, 0f, textWidth, labelHeight + 40f);

                Widgets.BeginScrollView(rightRect, ref rightScrollPosition, rightViewRect);
                Widgets.Label(new Rect(0f, 10f, textWidth, labelHeight), parsedText);
                Widgets.EndScrollView();
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rightRect, "No articles found. Make sure the Wiki markdown files are installed in the mod directories.");
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        private static string ParseMarkdownToRichText(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return "";

            string[] lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // 1. Headings
                if (line.StartsWith("# "))
                {
                    lines[i] = "\n<size=20><b>" + line.Substring(2) + "</b></size>\n";
                    continue;
                }
                if (line.StartsWith("## "))
                {
                    lines[i] = "\n<size=16><b>" + line.Substring(3) + "</b></size>\n";
                    continue;
                }
                if (line.StartsWith("### "))
                {
                    lines[i] = "\n<size=14><b>" + line.Substring(4) + "</b></size>\n";
                    continue;
                }

                // 2. Horizontal Rules
                if (line == "---" || line == "___" || line == "***")
                {
                    lines[i] = "<color=grey>──────────────────────────────────────────────────</color>";
                    continue;
                }

                // 3. Bullet points
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    lines[i] = "  • " + line.Substring(2);
                }
                else if (line.StartsWith("  - ") || line.StartsWith("  * "))
                {
                    lines[i] = "    • " + line.Substring(4);
                }

                // 4. Blockquotes
                if (line.StartsWith("> "))
                {
                    lines[i] = "  <i>" + line.Substring(2) + "</i>";
                }
            }

            string processed = string.Join("\n", lines);

            // 5. Bold & Italic Inline formatting
            processed = System.Text.RegularExpressions.Regex.Replace(processed, @"\*\*(.*?)\*\*", "<b>$1</b>");
            processed = System.Text.RegularExpressions.Regex.Replace(processed, @"__(.*?)__", "<b>$1</b>");
            processed = System.Text.RegularExpressions.Regex.Replace(processed, @"\*(.*?)\*", "<i>$1</i>");
            processed = System.Text.RegularExpressions.Regex.Replace(processed, @"_(.*?)_", "<i>$1</i>");

            // 6. Clean up links: [text](url) -> <b>text</b>
            processed = System.Text.RegularExpressions.Regex.Replace(processed, @"\[(.*?)\]\(.*?\)", "<b>$1</b>");

            // 7. Strip image blocks completely
            processed = System.Text.RegularExpressions.Regex.Replace(processed, @"\!\[.*?\]\(.*?\)", "");

            return processed.Trim();
        }
    }
}
