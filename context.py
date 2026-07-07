"""
RimSynapse Context Assembly — Stateless Prompt Builder
Receives a full game-state packet from the C# mod, filters by settings,
and returns structured data + a ready-to-use prompt.

No database, no state. Pure data transformation.
"""


def build_context(data: dict) -> dict:
    """
    Assemble context from a game-state packet.

    The mod sends event_type, pawn data, colony info, and framing.
    The bridge filters by settings and returns both structured data
    and an assembled prompt.
    """
    settings = data.get("settings", {})
    weight_threshold = settings.get("weight_threshold", 0.1)
    memory_limit = settings.get("memory_limit", 5)

    result = {
        "data": {},
        "prompt": "",
        "tokens_estimated": 0,
        "memories_included": 0,
        "threads_included": 0,
        "filtered_out": {
            "memories_below_threshold": 0,
            "memories_over_limit": 0,
        },
    }

    # Colony
    colony = data.get("colony")
    if colony and settings.get("include_colony", True):
        result["data"]["colony"] = colony

    # Source pawn
    source_pawn = data.get("source_pawn")
    if source_pawn:
        filtered = _filter_pawn(source_pawn, settings, weight_threshold, memory_limit, result)
        result["data"]["source_pawn"] = filtered

    # Target pawn
    target_pawn = data.get("target_pawn")
    if target_pawn:
        filtered = _filter_pawn(target_pawn, settings, weight_threshold, memory_limit, result)
        result["data"]["target_pawn"] = filtered

    # Narrative threads
    threads = data.get("narrative_threads", [])
    if settings.get("include_threads", True) and threads:
        filtered_threads = [t for t in threads if t.get("weight", 0) >= weight_threshold]
        result["data"]["active_threads"] = filtered_threads
        result["threads_included"] = len(filtered_threads)

    # Build prompt
    framing = data.get("framing", "")
    event_type = data.get("event_type", "general")
    result["prompt"] = _build_prompt(event_type, framing, result["data"])
    result["tokens_estimated"] = len(result["prompt"]) // 4

    return result


def _filter_pawn(pawn: dict, settings: dict, weight_threshold: float,
                 memory_limit: int, result: dict) -> dict:
    """Filter a pawn's data based on mod settings."""
    ctx = {"name": pawn.get("name", "Unknown")}

    # Pass through identity fields
    for field in ("pawn_id", "gender", "age", "ideology", "xenotype", "title"):
        if pawn.get(field):
            ctx[field] = pawn[field]

    # Traits
    if settings.get("include_traits", True) and pawn.get("traits"):
        ctx["traits"] = pawn["traits"]

    # Backstory
    if settings.get("include_backstory", True) and pawn.get("backstory"):
        ctx["backstory"] = pawn["backstory"]

    # Skills
    if settings.get("include_skills", False) and pawn.get("skills"):
        ctx["skills"] = pawn["skills"]

    # Needs
    if settings.get("include_needs", False) and pawn.get("needs"):
        ctx["needs"] = pawn["needs"]

    # Memories — filter by weight threshold, then cap at limit
    if settings.get("include_memories", True) and pawn.get("memories"):
        all_memories = pawn["memories"]
        above_threshold = [m for m in all_memories if m.get("weight", 0) >= weight_threshold]
        below_threshold = len(all_memories) - len(above_threshold)

        # Sort by weight descending, cap at limit
        above_threshold.sort(key=lambda m: m.get("weight", 0), reverse=True)
        over_limit = max(0, len(above_threshold) - memory_limit)
        capped = above_threshold[:memory_limit]

        ctx["memories"] = capped
        result["memories_included"] += len(capped)
        result["filtered_out"]["memories_below_threshold"] += below_threshold
        result["filtered_out"]["memories_over_limit"] += over_limit

    # Relationships
    if settings.get("include_relationships", True) and pawn.get("relationships"):
        ctx["relationships"] = pawn["relationships"]

    return ctx


def _build_prompt(event_type: str, framing: str, data: dict) -> str:
    """Build a prompt string from assembled context data."""
    parts = []

    # Source pawn identity
    source = data.get("source_pawn")
    if source:
        name = source.get("name", "Unknown")
        traits = source.get("traits", [])
        trait_str = ", ".join(traits) if isinstance(traits, list) else str(traits)
        parts.append(f"You are {name}" + (f", a {trait_str} colonist" if traits else "") + ".")

        backstory = source.get("backstory")
        if backstory:
            parts.append(f"Background: {backstory}.")

    # Framing / situation
    if framing:
        parts.append(f"\nSituation: {framing}")

    # Target pawn
    target = data.get("target_pawn")
    if target:
        target_name = target.get("name", "Unknown")
        target_traits = target.get("traits", [])
        trait_str = ", ".join(target_traits) if isinstance(target_traits, list) else str(target_traits)
        parts.append(f"\n{target_name}" + (f" ({trait_str})" if target_traits else "") + ":")

        # Relationship between source and target
        if source and source.get("relationships"):
            for rel in source["relationships"]:
                rel_with = rel.get("with", "")
                if rel_with == target_name:
                    opinion = rel.get("opinion", 0)
                    integral = rel.get("integral", 0)
                    rel_type = rel.get("type", "")
                    parts.append(
                        f"Your opinion of {target_name}: {opinion} "
                        f"(deep sentiment: {integral})"
                        + (f", {rel_type}" if rel_type else "")
                    )
                    break

    # Source memories
    if source and source.get("memories"):
        parts.append("\nRecent memories:")
        for m in source["memories"]:
            summary = m.get("summary", "")
            parts.append(f"- {summary}")

    # Target memories
    if target and target.get("memories"):
        target_name = target.get("name", "Unknown")
        parts.append(f"\n{target_name}'s recent memories:")
        for m in target["memories"]:
            parts.append(f"- {m.get('summary', '')}")

    # Narrative threads
    threads = data.get("active_threads", [])
    if threads:
        parts.append("\nActive rumors/events in the world:")
        for t in threads:
            parts.append(f"- {t.get('description', t.get('keyword', ''))}")

    # Colony
    colony = data.get("colony")
    if colony:
        colony_info = f"\nColony: {colony.get('name', 'Unknown')}"
        if colony.get("biome"):
            colony_info += f" ({colony['biome']})"
        if colony.get("pawn_count"):
            colony_info += f", {colony['pawn_count']} colonists"
        parts.append(colony_info)

    # Instruction based on event type
    instructions = {
        "relationship": "Respond in character. Show how this relationship dynamic plays out.",
        "dialogue": "Respond in character as this colonist.",
        "reaction": "Describe this colonist's reaction to the situation.",
        "event": "Generate a narrative event that fits this colony's current state.",
        "quest": "Describe this quest-related interaction.",
        "custom": framing,
    }
    instruction = instructions.get(event_type, "Respond in character.")
    if event_type != "custom":
        parts.append(f"\n{instruction}")

    return "\n".join(parts)
