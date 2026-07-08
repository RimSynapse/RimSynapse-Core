# Context Embedding — XML-Driven Configuration Addendum

This addendum adds **XML Def–based configuration** to the context embedding system.
Everything described in context-planning-2 (prompts, weights, tiers, thresholds) becomes
configurable via XML files that mod authors and players can edit without touching C#.

> [!IMPORTANT]
> This uses RimWorld's native `Def` system — the same mechanism every RimWorld mod uses
> for items, buildings, recipes, etc. It's the standard, expected pattern.

---

## How RimWorld's Def System Works (Quick Primer)

```
1. You define a C# class that extends `Def`
2. You create XML files in your mod's `Defs/` folder
3. RimWorld loads all XML at startup → creates instances of your Def class
4. Other mods can PATCH your XML using XPath (in their `Patches/` folder)
5. Access at runtime: DefDatabase<YourDef>.GetNamed("defName")
```

This means:
- **Mod authors** put XML in their `Defs/` folder → no C# needed
- **Other mods** patch those XMLs in `Patches/` → no C# needed
- **Players** edit the XML directly in the mod folder → no C# needed
- **In-game** the Defs are live objects you can read at runtime

---

## 1. `SynapsePromptDef` — System Prompts via XML

### C# Def Class

```csharp
public class SynapsePromptDef : Def
{
    /// <summary>The event type this prompt applies to.</summary>
    public string eventType;          // "dialogue", "event", "thought", etc.

    /// <summary>Which mod this prompt is for. Null = default for all mods.</summary>
    public string targetModId;        // "rimsynapse.chat", "rimsynapse.storyteller"

    /// <summary>The system prompt text.</summary>
    public string systemPrompt;

    /// <summary>Priority for conflict resolution. Higher wins.</summary>
    public int priority = 0;

    /// <summary>Optional context framing appended after the prompt.</summary>
    public string contextFraming;
}
```

### XML — Core Ships These Defaults

```xml
<!-- Defs/SynapsePrompts/Prompts_Core.xml -->
<?xml version="1.0" encoding="utf-8"?>
<Defs>

  <!-- Default dialogue prompt (used by Chat if it doesn't override) -->
  <SynapsePromptDef>
    <defName>SynapsePrompt_Dialogue</defName>
    <label>Default Dialogue Prompt</label>
    <eventType>dialogue</eventType>
    <systemPrompt>
You are role-playing as a colonist in a sci-fi survival colony on a distant rimworld.
Respond in character. Be concise, natural, and reflect the colonist's personality,
mood, and relationships. Do not break character.
    </systemPrompt>
    <priority>0</priority>
  </SynapsePromptDef>

  <!-- Default event/storyteller prompt -->
  <SynapsePromptDef>
    <defName>SynapsePrompt_Event</defName>
    <label>Default Event Prompt</label>
    <eventType>event</eventType>
    <systemPrompt>
You are an AI storyteller for a sci-fi survival colony. Generate a narrative event
that fits the colony's current state, ongoing story threads, and faction relationships.
The event should feel organic and consequential.
    </systemPrompt>
    <priority>0</priority>
  </SynapsePromptDef>

  <!-- Default reaction prompt -->
  <SynapsePromptDef>
    <defName>SynapsePrompt_Reaction</defName>
    <label>Default Reaction Prompt</label>
    <eventType>reaction</eventType>
    <systemPrompt>
Describe this colonist's emotional and behavioral reaction to the current situation.
Consider their personality, mood, and past experiences.
    </systemPrompt>
    <priority>0</priority>
  </SynapsePromptDef>

  <!-- Default relationship prompt -->
  <SynapsePromptDef>
    <defName>SynapsePrompt_Relationship</defName>
    <label>Default Relationship Prompt</label>
    <eventType>relationship</eventType>
    <systemPrompt>
Respond in character. Show how this relationship dynamic plays out based on
the colonists' history, opinions, and personality differences.
    </systemPrompt>
    <priority>0</priority>
  </SynapsePromptDef>

  <!-- Lightweight thought prompt -->
  <SynapsePromptDef>
    <defName>SynapsePrompt_Thought</defName>
    <label>Default Thought Prompt</label>
    <eventType>thought</eventType>
    <systemPrompt>
Generate a brief internal thought for this colonist given their current mood
and situation. One or two sentences maximum.
    </systemPrompt>
    <priority>0</priority>
  </SynapsePromptDef>

  <!-- Quest prompt -->
  <SynapsePromptDef>
    <defName>SynapsePrompt_Quest</defName>
    <label>Default Quest Prompt</label>
    <eventType>quest</eventType>
    <systemPrompt>
Describe this quest-related interaction. Consider the involved factions,
the stakes, and how the colony's current state affects the outcome.
    </systemPrompt>
    <priority>0</priority>
  </SynapsePromptDef>

</Defs>
```

### How a Companion Mod Overrides a Prompt

The Chat mod ships its own XML that takes priority:

```xml
<!-- RimSynapse-Chat/Defs/SynapsePrompts/Prompts_Chat.xml -->
<Defs>
  <SynapsePromptDef>
    <defName>SynapsePrompt_Dialogue_Chat</defName>
    <label>Chat Dialogue Prompt</label>
    <eventType>dialogue</eventType>
    <targetModId>rimsynapse.chat</targetModId>
    <systemPrompt>
You are playing the role of a colonist. The player is talking to you directly.
Respond as this character would — use their personality, mood, and memories
to guide your tone. Keep replies conversational and under 3 sentences.

Reply in this JSON format:
{
  "reply": "What you say",
  "thought": { "tag": "EMOTION", "description": "Why you feel this way", "relation_delta": 0 }
}
    </systemPrompt>
    <priority>10</priority>
  </SynapsePromptDef>
</Defs>
```

### How a Third-Party Mod Patches a Prompt (No C#!)

A random Workshop mod that wants to change the dialogue style:

```xml
<!-- SomeRandomMod/Patches/SynapsePromptPatch.xml -->
<Patch>
  <Operation Class="PatchOperationReplace">
    <xpath>/Defs/SynapsePromptDef[defName="SynapsePrompt_Dialogue"]/systemPrompt</xpath>
    <value>
      <systemPrompt>
You are a grizzled survivor on a rimworld. Speak in short, terse sentences.
Never use flowery language. Life is hard and you know it.
      </systemPrompt>
    </value>
  </Operation>
</Patch>
```

### Runtime Resolution

```csharp
// Core resolves which prompt to use:
public static string ResolvePrompt(string eventType, string modId)
{
    // 1. Look for mod-specific prompt (highest priority)
    var modSpecific = DefDatabase<SynapsePromptDef>.AllDefs
        .Where(d => d.eventType == eventType && d.targetModId == modId)
        .OrderByDescending(d => d.priority)
        .FirstOrDefault();

    if (modSpecific != null) return modSpecific.systemPrompt;

    // 2. Fall back to default prompt for this event type
    var defaultPrompt = DefDatabase<SynapsePromptDef>.AllDefs
        .Where(d => d.eventType == eventType && d.targetModId == null)
        .OrderByDescending(d => d.priority)
        .FirstOrDefault();

    return defaultPrompt?.systemPrompt ?? "";
}
```

---

## 2. `SynapseWeightDef` — Weight Table via XML

### C# Def Class

```csharp
public class SynapseWeightDef : Def
{
    public string slot;           // "backstory", "traits", "mood", "skills", etc.
    public float  baseWeight;     // 0-10
    public bool   required;       // if true, never dropped
    public string description;    // human-readable explanation
}
```

### XML — Core Ships Default Weights

```xml
<!-- Defs/SynapseWeights/Weights_Default.xml -->
<Defs>

  <SynapseWeightDef>
    <defName>SynapseWeight_PawnIdentity</defName>
    <slot>pawnIdentity</slot>
    <baseWeight>10</baseWeight>
    <required>true</required>
    <description>Pawn name, gender, age. Always included.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_EventType</defName>
    <slot>eventType</slot>
    <baseWeight>10</baseWeight>
    <required>true</required>
    <description>Event framing. Always included.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Backstory</defName>
    <slot>backstory</slot>
    <baseWeight>6</baseWeight>
    <required>false</required>
    <description>Childhood + adulthood backstory text.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Traits</defName>
    <slot>traits</slot>
    <baseWeight>7</baseWeight>
    <required>false</required>
    <description>Pawn trait labels and degrees.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Mood</defName>
    <slot>mood</slot>
    <baseWeight>7</baseWeight>
    <required>false</required>
    <description>Current mood level and active thoughts.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Skills</defName>
    <slot>skills</slot>
    <baseWeight>4</baseWeight>
    <required>false</required>
    <description>All pawn skills with levels and passions.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Health</defName>
    <slot>health</slot>
    <baseWeight>5</baseWeight>
    <required>false</required>
    <description>Injuries, diseases, bionics, addictions.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Relationships</defName>
    <slot>relationships</slot>
    <baseWeight>6</baseWeight>
    <required>false</required>
    <description>Direct social bonds (Spouse, Rival, etc).</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Opinions</defName>
    <slot>opinions</slot>
    <baseWeight>5</baseWeight>
    <required>false</required>
    <description>Numeric opinion scores toward other pawns.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Ideology</defName>
    <slot>ideology</slot>
    <baseWeight>4</baseWeight>
    <required>false</required>
    <description>Ideology name and precepts (DLC).</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Colony</defName>
    <slot>colony</slot>
    <baseWeight>4</baseWeight>
    <required>false</required>
    <description>Colony wealth, population, danger level.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Factions</defName>
    <slot>factions</slot>
    <baseWeight>4</baseWeight>
    <required>false</required>
    <description>Faction names, goodwill, and relation type.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Weather</defName>
    <slot>weather</slot>
    <baseWeight>2</baseWeight>
    <required>false</required>
    <description>Current season, weather, and biome.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Memories</defName>
    <slot>memories</slot>
    <baseWeight>6</baseWeight>
    <required>false</required>
    <description>Weighted AI memories from Psychology mod.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Threads</defName>
    <slot>narrativeThreads</slot>
    <baseWeight>5</baseWeight>
    <required>false</required>
    <description>Active narrative threads from Storyteller mod.</description>
  </SynapseWeightDef>

  <SynapseWeightDef>
    <defName>SynapseWeight_Personality</defName>
    <slot>personalitySummary</slot>
    <baseWeight>6</baseWeight>
    <required>false</required>
    <description>LLM-generated personality summary from Psychology.</description>
  </SynapseWeightDef>

</Defs>
```

### A Third-Party Mod Bumps Skills Weight

```xml
<!-- SkillFocusMod/Patches/WeightPatch.xml -->
<Patch>
  <Operation Class="PatchOperationReplace">
    <xpath>/Defs/SynapseWeightDef[defName="SynapseWeight_Skills"]/baseWeight</xpath>
    <value><baseWeight>8</baseWeight></value>
  </Operation>
</Patch>
```

---

## 3. `SynapseContextProfileDef` — Event Type Profiles via XML

### C# Def Class

```csharp
public class SynapseContextProfileDef : Def
{
    public string eventType;
    public float  budgetFraction;     // 0.0–1.0 of available context window
    public List<string> includeTiers; // ["Identity", "PawnState", "Synthetic"]

    /// <summary>
    /// Per-slot weight overrides for this profile.
    /// If a slot isn't listed, the default from SynapseWeightDef applies.
    /// </summary>
    public List<SlotWeightOverride> weightOverrides;
}

public class SlotWeightOverride
{
    public string slot;
    public float  weight;
}
```

### XML — Core Ships Default Profiles

```xml
<!-- Defs/SynapseProfiles/Profiles_Default.xml -->
<Defs>

  <SynapseContextProfileDef>
    <defName>SynapseProfile_Thought</defName>
    <label>Thought Profile</label>
    <eventType>thought</eventType>
    <budgetFraction>0.15</budgetFraction>
    <includeTiers>
      <li>Identity</li>
    </includeTiers>
  </SynapseContextProfileDef>

  <SynapseContextProfileDef>
    <defName>SynapseProfile_Dialogue</defName>
    <label>Dialogue Profile</label>
    <eventType>dialogue</eventType>
    <budgetFraction>0.50</budgetFraction>
    <includeTiers>
      <li>Identity</li>
      <li>PawnState</li>
      <li>Synthetic</li>
    </includeTiers>
  </SynapseContextProfileDef>

  <SynapseContextProfileDef>
    <defName>SynapseProfile_Relationship</defName>
    <label>Relationship Profile</label>
    <eventType>relationship</eventType>
    <budgetFraction>0.35</budgetFraction>
    <includeTiers>
      <li>Identity</li>
      <li>PawnState</li>
      <li>Synthetic</li>
    </includeTiers>
    <weightOverrides>
      <li><slot>relationships</slot><weight>9</weight></li>
      <li><slot>opinions</slot><weight>8</weight></li>
    </weightOverrides>
  </SynapseContextProfileDef>

  <SynapseContextProfileDef>
    <defName>SynapseProfile_Reaction</defName>
    <label>Reaction Profile</label>
    <eventType>reaction</eventType>
    <budgetFraction>0.25</budgetFraction>
    <includeTiers>
      <li>Identity</li>
      <li>PawnState</li>
    </includeTiers>
    <weightOverrides>
      <li><slot>mood</slot><weight>9</weight></li>
      <li><slot>health</slot><weight>7</weight></li>
    </weightOverrides>
  </SynapseContextProfileDef>

  <SynapseContextProfileDef>
    <defName>SynapseProfile_Event</defName>
    <label>Colony Event Profile</label>
    <eventType>event</eventType>
    <budgetFraction>0.70</budgetFraction>
    <includeTiers>
      <li>Identity</li>
      <li>Colony</li>
      <li>World</li>
    </includeTiers>
  </SynapseContextProfileDef>

  <SynapseContextProfileDef>
    <defName>SynapseProfile_Quest</defName>
    <label>Quest Profile</label>
    <eventType>quest</eventType>
    <budgetFraction>0.60</budgetFraction>
    <includeTiers>
      <li>Identity</li>
      <li>PawnState</li>
      <li>Colony</li>
      <li>World</li>
      <li>Synthetic</li>
    </includeTiers>
  </SynapseContextProfileDef>

</Defs>
```

### A Storyteller Mod Adds Its Own Profile

```xml
<!-- RimSynapse-Storyteller/Defs/SynapseProfiles/Profiles_Storyteller.xml -->
<Defs>
  <SynapseContextProfileDef>
    <defName>SynapseProfile_StorytellerNarration</defName>
    <label>Storyteller Narration Profile</label>
    <eventType>storyteller_narration</eventType>
    <budgetFraction>0.80</budgetFraction>
    <includeTiers>
      <li>Identity</li>
      <li>PawnState</li>
      <li>Colony</li>
      <li>World</li>
      <li>Synthetic</li>
    </includeTiers>
    <weightOverrides>
      <li><slot>narrativeThreads</slot><weight>9</weight></li>
      <li><slot>factions</slot><weight>7</weight></li>
      <li><slot>backstory</slot><weight>8</weight></li>
    </weightOverrides>
  </SynapseContextProfileDef>
</Defs>
```

---

## 4. `SynapseThoughtFilterDef` — Thought Filter Config via XML

```xml
<!-- Defs/SynapseConfig/ThoughtFilter.xml -->
<Defs>
  <SynapseThoughtFilterDef>
    <defName>SynapseThoughtFilter_Default</defName>
    <label>Default Thought Filter</label>

    <!-- Include thoughts with mood impact >= this value (absolute) -->
    <moodImpactThreshold>20</moodImpactThreshold>

    <!-- Include thoughts newer than this many in-game hours -->
    <recencyHours>12</recencyHours>

    <!-- Exclude thoughts past this % of their expiration -->
    <expirationCutoffPercent>0.90</expirationCutoffPercent>

    <!-- Deduplicate stacking thoughts -->
    <deduplicateStacks>true</deduplicateStacks>

    <!-- Include situational (non-memory) thoughts -->
    <includeSituational>true</includeSituational>
  </SynapseThoughtFilterDef>
</Defs>
```

A player who wants more thought detail just edits:
```xml
<moodImpactThreshold>10</moodImpactThreshold>
<recencyHours>24</recencyHours>
```

---

## 5. File Layout in the Mod

```
RimSynapse-Core/
├── About/
│   └── About.xml
├── Defs/                              ← NEW: all XML config lives here
│   ├── SynapsePrompts/
│   │   └── Prompts_Core.xml          ← default system prompts
│   ├── SynapseWeights/
│   │   └── Weights_Default.xml       ← default weight table
│   ├── SynapseProfiles/
│   │   └── Profiles_Default.xml      ← event type → tier/budget profiles
│   └── SynapseConfig/
│       └── ThoughtFilter.xml         ← thought filter settings
├── Patches/
│   └── (existing patches)
├── Source/
│   ├── Defs/                         ← NEW: C# Def classes
│   │   ├── SynapsePromptDef.cs
│   │   ├── SynapseWeightDef.cs
│   │   ├── SynapseContextProfileDef.cs
│   │   └── SynapseThoughtFilterDef.cs
│   └── (existing source files)
└── (existing folders)
```

---

## 6. The Mod Author Experience

### "I just want to change the dialogue prompt"

Edit `Defs/SynapsePrompts/Prompts_Core.xml` or create a new XML file with a higher priority. **Zero C# needed.**

### "I'm building a companion mod and want custom event types"

Add a new `SynapsePromptDef` + `SynapseContextProfileDef` in your mod's `Defs/` folder. Core picks them up automatically.

### "I want to patch someone else's prompt without conflicting"

Use RimWorld's standard XPath patching in your `Patches/` folder. This is how the entire RimWorld modding ecosystem works — no special handling needed.

### "I'm a player and want to tweak weights"

Open `Defs/SynapseWeights/Weights_Default.xml` in a text editor. Change numbers. Restart RimWorld. Done.

---

## 7. Resolution Order

When Core assembles context, it resolves XML defs in this order:

```
1. SynapseContextProfileDef  → which tiers and budget fraction
2. SynapseWeightDef          → base weight per slot
3. Profile.weightOverrides   → per-event-type weight adjustments
4. ChatOptions.weightOverrides → per-request overrides from C# (Storyteller boosting)
5. SynapsePromptDef          → system prompt (mod-specific → default fallback)
6. SynapseThoughtFilterDef   → thought inclusion rules
```

C# runtime overrides (step 4) always win over XML — this is how Storyteller's dynamic weight boosting works on top of the XML baseline.

---

## 8. Impact on context-planning-2

| Planning-2 Item | XML Impact |
|---|---|
| Default weight table (Section 6) | **Replaced** by `SynapseWeightDef` XML |
| Query-type budget fractions (Section 4) | **Replaced** by `SynapseContextProfileDef` XML |
| ContextTierMask defaults (Section 3) | **Replaced** by `SynapseContextProfileDef.includeTiers` XML |
| Thought filtering thresholds (Section 5) | **Replaced** by `SynapseThoughtFilterDef` XML |
| Per-mod system prompts (Section 3) | **Enhanced** — mods can use XML OR C# registration |
| Adaptive budget calculation | **Unchanged** — still C# logic using `budgetFraction` from XML |
| Context assembly pipeline | **Unchanged** — reads Defs at runtime instead of hardcoded values |

### New Files to Add

| File | Type |
|---|---|
| `Source/Defs/SynapsePromptDef.cs` | C# Def class |
| `Source/Defs/SynapseWeightDef.cs` | C# Def class |
| `Source/Defs/SynapseContextProfileDef.cs` | C# Def class |
| `Source/Defs/SynapseThoughtFilterDef.cs` | C# Def class |
| `Defs/SynapsePrompts/Prompts_Core.xml` | Default prompts |
| `Defs/SynapseWeights/Weights_Default.xml` | Default weights |
| `Defs/SynapseProfiles/Profiles_Default.xml` | Default profiles |
| `Defs/SynapseConfig/ThoughtFilter.xml` | Default thought filter |
