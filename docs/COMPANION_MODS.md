# RimSynapse — Companion Mods Reference

> **Purpose:** Reference document for companion mods that depend on RimSynapse Core.
> All features below were extracted from the original Python bridge codebase and design docs.
> Nothing here is in scope for the Core library — these are separate Workshop mods in separate repos.

---

## Dependency Chain

```
RimSynapse-Core (this repo)     ← LM Studio connector, the foundation
│
├── RimSynapse-Context          ← Prompt template engine + context assembly
│   │                              (Core only)
│   │
│   ├── RimSynapse-Psychology   ← Pawn personality, weighted memories
│   │   │                         (requires Context for LLM-driven summaries)
│   │   │
│   │   └── RimSynapse-Chat    ← In-game dialogue UI
│   │                             (requires Context + Psychology)
│   │
│   └── RimSynapse-Chat        ← (also directly uses Context for prompt assembly)
│
├── RimSynapse-Storyteller      ← AI storyteller + narrative threads
│                                  (Core only — soft optional dep on Psychology)
│
└── RimSynapse-DevTools         ← Developer dashboard + config tools
                                   (Core only)
```

### Build Order

1. **Context** — foundation prompt engine, no deps beyond Core
2. **Psychology** — pawn data layer, depends on Context
3. **Chat** — dialogue UI, depends on Context + Psychology
4. **Storyteller** — colony events, depends on Core only (optionally reads Psychology data)
5. **DevTools** — debug dashboard, depends on Core only

---

# 1. RimSynapse-Context

**Prompt template engine + context assembly from game state**

### Purpose

Mods register prompt templates with weighted slots. At runtime, they fill slots with game data, and the engine assembles token-budget-aware prompts. Eliminates every mod from reinventing prompt building.

### Key Features

**Template Registration:**
- Register templates with `{{placeholder}}` syntax
- Each slot has: `weight` (0-10), `required` (bool), `default` (string), `type` (string)
- Templates get a SHA-256 fingerprint for cache invalidation
- Persisted to disk as JSON files in a `templates/` directory
- Auto-registration of undefined placeholders with default weight 5

**Template Fill:**
- Fill slots with values → get assembled prompt
- Token estimation: `len(prompt) // 4`
- **Budget-aware slot dropping:** If prompt exceeds `max_tokens`, drop lowest-weight optional slots first
- Arrays render as bulleted lists (`- item`)
- Unfilled optional placeholders are cleaned out (empty `{{}}` removed)
- Returns: prompt text, tokens estimated, slots filled, slots dropped

**Handshake / Cache Sync:**
- Consumer mod sends known template IDs + fingerprints
- Engine responds with what's still valid vs. what needs re-registration
- Detects engine restarts via boot_time comparison
- Returns `action_required` list of template IDs that need refresh

**Context Assembly:**
- Mod sends full game-state packet (pawn data, colony data, memories, relationships, threads)
- Engine filters by settings: weight threshold, memory limit, section toggles
- Returns structured JSON data + assembled prompt text
- Different prompt instructions per event type:

| Event Type | Instruction |
|---|---|
| `relationship` | "Respond in character. Show how this relationship dynamic plays out." |
| `dialogue` | "Respond in character as this colonist." |
| `reaction` | "Describe this colonist's reaction to the situation." |
| `event` | "Generate a narrative event that fits this colony's current state." |
| `quest` | "Describe this quest-related interaction." |
| `custom` | Uses the `framing` text directly |

**Raw Mode:**
- Pass-through for mods that build their own prompts
- Accepts: `{ "prompt": "..." }`, `{ "messages": [...] }`, or `{ "system": "...", "user": "..." }`
- Packages into OpenAI chat format with token estimate

### Data Model

```csharp
public class PromptTemplate
{
    public string modId;
    public string templateId;
    public string template;          // "You are {{pawn_name}}, a {{traits}} colonist..."
    public Dictionary<string, SlotConfig> slots;
    public int? maxTokens;
    public string description;
    public int fillCount;
    public string fingerprint;       // SHA-256 hash of template + slots
}

public class SlotConfig
{
    public float weight;     // 0-10, higher = more important
    public bool required;
    public string defaultValue;
    public string type;      // "string", "list", etc.
}

public class ContextPacket
{
    public string eventType;
    public string framing;
    public PawnPacket sourcePawn;
    public PawnPacket targetPawn;
    public ColonyPacket colony;
    public List<NarrativeThread> narrativeThreads;
    public ContextSettings settings;
}

public class ContextSettings
{
    public bool includeMemories = true;
    public bool includeRelationships = true;
    public bool includeTraits = true;
    public bool includeBackstory = true;
    public bool includeThreads = true;
    public int memoryLimit = 5;
    public float weightThreshold = 0.15f;
}
```

---

# 2. RimSynapse-Psychology

**Pawn personality evolution, weighted memories, opinion tracking**

### Purpose

Gives pawns persistent AI-driven personality that evolves over time. Memories have weights that decay, relationships have momentum via opinion integrals. All data lives in the save file via Scribe.

**Depends on: Core + Context** (uses Context to build prompts for personality generation)

### Key Features

**Weighted Memory System:**
- Each memory has: summary, type, tags, weight (0.0-1.0), baseWeight, decayRate, timesReferenced
- Memory types: `raid`, `social`, `event`, `trade`, `quest`, `daily`
- Stored per-pawn via `ThingComp`

**Memory Decay:**
- Runs once per in-game day
- `weight = max(0, weight - decayRate)`
- Prune memories where `weight <= 0`
- Default decay rate: 0.05 per day

**Memory Bump:**
- When LLM references a memory in its response, bump weight by 0.2
- `weight = min(1.0, weight + bumpAmount)`
- Increment `timesReferenced`

**Opinion Integral / Trajectory:**
- Sample pawn opinions periodically (every N ticks)
- Compute moving average (integral) across samples
- A pawn at +80 opinion today but integral of -15 means the relationship only recently improved
- LLM uses both current opinion AND integral for richer responses

**AI Personality Summary:**
- LLM generates personality summaries based on accumulated events and traits
- Stored as `personalitySummary` string on the pawn comp
- Evolves as new memories accumulate
- Used as context in every interaction

### Data Model (Scribe-Persisted)

```csharp
// Per-pawn data — attached via ThingComp
public class SynapsePawnComp : ThingComp
{
    public List<WeightedMemory> memories = new();
    public List<OpinionSample> opinionHistory = new();
    public string personalitySummary;

    public override void CompExposeData()
    {
        Scribe_Collections.Look(ref memories, "synapseMemories", LookMode.Deep);
        Scribe_Collections.Look(ref opinionHistory, "synapseOpinionHistory", LookMode.Deep);
        Scribe_Values.Look(ref personalitySummary, "synapsePersonality");
    }
}

public class WeightedMemory : IExposable
{
    public string summary;
    public string memoryType;        // raid, social, event, trade, quest
    public List<string> tags;
    public int gameTick;
    public float weight;             // 0.0 to 1.0
    public float baseWeight;
    public float decayRate;          // default 0.05
    public int timesReferenced;

    public void ExposeData() { /* Scribe_Values for each field */ }
}

public class OpinionSample : IExposable
{
    public string targetPawnId;
    public int opinion;
    public int gameTick;

    public void ExposeData() { /* Scribe_Values for each field */ }
}
```

**Pawn Data Packet Builder:**

```csharp
// Assembles live game state + persisted comp data into a JSON packet
public static PawnPacket BuildPawnPacket(Pawn pawn)
{
    var comp = pawn.GetComp<SynapsePawnComp>();
    return new PawnPacket
    {
        pawnId = pawn.ThingID,
        name = pawn.Name?.ToStringShort,
        gender = pawn.gender.ToString(),
        age = pawn.ageTracker.AgeBiologicalYears,
        backstory = GetBackstoryString(pawn),
        traits = pawn.story?.traits?.allTraits?.Select(t => t.Label).ToList(),
        skills = BuildSkillsDict(pawn),
        ideology = pawn.Ideo?.name,
        needs = new { mood = pawn.needs.mood.CurLevel, food = pawn.needs.food.CurLevel },
        memories = comp?.memories ?? new(),
        relationships = BuildRelationshipsArray(pawn, comp),
    };
}
```

---

# 3. RimSynapse-Chat

**In-game dialogue UI + conversation management**

### Purpose

Let players talk to their colonists. Manages conversation history, displays dialogue in-game, and handles the dialogue-specific response format.

**Depends on: Core + Context + Psychology**

### Key Features

**Dialogue Response Format:**
- Expected LLM response structure:
```json
{
    "reply": "What the pawn says",
    "thought": {
        "tag": "GRATEFUL",
        "description": "Appreciated the kind words",
        "relation_delta": 5
    },
    "relation_delta": 5
}
```
- Validation: ensure `reply` exists, ensure `thought` has `tag`/`description`/`relation_delta`
- Defaults: tag=`NEUTRAL`, description=`Responded`, relation_delta=`0`
- Plain text fallback: wrap raw text in the JSON structure
- `relation_delta` must be parsed as int (coerce non-numeric to 0)

**Pawn Name Tracking:**
- Extract pawn names from prompts and responses via regex
- Role patterns: `"You are playing the role of X"`, `"You are X"`, `"name is X"`
- Listing patterns: `"Character: X"`, `"Name: X"`, `"Pawn: X"`
- Track active pawns with last-seen timestamps
- Prune pawns not seen in 10 minutes

**Conversation History:**
- Store per-pawn conversation threads
- Include in subsequent prompts as assistant/user message history
- Manage conversation depth (limit to last N exchanges)

### UI Components

- **Chat window** — floating panel or RimWorld tab
- **Message bubbles** — pawn replies with thought tags
- **Opinion delta display** — show mood/opinion changes from dialogue
- **Conversation selector** — pick which pawn to talk to

---

# 4. RimSynapse-Storyteller

**AI storyteller integration + narrative thread system**

### Purpose

An AI-driven storyteller that generates and manages narrative events. Tracks ongoing story threads that connect events across time.

**Depends on: Core only** (optionally enhanced by Psychology if present — soft dependency)

### Key Features

**Narrative Thread System:**
- Events create keyword-tagged threads (e.g., `food_shortage`, `mechanoid_war`)
- Threads have weight that decays over time
- Active threads influence future prompts (e.g., food_shortage → pawns more anxious)
- Threads can be resolved with a resolution summary
- Categories: `crisis`, `social`, `political`, `economic`, `military`

**AI Storyteller/Director:**
- Hooks into RimWorld's incident system
- LLM generates narrative events that fit colony state
- Event types: raids, social events, quests, weather, trade

**AI Advisor:**
- Colony-level analysis and recommendations
- Generates advice JSON with target pawn and reasoning:
```json
{
    "advices": [
        {
            "target": "Engie",
            "reason": "Engie has been working non-stop and mood is dropping",
            "action": "schedule_recreation"
        }
    ]
}
```

**Colony Memory Summarizer:**
- Periodically summarize colony history
- Compress old memories into summaries to save token space

### Data Model (Scribe-Persisted)

```csharp
// Colony-wide data — WorldComponent
public class SynapseWorldComponent : WorldComponent
{
    public List<NarrativeThread> narrativeThreads = new();
    public List<InteractionRecord> interactionHistory = new();
    public List<PromptLogEntry> promptLog = new();

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref narrativeThreads, "narrativeThreads", LookMode.Deep);
        Scribe_Collections.Look(ref interactionHistory, "interactionHistory", LookMode.Deep);
        Scribe_Collections.Look(ref promptLog, "promptLog", LookMode.Deep);
    }
}

public class NarrativeThread : IExposable
{
    public string keyword;
    public string category;          // crisis, social, political, economic, military
    public string description;
    public float weight;             // 0.0 to 1.0
    public float decayRate;          // default 0.03
    public int timesReferenced;
    public bool isResolved;
    public string resolutionSummary;

    public void ExposeData() { /* Scribe_Values for each field */ }
}
```

---

# 5. RimSynapse-DevTools

**Developer dashboard, config tools, debugging**

### Purpose

Quality-of-life tools for mod developers building on RimSynapse. Not needed by end users.

**Depends on: Core only**

### Key Features

**Status Dashboard:**
- LM Studio connectivity + loaded models
- GPU stats breakdown
- Active pawn count + names
- Token usage averages (prompt, completion, duration)
- Capacity planning: tokens per pawn, max pawns by context window, calls per minute
- Queue depth and processing state

**Scalability Metrics:**
- Track last 50 request metrics: prompt tokens, completion tokens, duration
- Calculate averages for capacity planning
- Estimate max concurrent pawns based on context window size
- `tokensPerPawn = max(100, avgPromptTokens / activePawnCount)` or default 1200
- `maxPawnsByContext = contextTarget / tokensPerPawn`
- `callsPerMinute` from last 5 minutes

**Debug Logging:**
- Structured log entries: id, timestamp, level, type, message, details
- In-memory buffer (last 100 entries)
- Token tracking: prompt tokens, completion tokens, duration per request
- Active pawn tracker with 10-minute TTL

---

# Data Model Summary

All persistent data lives in the RimWorld save file via Scribe. Here's the complete picture across all mods:

```
Save File (.rws)
├── WorldComponent (SynapseWorldComponent)       ← Storyteller mod
│   ├── narrativeThreads[]
│   ├── interactionHistory[]
│   └── promptLog[]                              ← DevTools mod
│
└── ThingComp on each Pawn (SynapsePawnComp)     ← Psychology mod
    ├── memories[]
    │   ├── summary, type, tags
    │   ├── weight, baseWeight, decayRate
    │   └── timesReferenced
    ├── opinionHistory[]
    │   ├── targetPawnId, opinion
    │   └── gameTick
    └── personalitySummary
```

No external database. Portable. Save-game scoped. Automatically handled by RimWorld's load/save.
