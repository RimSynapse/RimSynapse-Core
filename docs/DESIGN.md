# RimSynapse Design Document

> **Version**: 0.2.0  
> **Last Updated**: 2026-07-06  
> **Status**: Architecture Revision — Game-Native Storage

---

## Table of Contents

1. [Project Vision](#project-vision)
2. [Architecture Overview](#architecture-overview)
3. [Part 1: Platform — Bridge Server](#part-1-platform--bridge-server)
   - [Context Assembly API](#context-assembly-api)
   - [LLM Proxy](#llm-proxy)
   - [Dashboard](#dashboard)
4. [Part 2: C# Mod — Game-Native Data](#part-2-c-mod--game-native-data)
   - [Data Storage via Scribe](#data-storage-via-scribe)
   - [Weight System (C# Side)](#weight-system-c-side)
   - [Pawn Data Packets](#pawn-data-packets)
5. [Part 3: Mod Vision](#part-3-mod-vision)
   - [Dynamic Pawn Psychology](#dynamic-pawn-psychology)
   - [Faction & World Simulation](#faction--world-simulation)
   - [Dynamic Chat Interactions](#dynamic-chat-interactions)
   - [Narrative Thread System](#narrative-thread-system)
   - [Relationship Integrals & Political Capital](#relationship-integrals--political-capital)

---

## Project Vision

RimSynapse is two things:

1. **A platform** — a local Python bridge server that provides:
   - A **context assembly engine** that receives game data, filters by mod settings, and returns structured prompts
   - A **local AI proxy** that connects to LM Studio / Ollama so mods can make LLM calls without managing connections
   - A **dashboard** for mod developers to monitor data flow, AI calls, and game state

2. **A mod** (separate repo) — our own RimWorld mod that uses the platform to enhance NPC/pawn interactions with rich backstories, dynamic personality evolution, faction warfare, and emergent culture building.

### Key Insight: The Game IS the Database

RimWorld's `.rws` save files are XML documents that mods can extend with custom data via the `Scribe` serialization system. Every mod gets its own persistent storage in the save file through:

- **`WorldComponent`** — global data (memory stores, narrative threads, faction AI state)
- **`GameComponent`** — per-game data (settings, prompt logs)
- **`ThingComp`** on Pawns — per-pawn data (memory weights, AI personality, opinion history)

This means **no external database is needed.** All persistent game state lives in the save file — it's portable, save-game scoped, and automatically handled by RimWorld's load/save system.

The bridge server is **stateless**. It receives data, processes it, and returns results. Nothing to initialize, nothing to migrate, nothing to corrupt.

### Architecture

```
┌──────────────────┐   JSON    ┌──────────────────┐  proxy   ┌──────────┐
│  Any RimWorld    │ ────────> │  RimSynapse      │ ───────> │ LM Studio│
│  Mod (C#)        │           │  Bridge (Python)  │          │ / Ollama │
│                  │ <──────── │                  │ <─────── │ (Local)  │
│  - Owns all data │  response │  - Context assem │  LLM out └──────────┘
│  - Sends packets │           │  - LLM proxy     │
│  - Runs decay    │           │  - Dashboard     │
│  - Parses output │           │  - Stateless!    │
└──────────────────┘           └──────────────────┘
     DATA OWNER                   COMPUTE LAYER
```

### Design Principles

- **Game-native storage** — all persistent data in the save file via Scribe, travels with the save
- **Stateless bridge** — the server has no database, no state to corrupt, nothing to migrate
- **Full packet model** — the mod ALWAYS sends the full data packet; the bridge filters by settings
- **Mod-agnostic** — any mod can send a context packet and get a prompt back
- **Small-model friendly** — context assembly optimizes for minimal token windows
- **Portable** — ships as a zip file, auto-downloads embedded Python, no prerequisites

---

# Part 1: Platform — Bridge Server

The bridge is a stateless compute layer. It receives data from the game, assembles context, and proxies LLM calls.

## Context Assembly API

The core value of the bridge: receive a full game-state packet, filter by mod settings, and return a structured prompt.

### `POST /api/context/build`

The mod sends everything it has. The bridge returns only what's relevant.

**Request:**
```json
{
    "event_type": "relationship",
    "framing": "They argued over food rations after a raid",

    "source_pawn": {
        "pawn_id": "Thing_Human_1",
        "name": "Engie",
        "gender": "Female",
        "age": 28,
        "backstory": "Urbworld urchin -> Gunsmith",
        "traits": ["Kind", "Neurotic"],
        "skills": {"Shooting": 12, "Crafting": 15},
        "ideology": "Originalist",
        "needs": {"mood": 0.45, "food": 0.8},
        "memories": [
            {"summary": "Survived a brutal mechanoid raid", "weight": 0.85, "type": "raid"},
            {"summary": "Married Val in the garden", "weight": 0.92, "type": "social"},
            {"summary": "Ate a simple meal", "weight": 0.1, "type": "daily"}
        ],
        "relationships": [
            {"with": "Fred", "type": "Rival", "opinion": -34, "integral": -28.5}
        ]
    },

    "target_pawn": {
        "pawn_id": "Thing_Human_2",
        "name": "Fred",
        "gender": "Male",
        "age": 35,
        "traits": ["Greedy", "Tough"],
        "memories": [
            {"summary": "Hoarded food during toxic fallout", "weight": 0.7, "type": "social"}
        ]
    },

    "colony": {
        "name": "New Hope",
        "biome": "Temperate Forest",
        "ideology": "Originalist",
        "wealth": 45000,
        "pawn_count": 8
    },

    "narrative_threads": [
        {"keyword": "food_shortage", "description": "Colony nearly starved during toxic fallout", "weight": 0.6}
    ],

    "settings": {
        "include_memories": true,
        "include_relationships": true,
        "include_traits": true,
        "include_backstory": true,
        "include_threads": true,
        "memory_limit": 5,
        "weight_threshold": 0.15
    }
}
```

**Response:**
```json
{
    "prompt": "You are Engie, a Kind, Neurotic colonist...",
    "data": {
        "source_pawn": { "name": "Engie", "traits": ["Kind", "Neurotic"], ... },
        "target_pawn": { "name": "Fred", ... },
        "colony": { ... },
        "active_threads": [ ... ]
    },
    "tokens_estimated": 142,
    "memories_included": 3,
    "threads_included": 1,
    "filtered_out": {
        "memories_below_threshold": 1,
        "memories_over_limit": 0
    }
}
```

The bridge filters memories below `weight_threshold`, caps at `memory_limit`, and strips any sections disabled in `settings`. The prompt is ready to forward to the LLM.

### How Context Assembly Works

1. **Mod sends full packet** — everything about the pawns, colony, and situation
2. **Bridge filters by settings** — drops low-weight memories, optional sections
3. **Bridge builds prompt** — structured text optimized for small context windows
4. **Bridge returns both** — structured JSON data + assembled prompt text
5. **Mod decides** — use the bridge's prompt, modify it, or build their own from the data

### Event Types

The bridge generates different prompt instructions based on event type:

| Event Type | Instruction |
|------------|-------------|
| `relationship` | "Respond in character. Show how this relationship dynamic plays out." |
| `dialogue` | "Respond in character as this colonist." |
| `reaction` | "Describe this colonist's reaction to the situation." |
| `event` | "Generate a narrative event that fits this colony's current state." |
| `quest` | "Describe this quest-related interaction." |
| `custom` | Uses the `framing` text directly as the instruction. |

---

## LLM Proxy

OpenAI-compatible pass-through to local LLM backends.

### `GET /v1/models`
List loaded models. Auto-maps to the active model in LM Studio.

### `POST /v1/chat/completions`
Forward chat completion requests. Features:
- Single-concurrency request queue (protects consumer GPUs)
- Auto-model mapping (no need to paste model names)
- Response sanitization (strips markdown wrappers for clean C# parsing)
- SSE streaming support

---

## Dashboard

Real-time monitoring dashboard for mod developers:

- **Connection status** — bridge, LM Studio, RimWorld link state
- **GPU monitoring** — VRAM usage breakdown (LM Studio vs game vs system)
- **Activity logs** — SSE-streamed request/response log
- **Scalability planner** — calculates safe tick intervals based on latency
- **Context optimizer** — token budget allocation across prompt sections
- **Database panels** — colony overview, pawn list, memory feed, weight heatmap, narrative threads, mod registry

---

# Part 2: C# Mod — Game-Native Data

All persistent data lives in the RimWorld save file. The C# mod owns the data; the bridge just reads it.

## Data Storage via Scribe

RimWorld's `Scribe` system serializes custom data into the save XML. Three injection points:

### WorldComponent — Colony-Wide Data

```csharp
public class SynapseWorldComponent : WorldComponent
{
    // Narrative threads
    public List<NarrativeThread> narrativeThreads = new();

    // AI interaction history
    public List<InteractionRecord> interactionHistory = new();

    // Prompt audit log (last N entries)
    public List<PromptLogEntry> promptLog = new();

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref narrativeThreads, "narrativeThreads", LookMode.Deep);
        Scribe_Collections.Look(ref interactionHistory, "interactionHistory", LookMode.Deep);
        Scribe_Collections.Look(ref promptLog, "promptLog", LookMode.Deep);
    }
}
```

This gets serialized into the save as:
```xml
<li Class="RimSynapse.SynapseWorldComponent">
    <narrativeThreads>
        <li>
            <keyword>food_shortage</keyword>
            <description>Colony nearly starved during toxic fallout</description>
            <weight>0.6</weight>
            <decayRate>0.03</decayRate>
            <timesReferenced>4</timesReferenced>
        </li>
    </narrativeThreads>
    ...
</li>
```

### ThingComp on Pawns — Per-Pawn AI Data

```csharp
public class SynapsePawnComp : ThingComp
{
    // Weighted memories (bridge-only concept — game tales have no weights)
    public List<WeightedMemory> memories = new();

    // Opinion sample history (game computes opinions dynamically, never persists trajectory)
    public List<OpinionSample> opinionHistory = new();

    // AI personality notes (generated by LLM, persisted across sessions)
    public string personalitySummary;

    public override void CompExposeData()
    {
        base.CompExposeData();
        Scribe_Collections.Look(ref memories, "synapseMemories", LookMode.Deep);
        Scribe_Collections.Look(ref opinionHistory, "synapseOpinionHistory", LookMode.Deep);
        Scribe_Values.Look(ref personalitySummary, "synapsePersonality");
    }
}
```

### Data Classes

```csharp
public class WeightedMemory : IExposable
{
    public string summary;
    public string memoryType;     // raid, social, event, trade, quest
    public List<string> tags;
    public int gameTick;
    public float weight;          // 0.0 to 1.0
    public float baseWeight;
    public float decayRate;
    public int timesReferenced;

    public void ExposeData()
    {
        Scribe_Values.Look(ref summary, "summary");
        Scribe_Values.Look(ref memoryType, "memoryType");
        Scribe_Collections.Look(ref tags, "tags", LookMode.Value);
        Scribe_Values.Look(ref gameTick, "gameTick");
        Scribe_Values.Look(ref weight, "weight");
        Scribe_Values.Look(ref baseWeight, "baseWeight");
        Scribe_Values.Look(ref decayRate, "decayRate", 0.05f);
        Scribe_Values.Look(ref timesReferenced, "timesReferenced");
    }
}

public class OpinionSample : IExposable
{
    public string targetPawnId;
    public int opinion;
    public int gameTick;

    public void ExposeData()
    {
        Scribe_Values.Look(ref targetPawnId, "target");
        Scribe_Values.Look(ref opinion, "opinion");
        Scribe_Values.Look(ref gameTick, "tick");
    }
}

public class NarrativeThread : IExposable
{
    public string keyword;
    public string category;
    public string description;
    public float weight;
    public float decayRate;
    public int timesReferenced;
    public bool isResolved;
    public string resolutionSummary;

    public void ExposeData()
    {
        Scribe_Values.Look(ref keyword, "keyword");
        Scribe_Values.Look(ref category, "category");
        Scribe_Values.Look(ref description, "description");
        Scribe_Values.Look(ref weight, "weight");
        Scribe_Values.Look(ref decayRate, "decayRate", 0.03f);
        Scribe_Values.Look(ref timesReferenced, "timesReferenced");
        Scribe_Values.Look(ref isResolved, "isResolved");
        Scribe_Values.Look(ref resolutionSummary, "resolutionSummary");
    }
}
```

---

## Weight System (C# Side)

All weight math runs in C#. These are simple calculations that don't need a server.

### Memory Decay

Run once per in-game day (or configurable interval):

```csharp
public void RunDecayCycle()
{
    foreach (var memory in memories)
    {
        memory.weight = Mathf.Max(0f, memory.weight - memory.decayRate);
    }
    // Prune dead memories
    memories.RemoveAll(m => m.weight <= 0f);
}
```

### Memory Bump

When the LLM references a memory, bump its weight:

```csharp
public void BumpMemory(WeightedMemory memory, float amount = 0.2f)
{
    memory.weight = Mathf.Min(1f, memory.weight + amount);
    memory.timesReferenced++;
}
```

### Opinion Integral (Moving Average)

Sample opinions periodically and compute the trajectory:

```csharp
public float ComputeIntegral(List<OpinionSample> samples)
{
    if (samples.Count == 0) return 0f;
    return samples.Average(s => s.opinion);
}
```

---

## Pawn Data Packets

When the C# mod calls the bridge, it assembles a full packet from live game state:

```csharp
public static JObject BuildPawnPacket(Pawn pawn)
{
    var comp = pawn.GetComp<SynapsePawnComp>();
    return new JObject
    {
        ["pawn_id"] = pawn.ThingID,
        ["name"] = pawn.Name?.ToStringShort,
        ["gender"] = pawn.gender.ToString(),
        ["age"] = pawn.ageTracker.AgeBiologicalYears,
        ["backstory"] = GetBackstoryString(pawn),
        ["traits"] = new JArray(pawn.story?.traits?.allTraits?.Select(t => t.Label)),
        ["skills"] = BuildSkillsObject(pawn),
        ["ideology"] = pawn.Ideo?.name,
        ["needs"] = BuildNeedsObject(pawn),
        // Bridge-only data from our ThingComp:
        ["memories"] = BuildMemoriesArray(comp),
        ["relationships"] = BuildRelationshipsArray(pawn, comp),
    };
}
```

The mod pulls vanilla game data (name, traits, skills, needs) directly from the Pawn object, then appends our custom data (weighted memories, opinion history) from the ThingComp. The bridge receives everything in one packet.

---

# Part 3: Mod Vision

> This section describes what our **specific mod** will build on top of the platform.
> Other mods can use the bridge without any of this.

## Dynamic Pawn Psychology

**Goal:** Pawns develop persistent personality through AI interactions.

- LLM generates personality summaries based on events and traits
- Stored in `SynapsePawnComp.personalitySummary`
- Personality evolves as new memories accumulate
- Used as context in every interaction

## Faction & World Simulation

**Goal:** AI-driven faction behavior and diplomacy.

- `WorldComponent` tracks faction memory (trades, raids, gifts)
- LLM generates faction responses to player actions
- Diplomatic events can spawn from narrative threads

## Dynamic Chat Interactions

**Goal:** Talk to your colonists.

- Player initiates dialogue through in-game UI
- Mod builds pawn packet + conversation history
- Sends to bridge for context assembly -> LLM
- Response displayed in chat UI with opinion/mood effects

## Narrative Thread System

**Goal:** Events connect across time through keywords.

- When LLM generates a response, extract keywords
- Match against existing threads or create new ones
- Threads influence future prompts (e.g., "food_shortage" thread makes pawns more anxious about food)
- Threads can be resolved (the colony recovered from the drought)

## Relationship Integrals & Political Capital

**Goal:** Relationships have momentum, not just snapshots.

- Opinion samples taken every N ticks
- Integral (moving average) tells you the deep trend
- A relationship at +80 today but with an integral of -15 means it only recently improved
- LLM uses both current opinion AND integral for richer responses

---

# API Reference (Quick)

## Context Assembly
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/context/build` | Assemble context from game data packet |

## LLM Proxy
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/v1/models` | List loaded models |
| POST | `/v1/chat/completions` | Forward chat completion |

## Dashboard Stats
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/database/stats` | Dashboard statistics |
| GET | `/api/database/memories/recent` | Recent memory feed |
| GET | `/api/database/pawns` | Pawn list for dashboard |
| GET | `/api/database/threads` | Active narrative threads |
| GET | `/api/status` | Bridge + LM Studio status |
| GET | `/api/logs` | SSE log stream |
