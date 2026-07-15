# Future Features & MCP Architecture: RimSynapse Core

This document outlines the architectural roadmap and achievements for refactoring **RimSynapse Core** using a Model Context Protocol (MCP) / Function Calling model. Static C# metrics checks are fully refactored, and the LLM now queries live game state dynamically using registered tools.

---

## 1. Core Infrastructure (Completed)
- **Low-level HTTP Client & REST Routings**: `HttpEngine` and provider-specific translators (`OpenAiProvider`, `AnthropicProvider`, `GeminiProvider`) are fully optimized, budget-aware, and support native tool payloads (including Anthropic Messages API schemas).
- **Request Queue & Priority Scoring**: The asynchronous `RequestQueue` with mod-budget throttling, priority ranking, and TTL checks manages text/query requests.
- **Harmony Hooks & Letter Interception**: Low-level engine hooks (intercepting letter stacks, executing events, and recording raid outcomes) redirect narrative checks to Core.
- **Main-Thread Dispatcher**: The thread-safe dispatcher (`SynapseGameComponent.Enqueue`) and blocking AutoResetEvent synchronizer prevent multi-threading crashes when tools execute in the game loop.
- **Template Cache & Handshake Sync API**: `SynapseTemplateRegistry` stores templates as JSON files with SHA-256 fingerprints, and provides a synchronization handshake (`SyncHandshake`) to minimize registration overhead on reload.

---

## 2. Dynamic Tool Calling & Object Control (Completed)
Instead of pre-calculating and pushing static colony data packets, Core exposes twelve standardized MCP tools:
1. `get_colonists_profile`: Details colonist names, shooting/melee skills, traits, weapons, and visible health/injury conditions.
2. `get_stockpile_details`: Details quantities of resources (steel, wood, components, silver, medicine, and food nutrition) available on the map.
3. `get_active_threats`: Counts active hostiles (raiders, mechanoids), bug hives/infestations, crashed ship parts, and fires.
4. `get_colony_moods`: Lists mood percentages, mental break thresholds (Extreme, Major, Minor break risks), and negative thoughts.
5. `get_map_environment`: Exposes biome name, outdoor temperature, weather, overhead mountain cells, steam geyser count, and lists all map turrets, doors, and generators (including load IDs, Def names, coordinates, and active hack/cooldown status). Also details active network gateways (powered Comms Console) and nearby hacker bases.
6. `get_available_incidents`: Lists all storyteller incidents available to be fired, including their def names, base weights, description, and modder-supplied thematic guides (narrative context guidelines).
7. `fire_incident`: Executes a chosen incident defName immediately on the map with optional custom threat points overrides.
8. `possess_colonist`: Takes direct controller possession of a colonist, locking out manual player overrides, and directing them to perform actions with target-based and condition-based auto-release metrics.
9. `trigger_colonist_break`: Invokes a context-stripped secondary LLM solver to resolve critical mental breakdowns (suicide, homicide, crisis of faith, departure) safely, avoiding LLM safety blockages.
10. `control_turret`: Directly toggles turret power states, redirects targeting to friendly colonists (if sabotaged), or triggers an emergency bomb detonation (self-destruct).
11. `attempt_remote_hack`: Tries to remotely breach a target object's firewall. Requires active network gateways (comms console and nearby Hacker Base).
12. `spawn_hacker_base`: Spawns a hostile transceiver outpost on the world map within 8 tiles of the colony to begin hacking attempts.
13. `modify_pawn_state`: Apply direct modifications to a colonist's health (hediffs), traits, skills (using public properties), or conversion.
14. `modify_object_state`: Directly alter structures on the map (locks, power status, fuel levels, damage, fire).

---

## 3. NLP XML Modding & Action Console (Completed)
- **XML Triggers (`SynapseTriggerDef`)**: Modders can write plain English rules in XML Def files mapping hooks like `PawnInjured` or `PawnMentalBreak` to natural language conditions and instructions.
- **Trigger Manager (`SynapseTriggerManager`)**: When hooked events fire, automatically compiles target pawn state context and uses the LLM to parse rules against C# tool schemas to execute changes.
- **God Mode Console (`Dialog_GodMode`)**: Developer-facing console window in mod settings for executing plain-text actions (e.g., setting skills, locking doors, spawning fires) using MCP tools.
- **Dynamic [DebugAction] Scanner**: Reflects over LudeonTK's debug toolkit at runtime to dynamically register over 80+ pawn-related debug tools to the LLM, keeping them in the background (filtered out of storyteller pacing loops) to maintain minimal context windows.
