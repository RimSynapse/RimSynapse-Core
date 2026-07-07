"""
RimSynapse Prompt Engine — Template Registry + Raw Pass-through

Two modes for mod developers:
1. TEMPLATE MODE (efficient): Register a template once, fill slots at runtime
2. RAW MODE (flexible): Send the full prompt/context each time

Both return a ready-to-use prompt for the LLM proxy.
"""
import hashlib
import json
import re
import time
from typing import Optional


# Engine boot time — mods compare this to know if bridge restarted
_boot_time = time.time()


# In-memory template registry: { template_id: TemplateRecord }
_templates = {}


class TemplateRecord:
    """A registered prompt template with weighted slots."""

    def __init__(self, mod_id: str, template_id: str, template: str,
                 slots: dict, max_tokens: Optional[int] = None,
                 description: str = ""):
        self.mod_id = mod_id
        self.template_id = template_id
        self.template = template          # Template string with {{slot_name}} placeholders
        self.slots = slots                # { slot_name: { weight, required, default, type } }
        self.max_tokens = max_tokens      # Optional token budget
        self.description = description
        self.fill_count = 0               # How many times this template has been used
        self.registered_at = time.time()
        self.fingerprint = self._compute_fingerprint()

    def _compute_fingerprint(self) -> str:
        """Hash of template + slots — mods compare this to detect drift."""
        payload = json.dumps({
            "template": self.template,
            "slots": self.slots,
            "max_tokens": self.max_tokens,
        }, sort_keys=True)
        return hashlib.sha256(payload.encode()).hexdigest()[:12]

    def to_dict(self):
        return {
            "mod_id": self.mod_id,
            "template_id": self.template_id,
            "template": self.template,
            "slots": self.slots,
            "max_tokens": self.max_tokens,
            "description": self.description,
            "fill_count": self.fill_count,
            "fingerprint": self.fingerprint,
            "registered_at": self.registered_at,
        }


# ---------------------------------------------------------------------------
# Template Registration
# ---------------------------------------------------------------------------

def register_template(data: dict) -> dict:
    """
    Register a prompt template with weighted slots.

    Args:
        data: {
            "mod_id": "rimsynapse",
            "template_id": "relationship_dialogue",
            "template": "You are {{pawn_name}}, a {{traits}} colonist...",
            "slots": {
                "pawn_name": { "weight": 10, "required": true },
                "traits":    { "weight": 8 },
                ...
            },
            "max_tokens": 200,       # optional
            "description": "..."     # optional
        }

    Returns: { "status": "registered", "template_id": "...", ... }
    """
    mod_id = data.get("mod_id", "unknown")
    template_id = data.get("template_id")
    if not template_id:
        return {"error": "template_id is required"}

    template_str = data.get("template", "")
    if not template_str:
        return {"error": "template is required"}

    slots = data.get("slots", {})

    # Normalize slot definitions
    normalized_slots = {}
    for name, config in slots.items():
        if isinstance(config, dict):
            normalized_slots[name] = {
                "weight": config.get("weight", 5),
                "required": config.get("required", False),
                "default": config.get("default", ""),
                "type": config.get("type", "string"),
            }
        else:
            # Simple weight-only definition: "pawn_name": 10
            normalized_slots[name] = {
                "weight": config if isinstance(config, (int, float)) else 5,
                "required": False,
                "default": "",
                "type": "string",
            }

    # Check that all template placeholders have slot definitions
    placeholders = set(re.findall(r'\{\{(\w+)\}\}', template_str))
    undefined = placeholders - set(normalized_slots.keys())
    if undefined:
        # Auto-register undefined slots with default weight
        for name in undefined:
            normalized_slots[name] = {
                "weight": 5,
                "required": False,
                "default": "",
                "type": "string",
            }

    record = TemplateRecord(
        mod_id=mod_id,
        template_id=template_id,
        template=template_str,
        slots=normalized_slots,
        max_tokens=data.get("max_tokens"),
        description=data.get("description", ""),
    )

    is_update = template_id in _templates
    _templates[template_id] = record

    return {
        "status": "updated" if is_update else "registered",
        "template_id": template_id,
        "mod_id": mod_id,
        "slot_count": len(normalized_slots),
        "placeholders": sorted(placeholders),
        "fingerprint": record.fingerprint,
        "boot_time": _boot_time,
    }


def list_templates() -> list:
    """List all registered templates."""
    return [t.to_dict() for t in _templates.values()]


def get_template(template_id: str) -> Optional[dict]:
    """Get a single template by ID."""
    t = _templates.get(template_id)
    return t.to_dict() if t else None


def unregister_template(template_id: str) -> dict:
    """Remove a registered template."""
    if template_id in _templates:
        del _templates[template_id]
        return {"status": "removed", "template_id": template_id}
    return {"error": "template not found", "template_id": template_id}


def verify_handshake(data: dict) -> dict:
    """
    Handshake verification. Mod sends its known template IDs + fingerprints.
    Bridge responds with what's still cached vs. what needs re-registration.

    Args:
        data: {
            "mod_id": "rimsynapse",
            "boot_time": 1720300000.0,        # last known bridge boot time
            "templates": {
                "relationship_dialogue": "a1b2c3d4e5f6",  # template_id: fingerprint
                "quest_intro": "f6e5d4c3b2a1",
            }
        }

    Returns: {
        "status": "ok" | "stale" | "missing",
        "bridge_boot_time": 1720300000.0,
        "bridge_restarted": false,
        "templates": {
            "relationship_dialogue": { "status": "ok", "fingerprint": "a1b2c3d4e5f6" },
            "quest_intro":           { "status": "missing", "fingerprint": null },
        },
        "action_required": ["quest_intro"]  # list of template_ids to re-register
    }
    """
    mod_boot_time = data.get("boot_time", 0)
    known_templates = data.get("templates", {})
    bridge_restarted = mod_boot_time != 0 and mod_boot_time != _boot_time

    results = {}
    action_required = []

    for template_id, expected_fingerprint in known_templates.items():
        record = _templates.get(template_id)
        if record is None:
            results[template_id] = {"status": "missing", "fingerprint": None}
            action_required.append(template_id)
        elif record.fingerprint != expected_fingerprint:
            results[template_id] = {
                "status": "stale",
                "fingerprint": record.fingerprint,
                "expected": expected_fingerprint,
            }
            action_required.append(template_id)
        else:
            results[template_id] = {"status": "ok", "fingerprint": record.fingerprint}

    overall = "ok"
    if action_required:
        overall = "missing" if bridge_restarted else "stale"

    return {
        "status": overall,
        "bridge_boot_time": _boot_time,
        "bridge_restarted": bridge_restarted,
        "templates": results,
        "action_required": action_required,
    }


# ---------------------------------------------------------------------------
# Prompt Fill (Template Mode)
# ---------------------------------------------------------------------------

def fill_template(data: dict) -> dict:
    """
    Fill a registered template with slot values.

    Args:
        data: {
            "template_id": "relationship_dialogue",
            "slots": {
                "pawn_name": "Fred",
                "traits": "Greedy, Tough",
                "event": "argued about food rations",
            },
            "max_tokens": 150  # optional override
        }

    Returns: {
        "prompt": "You are Fred, a Greedy, Tough colonist...",
        "template_id": "...",
        "tokens_estimated": 42,
        "slots_filled": ["pawn_name", "traits", "event"],
        "slots_dropped": ["backstory"],
        "slots_missing": []
    }
    """
    template_id = data.get("template_id")
    if not template_id:
        return {"error": "template_id is required"}

    record = _templates.get(template_id)
    if not record:
        return {"error": f"Template '{template_id}' not found. Register it first via POST /api/template/register"}

    slot_values = data.get("slots", {})
    max_tokens = data.get("max_tokens") or record.max_tokens

    # Check required slots
    missing = []
    for name, config in record.slots.items():
        if config["required"] and name not in slot_values:
            if config["default"]:
                slot_values[name] = config["default"]
            else:
                missing.append(name)

    if missing:
        return {
            "error": f"Missing required slots: {missing}",
            "template_id": template_id,
            "slots_missing": missing,
        }

    # Sort optional slots by weight (lowest first = drop first)
    optional_slots = [
        (name, config["weight"])
        for name, config in record.slots.items()
        if not config["required"] and name in slot_values
    ]
    optional_slots.sort(key=lambda x: x[1])  # lowest weight first

    # Build the prompt — start with all slots filled
    filled_slots = set()
    dropped_slots = []

    def _render(values: dict) -> str:
        """Render the template with the given values."""
        result = record.template
        for name, value in values.items():
            placeholder = "{{" + name + "}}"
            if isinstance(value, list):
                # Render arrays as bulleted list
                rendered = "\n".join(f"- {item}" for item in value)
            else:
                rendered = str(value)
            result = result.replace(placeholder, rendered)
        # Clean up any unfilled optional placeholders
        result = re.sub(r'\{\{\w+\}\}', '', result)
        # Clean up empty lines left by removed placeholders
        result = re.sub(r'\n\s*\n\s*\n', '\n\n', result)
        return result.strip()

    # First pass: render with everything
    prompt = _render(slot_values)
    filled_slots = set(slot_values.keys())
    tokens_est = len(prompt) // 4

    # If over budget, drop lowest-weight optional slots until it fits
    if max_tokens and tokens_est > max_tokens:
        working_values = dict(slot_values)
        for name, weight in optional_slots:
            if tokens_est <= max_tokens:
                break
            del working_values[name]
            filled_slots.discard(name)
            dropped_slots.append(name)
            prompt = _render(working_values)
            tokens_est = len(prompt) // 4

    record.fill_count += 1

    return {
        "prompt": prompt,
        "template_id": template_id,
        "mod_id": record.mod_id,
        "tokens_estimated": tokens_est,
        "slots_filled": sorted(filled_slots),
        "slots_dropped": dropped_slots,
        "slots_missing": [],
    }


# ---------------------------------------------------------------------------
# Raw Prompt (Pass-through Mode)
# ---------------------------------------------------------------------------

def build_raw_prompt(data: dict) -> dict:
    """
    Raw mode: The mod sends the complete prompt or context packet.
    The bridge just packages it for the LLM.

    Accepts either:
    - { "prompt": "full prompt text" }  — direct pass-through
    - { "messages": [...] }             — chat format pass-through
    - { "system": "...", "user": "..." } — simple system+user format

    Returns: {
        "prompt": "...",
        "messages": [...],
        "tokens_estimated": N,
        "mode": "raw"
    }
    """
    # Direct prompt text
    if "prompt" in data:
        prompt = data["prompt"]
        return {
            "prompt": prompt,
            "messages": [{"role": "user", "content": prompt}],
            "tokens_estimated": len(prompt) // 4,
            "mode": "raw",
        }

    # Chat messages format
    if "messages" in data:
        messages = data["messages"]
        total_text = " ".join(m.get("content", "") for m in messages)
        return {
            "prompt": total_text,
            "messages": messages,
            "tokens_estimated": len(total_text) // 4,
            "mode": "raw",
        }

    # Simple system + user format
    system = data.get("system", "")
    user = data.get("user", "")
    messages = []
    if system:
        messages.append({"role": "system", "content": system})
    if user:
        messages.append({"role": "user", "content": user})

    total_text = f"{system} {user}".strip()
    return {
        "prompt": total_text,
        "messages": messages,
        "tokens_estimated": len(total_text) // 4,
        "mode": "raw",
    }


# ---------------------------------------------------------------------------
# Session Stats (for dashboard)
# ---------------------------------------------------------------------------

def get_engine_stats() -> dict:
    """Return stats about the prompt engine for the dashboard."""
    total_fills = sum(t.fill_count for t in _templates.values())
    return {
        "templates_registered": len(_templates),
        "total_fills": total_fills,
        "templates": [
            {
                "template_id": t.template_id,
                "mod_id": t.mod_id,
                "slot_count": len(t.slots),
                "fill_count": t.fill_count,
                "description": t.description,
            }
            for t in _templates.values()
        ],
    }
