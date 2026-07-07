# Design Document – Context Embedding in RimSynapse Core

## Overview
The RimSynapse ecosystem consists of a **Core** library and several **companion mods** (Context, Psychology, Chat, Storyteller, DevTools).  Currently each companion mod implements its own logic for gathering game‑state data and building prompts.  This leads to duplicated code, inconsistent token budgeting, and makes it harder to guarantee that every request sent to LM Studio carries the same baseline context.

**Goal** – Introduce a **centralised Context‑Embedding layer** in the Core mod that:
1. Accepts a structured **ContextPacket** from any companion mod.
2. Stores the packet (in‑memory, optional persistence).
3. Merges the context into every outgoing LM Studio request automatically.
4. Exposes a small, well‑defined API for companion mods to push or clear context.
5. Provides UI knobs for users to enable/disable the feature and tune token budgets.

## Design Principles
- **Single Source of Truth** – All prompt‑building logic that depends on game state lives in one place (Core).
- **Low‑Coupling** – Companion mods interact with Core via a thin façade (`SynapseCoreContext.SetContext`, `ClearContext`).
- **Opt‑In** – Existing behaviour remains unchanged unless the user enables *Context Embedding* in settings.
- **Performance‑Aware** – Core enforces a **max‑context‑tokens** budget; overflow slots are trimmed according to weight (re‑using the slot‑weight logic already present in the Context mod).
- **Persist‑Optional** – Users may choose to persist the last context across saves for continuity, or keep it volatile for the current session only.

## Architecture
```
Core (RimSynapse‑Core)
├─ ModelManager
│   ├─ ActiveModel
│   └─ ContextPayload  ← new field (ContextPacket / JSON string)
├─ HttpEngine
│   └─ PostChatCompletionSync() merges ContextPayload → request body
├─ SynapseCoreContext (static façade)
│   ├─ SetContext(ContextPacket packet)
│   ├─ ClearContext()
│   └─ GetContext() : ContextPacket?
└─ Settings UI
    ├─ Enable Context Embedding (bool)
    ├─ Max Context Tokens (int)
    └─ Persist Context Across Saves (bool)
```

### Data Model – `ContextPacket`
```csharp
public class ContextPacket
{
    public string EventType;               // e.g. "dialogue", "relationship"
    public string Framing;                 // optional free‑form prompt framing
    public PawnPacket SourcePawn;          // may be null if not pawn‑specific
    public PawnPacket TargetPawn;          // optional second pawn
    public ColonyPacket Colony;            // high‑level colony state
    public List<NarrativeThread> Threads; // optional story threads
    public ContextSettings Settings;       // token/weight budget config
}

public class PawnPacket
{
    public string PawnId;
    public string Name;
    public string Gender;
    public int Age;
    public List<string> Traits;
    public List<WeightedMemory> Memories;
    public List<Relationship> Relationships;
    // …additional fields as needed
}

public class ContextSettings
{
    public bool IncludeMemories = true;
    public bool IncludeRelationships = true;
    public bool IncludeTraits = true;
    public int MaxTokens = 256;           // default budget for the whole packet
    public float WeightThreshold = 0.15f; // drop low‑weight slots first
}
```
The packet is serialized to JSON (`ContextPayload` stored as a string) before being merged into the LM Studio request body under a top‑level `"context"` property.

## API Surface (Core)
```csharp
public static class SynapseCoreContext
{
    // Sets the current context for the active model.
    public static void SetContext(ContextPacket packet);

    // Clears any stored context (e.g., when leaving a dialogue).
    public static void ClearContext();

    // Reads the current payload (mainly for DevTools display).
    public static ContextPacket? GetContext();
}
```
All methods are **thread‑safe** and no‑op if *Context Embedding* is disabled in settings.

## UI Changes (Core Settings)
- **Advanced Settings Tab** – Add three toggles/fields:
  1. **Enable Context Embedding** (checkbox).
  2. **Max Context Tokens** (numeric input, default 256).
  3. **Persist Context Across Saves** (checkbox). 
- When any toggle changes, the UI writes the values to `RimSynapseSettings` and calls `ModelManager.RefreshSettings()` to apply them immediately.

## Impact on Companion Mods
| Mod | Required Change |
|-----|-----------------|
| **Context** | Instead of building its own prompt, it now creates a `ContextPacket` and calls `SynapseCoreContext.SetContext(packet)`. The mod still decides which slots to include; Core will trim according to the user‑defined token budget. |
| **Psychology** | Before sending pawn‑specific requests, invoke `SetContext` with the pawn’s packet (including weighted memories). No further changes to its internal prompt logic. |
| **Chat** | On dialogue start, push a packet containing `SourcePawn`, `TargetPawn`, and optional `Framing`. On exit, call `ClearContext`. |
| **Storyteller** | Optional – attach the current `NarrativeThread` collection to the packet before generating a story event. |
| **DevTools** | Extend the dashboard panel to show **Current Context Size**, **Token Estimate**, and a **Raw JSON** preview. |

## Persistence
- When *Persist Context Across Saves* is enabled, Core creates a hidden `WorldComponent` (`SynapseCoreContextComponent`) that stores the raw JSON string via `Scribe_Values.Look`. On load, the component restores the payload and re‑populates `ModelManager.ContextPayload`.
- If the option is disabled, the context lives only in memory and is cleared on game exit.

## Testing Strategy
### Unit Tests
- Serialize/deserialize `ContextPacket` round‑trip.
- Verify `ModelManager` appends a `"context"` property only when enabled.
- Ensure token‑budget trimming drops the lowest‑weight optional slots first.

### Integration Tests
- Spin up a minimal game instance (using the existing test harness) and simulate a dialogue start → verify the LM Studio request contains a correctly‑structured `context` JSON.
- Disable the feature → verify the request has no `context` field.

### Manual QA
1. Launch RimWorld with Core + a companion mod (e.g., Chat).
2. Enable *Context Embedding* and set a low token limit (e.g., 100).
3. Start a conversation; inspect LM Studio logs for the `context` object and token count.
4. Toggle *Persist Context*; save & reload; repeat step 3 to confirm persistence.
5. Disable the feature; confirm the request payload is unchanged.

## Migration Path & Release Checklist
1. **Core Changes** – add `ContextPayload`, API, UI, persistence (PR #X).  
2. **Companion Mod Updates** – each repo adds a single call to `SynapseCoreContext.SetContext` (PRs #Y‑#Z).  
3. **Version Bump** – Core version `1.1.0` (adds Context feature).  
4. **Documentation** – update `README.md` and add this design doc under `docs/DESIGN_CONTEXT_EMBEDDING.md`.  
5. **Changelog** – note “Context embedding now centralised in Core; companion mods simplified”.  
6. **Release** – publish Core package, notify mod authors to pull the latest version.

---
*Prepared by Antigravity – the AI coding assistant.*
