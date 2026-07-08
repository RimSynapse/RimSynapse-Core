# Context Embedding — XML-Driven Configuration Addendum

> Approved design addendum. All configuration (prompts, weights, profiles,
> thought filters) is exposed via RimWorld's native Def XML system.
> Mod authors and players can modify behavior without touching C#.

---

## Def Types

| Def Class | Purpose | Default XML Location |
|---|---|---|
| `SynapsePromptDef` | System prompts per event type | `Defs/SynapsePrompts/` |
| `SynapseWeightDef` | Context slot base weights | `Defs/SynapseWeights/` |
| `SynapseContextProfileDef` | Event type → tier/budget profiles | `Defs/SynapseProfiles/` |
| `SynapseThoughtFilterDef` | Thought inclusion rules | `Defs/SynapseConfig/` |

## Resolution Order

```
1. SynapseContextProfileDef  → which tiers and budget fraction
2. SynapseWeightDef          → base weight per slot
3. Profile.weightOverrides   → per-event-type weight adjustments
4. ChatOptions.weightOverrides → per-request C# overrides (Storyteller boosting)
5. SynapsePromptDef          → system prompt (mod-specific → default fallback)
6. SynapseThoughtFilterDef   → thought inclusion rules
```

## Full specification

See the approved artifact for complete C# class definitions, XML examples,
XPath patching examples, and file layout.

*Approved 2026-07-07.*
