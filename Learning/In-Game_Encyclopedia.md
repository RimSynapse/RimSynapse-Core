# In-Game Encyclopedia and Learning Helper

This guide explains how to access, utilize, and extend the local **In-Game Encyclopedia** and **Learning Helper Tutor Integration** in **RimSynapse - Core**.

---

## 1. Accessing the In-Game Wiki

There are two primary ways to search and read help documentation natively inside RimWorld:

### The Encyclopedia Dialog Window
1. Open the **RimSynapse Core Mod Settings** menu.
2. Click **Customize LLM Providers**.
3. In the top section, click **Open Setup Wiki**. This will launch the in-game Encyclopedia window rather than opening a browser.
4. The window features a **split-pane layout**:
    *   **Left Sidebar:** Lists all available help guides, grouped by the mod that provides them.
    *   **Right Content Panel:** Displays the selected guide in scrollable rich text.

### The RimWorld Learning Helper Tutor
*   All installed wiki guide pages are dynamically loaded into RimWorld's built-in **Learning Helper** database on startup.
*   You can open the game tutor, search for any topic or mod name (e.g. *"ElevenLabs"*, *"Voicebox"*, *"PTSD"*), and read the parsed guide directly within the standard tutor panel.

---

## 2. Dynamic Formatting Support

The system reads standard Markdown files and translates them on-the-fly to Unity Rich Text formatting tags. The following formatting features are supported:
*   **Headings:** `# Heading 1`, `## Heading 2`, and `### Heading 3` tags are converted into larger, bold, readable titles.
*   **Bold and Italic inline text:** `**bold**` and `*italic*` markdown tokens display as standard bold (`<b>`) and italic (`<i>`) text.
*   **Bullet Lists:** Standard bullet items (`-` or `*`) are cleaned and rendered with bullet indicators (`  •`).
*   **Horizontal Rules:** Line markers (`---`) translate to custom horizontal dividing lines (`────────────────────────────────────────`).
*   **Links:** Markdown links `[text](url)` are automatically cleaned up and highlighted in bold inline text.

---

## 3. Guide for Mod Developers

If you are developing a companion mod for RimSynapse and want to ship local guides that integrate seamlessly with the in-game wiki:
1. Create a folder named `Wiki` at the root of your mod's directory (sibling to `About`, `Defs`, and `Assemblies`).
2. Save your documentation pages as Markdown files (`.md` extension) inside that folder.
3. The game will automatically detect, parse, and register them on startup. They will show up grouped under your mod's name in both the local Encyclopedia window and the tutor search index.
