"""
RimSynapse Bridge — Python Backend
A secure HTTPS local bridge connecting RimWorld mods to local LLM backends
like LM Studio and Ollama. 1:1 port of the original Node.js/Express server.
"""

# Bootstrap: ensure local lib/ directory is on sys.path for portable distribution
import sys
import os
_base_dir = os.path.dirname(os.path.abspath(__file__))
_lib_dir = os.path.join(_base_dir, "lib")
if os.path.isdir(_lib_dir) and _lib_dir not in sys.path:
    sys.path.insert(0, _lib_dir)


import json
import os
import re
import ssl
import subprocess
import sys
import threading
import time
import uuid
from collections import deque
from datetime import datetime, timezone
from pathlib import Path
from queue import Queue, Empty

from flask import Flask, request, jsonify, Response, send_from_directory
from flask_cors import CORS
from context import build_context

# ---------------------------------------------------------------------------
# Paths & Constants
# ---------------------------------------------------------------------------
BASE_DIR = Path(__file__).resolve().parent
CONFIG_FILE = BASE_DIR / "config.json"
PFX_PATH = BASE_DIR / "certificate.pfx"
PFX_PASS = "rimmind-bridge"
PUBLIC_DIR = BASE_DIR / "public"
QUERY_GPU_SCRIPT = BASE_DIR / "query_gpu.ps1"

# ---------------------------------------------------------------------------
# GPU VRAM & Performance Monitoring — PowerShell script content
# ---------------------------------------------------------------------------
PS_SCRIPT = r"""$counters = Get-Counter -Counter '\\GPU Process Memory(*)\Dedicated Usage' -ErrorAction SilentlyContinue
if ($counters) {
    $results = @()
    foreach ($s in $counters.CounterSamples) {
        if ($s.CookedValue -gt 1MB) {
            $start = $s.Path.IndexOf("(")
            $end = $s.Path.IndexOf(")")
            if ($start -ge 0 -and $end -gt $start) {
                $instance = $s.Path.Substring($start + 1, $end - $start - 1)
                if ($instance -like "pid_*") {
                    $parts = $instance.Split("_")
                    if ($parts.Count -gt 1) {
                        $targetPid = $parts[1]
                        $proc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
                        if ($proc -and ($proc.Name -like "*RimWorld*" -or $proc.Name -like "*LM Studio*" -or $proc.Name -like "*llama*")) {
                            $exists = $false
                            foreach ($r in $results) {
                                if ($r.Pid -eq $targetPid) { $exists = $true; break }
                            }
                            if (-not $exists) {
                                $results += [PSCustomObject]@{
                                    Pid = $targetPid
                                    Name = $proc.Name
                                    VramMB = [math]::round($s.CookedValue / 1MB, 2)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    $results | ConvertTo-Json -Compress
} else {
    Write-Host "[]"
}"""


def write_ps_script():
    """Write the GPU monitoring PowerShell script to disk."""
    try:
        QUERY_GPU_SCRIPT.write_text(PS_SCRIPT, encoding="utf-8")
    except Exception as e:
        print(f"Failed to write query_gpu.ps1: {e}")


def fetch_gpu_stats() -> dict:
    """Query nvidia-smi and per-process VRAM usage."""
    stats = {
        "supported": False,
        "utilization": 0,
        "usedVram": 0,
        "totalVram": 0,
        "processes": [],
    }

    try:
        result = subprocess.run(
            [
                "nvidia-smi",
                "--query-gpu=utilization.gpu,memory.used,memory.total",
                "--format=csv,noheader,nounits",
            ],
            capture_output=True,
            text=True,
            timeout=5,
        )
        if result.returncode == 0:
            parts = result.stdout.strip().split(",")
            if len(parts) >= 3:
                stats["supported"] = True
                stats["utilization"] = int(parts[0].strip() or 0)
                stats["usedVram"] = int(parts[1].strip() or 0) / 1024
                stats["totalVram"] = int(parts[2].strip() or 0) / 1024
    except Exception:
        pass

    # Per-process VRAM via PowerShell
    try:
        ps_result = subprocess.run(
            [
                "powershell",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(QUERY_GPU_SCRIPT),
            ],
            capture_output=True,
            text=True,
            timeout=10,
        )
        if ps_result.returncode == 0 and ps_result.stdout.strip():
            json_str = ps_result.stdout.strip()
            start_idx = json_str.find("[")
            if start_idx != -1:
                json_str = json_str[start_idx:]
                parsed = json.loads(json_str)
                stats["processes"] = parsed if isinstance(parsed, list) else [parsed]
    except Exception:
        pass

    return stats


# ---------------------------------------------------------------------------
# Global Metrics & Capacity Tracking
# ---------------------------------------------------------------------------
recent_requests_metrics: list[dict] = []
active_pawns_tracker: dict[str, float] = {}  # name -> timestamp


def prune_active_pawns():
    """Remove pawns not seen in the last 10 minutes."""
    cutoff = time.time() - 600
    stale = [name for name, ts in active_pawns_tracker.items() if ts < cutoff]
    for name in stale:
        del active_pawns_tracker[name]


# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
config = {
    "port": 3001,
    "lmStudioUrl": "http://127.0.0.1:1234",
    "bridgeApiKey": "rimsynapse-bridge",
    "lmStudioApiKey": "",
    "autoMapModel": True,
    "sanitizeResponse": True,
    "timeoutMs": 120000,
    "queueConcurrency": 1,
}

if CONFIG_FILE.exists():
    try:
        with open(CONFIG_FILE, "r", encoding="utf-8") as f:
            config.update(json.load(f))
    except Exception as err:
        print(f"Error reading config file, using defaults: {err}")

# Override default port to 3001 for the Python backend
if config.get("port") == 3000:
    config["port"] = 3001


def save_config() -> bool:
    try:
        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            json.dump(config, f, indent=2)
        return True
    except Exception as err:
        print(f"Error saving config file: {err}")
        return False


# ---------------------------------------------------------------------------
# In-Memory Log Buffer & SSE Clients
# ---------------------------------------------------------------------------
log_buffer: list[dict] = []
sse_clients: list = []
sse_clients_lock = threading.Lock()

# Request Queue
request_queue: Queue = Queue()
processing_queue = False
queue_size = 0
queue_lock = threading.Lock()


def log_to_dashboard(level: str, log_type: str, message: str, details: dict | None = None):
    """Broadcast a log entry to all SSE clients and console."""
    log_entry = {
        "id": f"{int(time.time() * 1000)}-{uuid.uuid4().hex[:9]}",
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "level": level,
        "type": log_type,
        "message": message,
        "details": details,
    }

    log_buffer.append(log_entry)
    if len(log_buffer) > 100:
        log_buffer.pop(0)

    # Track prompt/completion tokens for capacity metrics
    if log_type == "proxy" and level == "success" and details:
        if "promptTokens" in details and "completionTokens" in details:
            recent_requests_metrics.append(
                {
                    "timestamp": time.time() * 1000,
                    "promptTokens": details["promptTokens"],
                    "completionTokens": details["completionTokens"],
                    "durationMs": details.get("durationMs", 0),
                }
            )
            if len(recent_requests_metrics) > 50:
                recent_requests_metrics.pop(0)

    # Stream to all connected SSE clients
    data_line = f"data: {json.dumps(log_entry)}\n\n"
    with sse_clients_lock:
        dead = []
        for i, q in enumerate(sse_clients):
            try:
                q.put_nowait(data_line)
            except Exception:
                dead.append(i)
        for i in reversed(dead):
            sse_clients.pop(i)

    print(f"[{log_entry['timestamp']}] [{level.upper()}] [{log_type}] {message}")


# ---------------------------------------------------------------------------
# Request Queue Worker
# ---------------------------------------------------------------------------
def queue_worker():
    """Background thread that processes queued requests one at a time."""
    global processing_queue, queue_size
    while True:
        task_func, task_event = request_queue.get()
        with queue_lock:
            processing_queue = True
            queue_size = request_queue.qsize()

        log_to_dashboard("info", "queue", f"Processing request in queue. Remaining: {request_queue.qsize()}")

        try:
            task_func()
        except Exception as err:
            log_to_dashboard("error", "queue", f"Error during task execution: {err}")
        finally:
            task_event.set()
            with queue_lock:
                queue_size = request_queue.qsize()
                if queue_size == 0:
                    processing_queue = False
                    log_to_dashboard("info", "queue", "Queue is now empty.")
            request_queue.task_done()


# Start worker thread
_worker_thread = threading.Thread(target=queue_worker, daemon=True)
_worker_thread.start()

# ---------------------------------------------------------------------------
# Response Sanitization
# ---------------------------------------------------------------------------

def sanitize_message_content(content: str, is_pawn_dialogue: bool = False) -> str:
    """Clean markdown wrappers, strip think blocks, repair JSON, wrap plain text fallbacks."""
    if not content or not isinstance(content, str):
        return content

    cleaned = content.strip()

    # 1. Remove <think>...</think> blocks
    cleaned = re.sub(r"<think>[\s\S]*?</think>", "", cleaned, flags=re.IGNORECASE).strip()

    # 2. Match ```json <content> ``` or ``` <content> ```
    md_json_match = re.match(r"^```(?:json)?\s*([\s\S]*?)\s*```$", cleaned, re.IGNORECASE)
    if md_json_match and md_json_match.group(1):
        log_to_dashboard("success", "proxy", "Successfully cleaned markdown code blocks from LLM output.")
        cleaned = md_json_match.group(1).strip()

    # 3. Double-check think blocks again
    cleaned = re.sub(r"<think>[\s\S]*?</think>", "", cleaned, flags=re.IGNORECASE).strip()

    # 4. Robust JSON check & repair for Pawn Dialogues
    looks_like_json = cleaned.startswith("{") or '"reply"' in cleaned or '"thought"' in cleaned
    if is_pawn_dialogue or looks_like_json:
        parsed = None
        json_str = cleaned

        start_idx = json_str.find("{")
        end_idx = json_str.rfind("}")
        if start_idx != -1 and end_idx != -1 and end_idx > start_idx:
            json_str = json_str[start_idx : end_idx + 1]

        try:
            parsed = json.loads(json_str)
        except json.JSONDecodeError:
            try:
                repaired = re.sub(r",\s*}", "}", json_str)
                repaired = re.sub(r",\s*\]", "]", repaired)
                parsed = json.loads(repaired)
            except json.JSONDecodeError:
                pass

        if parsed:
            if "reply" in parsed or "thought" in parsed or is_pawn_dialogue:
                if "reply" not in parsed:
                    parsed["reply"] = ""
                if "thought" not in parsed or not isinstance(parsed.get("thought"), dict) or parsed["thought"] is None:
                    parsed["thought"] = {
                        "tag": "NEUTRAL",
                        "description": "Responded",
                        "relation_delta": 0,
                    }
                else:
                    thought = parsed["thought"]
                    if "tag" not in thought:
                        thought["tag"] = "NEUTRAL"
                    if "description" not in thought:
                        thought["description"] = "Responded"
                    if "relation_delta" not in thought:
                        thought["relation_delta"] = 0
                    else:
                        try:
                            thought["relation_delta"] = int(thought["relation_delta"])
                        except (ValueError, TypeError):
                            thought["relation_delta"] = 0

                parsed["relation_delta"] = parsed["thought"]["relation_delta"]
                return json.dumps(parsed)
        elif is_pawn_dialogue:
            try:
                wrapped = {
                    "reply": cleaned,
                    "thought": {
                        "tag": "NEUTRAL",
                        "description": "Responded",
                        "relation_delta": 0,
                    },
                    "relation_delta": 0,
                }
                log_to_dashboard("warn", "proxy", "Wrapped plain-text response into valid JSON dialogue format.")
                return json.dumps(wrapped)
            except Exception:
                pass

    return cleaned


# ---------------------------------------------------------------------------
# LM Studio Helpers
# ---------------------------------------------------------------------------
import requests as http_requests


def fetch_lm_studio_models() -> dict:
    """Check connectivity to LM Studio and fetch loaded models."""
    try:
        headers = {}
        if config.get("lmStudioApiKey"):
            headers["Authorization"] = f"Bearer {config['lmStudioApiKey']}"

        resp = http_requests.get(
            f"{config['lmStudioUrl']}/v1/models",
            headers=headers,
            timeout=5,
        )
        resp.raise_for_status()
        data = resp.json()
        return {"online": True, "models": data.get("data", [])}
    except Exception as err:
        return {"online": False, "error": str(err), "models": []}


def fetch_lm_studio_active_context() -> dict | None:
    """Fetch detailed info about loaded model context size from native LM Studio API."""
    try:
        headers = {}
        if config.get("lmStudioApiKey"):
            headers["Authorization"] = f"Bearer {config['lmStudioApiKey']}"

        resp = http_requests.get(
            f"{config['lmStudioUrl']}/api/v1/models",
            headers=headers,
            timeout=5,
        )
        if not resp.ok:
            return None
        data = resp.json()
        if not data.get("models"):
            return None

        for m in data["models"]:
            if (
                m.get("type") == "llm"
                and m.get("loaded_instances")
                and len(m["loaded_instances"]) > 0
            ):
                instance = m["loaded_instances"][0]
                if instance.get("config", {}).get("context_length"):
                    return {
                        "modelId": instance["id"],
                        "contextLength": instance["config"]["context_length"],
                    }
        return None
    except Exception:
        return None


# ---------------------------------------------------------------------------
# RimWorld Config Helpers
# ---------------------------------------------------------------------------

def get_rimworld_config_path() -> str | None:
    user_profile = os.environ.get("USERPROFILE") or os.environ.get("HOME") or ""
    if not user_profile:
        return None
    return os.path.join(
        user_profile, "AppData", "LocalLow", "Ludeon Studios",
        "RimWorld by Ludeon Studios", "Config",
    )


def find_rimmind_config_file() -> str | None:
    config_dir = get_rimworld_config_path()
    if not config_dir or not os.path.isdir(config_dir):
        return None
    try:
        for f in os.listdir(config_dir):
            if f.startswith("Mod_") and (
                f.endswith("_RimMindCoreMod.xml")
                or f.endswith("_RimSynapseCoreMod.xml")
                or "rimsynapse" in f.lower()
            ):
                return os.path.join(config_dir, f)
        return None
    except Exception as err:
        print(f"Error scanning RimWorld config directory: {err}")
        return None


def read_rimmind_config(custom_file_path: str | None = None) -> dict:
    file_path = custom_file_path or find_rimmind_config_file()
    default_dir = get_rimworld_config_path() or ""
    default_file = os.path.join(default_dir, "Mod_3707741395_RimMindCoreMod.xml") if default_dir else ""

    if not file_path or not os.path.isfile(file_path):
        return {
            "found": False,
            "searchPath": default_dir,
            "filePath": file_path or default_file,
            "fileName": os.path.basename(file_path) if file_path else "Mod_3707741395_RimMindCoreMod.xml",
            "apiKey": "",
            "apiEndpoint": "",
            "modelName": "",
        }

    try:
        with open(file_path, "r", encoding="utf-8") as f:
            xml = f.read()

        is_rimchat = "rimchat" in os.path.basename(file_path).lower()

        api_key = ""
        api_endpoint = ""
        model_name = ""

        if is_rimchat:
            m = re.search(r"<LocalConfig>[\s\S]*?<baseUrl>([\s\S]*?)</baseUrl>", xml)
            api_endpoint = m.group(1).strip() if m else ""
            m = re.search(r"<LocalConfig>[\s\S]*?<modelName>([\s\S]*?)</modelName>", xml)
            model_name = m.group(1).strip() if m else ""
            m = re.search(r"<provider>Custom</provider>[\s\S]*?<apiKey>([\s\S]*?)</apiKey>", xml)
            api_key = m.group(1).strip() if m else ""
            if not api_endpoint:
                m = re.search(r"<provider>Custom</provider>[\s\S]*?<baseUrl>([\s\S]*?)</baseUrl>", xml)
                api_endpoint = m.group(1).strip() if m else ""
            if not model_name:
                m = re.search(r"<provider>Custom</provider>[\s\S]*?<customModelName>([\s\S]*?)</customModelName>", xml)
                model_name = m.group(1).strip() if m else ""
        else:
            m = re.search(r"<apiKey>([\s\S]*?)</apiKey>", xml)
            api_key = m.group(1).strip() if m else ""
            m = re.search(r"<apiEndpoint>([\s\S]*?)</apiEndpoint>", xml)
            api_endpoint = m.group(1).strip() if m else ""
            m = re.search(r"<modelName>([\s\S]*?)</modelName>", xml)
            model_name = m.group(1).strip() if m else ""

        return {
            "found": True,
            "filePath": file_path,
            "fileName": os.path.basename(file_path),
            "apiKey": api_key,
            "apiEndpoint": api_endpoint,
            "modelName": model_name,
        }
    except Exception as err:
        return {
            "found": False,
            "searchPath": default_dir,
            "filePath": file_path or default_file,
            "fileName": os.path.basename(file_path) if file_path else "Mod_3707741395_RimMindCoreMod.xml",
            "error": str(err),
        }


def update_rimmind_config(endpoint: str, api_key: str, model_name: str, custom_file_path: str | None = None) -> dict:
    file_path = custom_file_path or find_rimmind_config_file()
    default_dir = get_rimworld_config_path() or ""
    target_path = file_path or (os.path.join(default_dir, "Mod_3707741395_RimMindCoreMod.xml") if default_dir else None)

    if not target_path:
        return {"success": False, "message": "RimWorld config directory could not be resolved."}

    is_rimchat = "rimchat" in os.path.basename(target_path).lower()

    if not os.path.isfile(target_path):
        try:
            folder = os.path.dirname(target_path)
            os.makedirs(folder, exist_ok=True)

            if is_rimchat:
                clean_ep = endpoint.rstrip("/")
                default_xml = (
                    f'<?xml version="1.0" encoding="utf-8"?>\n'
                    f"<SettingsBlock>\n"
                    f'\t<ModSettings Class="RimChat.RimChatSettings">\n'
                    f"\t\t<CloudConfigs>\n"
                    f"\t\t\t<li>\n"
                    f"\t\t\t\t<provider>Custom</provider>\n"
                    f"\t\t\t\t<apiKey>{api_key}</apiKey>\n"
                    f"\t\t\t\t<selectedModel>Custom</selectedModel>\n"
                    f"\t\t\t\t<customModelName>{model_name}</customModelName>\n"
                    f"\t\t\t\t<baseUrl>{clean_ep}</baseUrl>\n"
                    f"\t\t\t</li>\n"
                    f"\t\t</CloudConfigs>\n"
                    f"\t\t<LocalConfig>\n"
                    f"\t\t\t<baseUrl>{endpoint}</baseUrl>\n"
                    f"\t\t\t<modelName>{model_name}</modelName>\n"
                    f"\t\t</LocalConfig>\n"
                    f"\t</ModSettings>\n"
                    f"</SettingsBlock>"
                )
            else:
                is_rimsynapse = "rimsynapse" in os.path.basename(target_path).lower()
                settings_class = "RimSynapse.RimSynapseCoreSettings" if is_rimsynapse else "RimMind.RimMindCoreSettings"
                default_xml = (
                    f'<?xml version="1.0" encoding="utf-8"?>\n'
                    f'<ModSettings Class="{settings_class}">\n'
                    f"\t<apiEndpoint>{endpoint}</apiEndpoint>\n"
                    f"\t<apiKey>{api_key}</apiKey>\n"
                    f"\t<modelName>{model_name}</modelName>\n"
                    f"</ModSettings>"
                )
            with open(target_path, "w", encoding="utf-8") as f:
                f.write(default_xml)
            return {"success": True}
        except Exception as e:
            return {"success": False, "message": f"Could not create settings file: {e}"}

    try:
        with open(target_path, "r", encoding="utf-8") as f:
            xml = f.read()

        if is_rimchat:
            # Update LocalConfig
            if "<LocalConfig />" in xml:
                xml = xml.replace(
                    "<LocalConfig />",
                    f"<LocalConfig>\n\t\t\t<baseUrl>{endpoint}</baseUrl>\n\t\t\t<modelName>{model_name}</modelName>\n\t\t</LocalConfig>",
                )
            elif "<LocalConfig>" in xml:
                if re.search(r"<LocalConfig>[\s\S]*?<baseUrl>[\s\S]*?</baseUrl>", xml):
                    xml = re.sub(
                        r"(<LocalConfig>[\s\S]*?<baseUrl>)([\s\S]*?)(</baseUrl>)",
                        rf"\g<1>{endpoint}\g<3>",
                        xml,
                    )
                else:
                    xml = xml.replace("<LocalConfig>", f"<LocalConfig>\n\t\t\t<baseUrl>{endpoint}</baseUrl>")
                if re.search(r"<LocalConfig>[\s\S]*?<modelName>[\s\S]*?</modelName>", xml):
                    xml = re.sub(
                        r"(<LocalConfig>[\s\S]*?<modelName>)([\s\S]*?)(</modelName>)",
                        rf"\g<1>{model_name}\g<3>",
                        xml,
                    )
                else:
                    xml = xml.replace("</LocalConfig>", f"\t\t\t<modelName>{model_name}</modelName>\n\t\t</LocalConfig>")
            else:
                xml = xml.replace(
                    "</ModSettings>",
                    f"\t<LocalConfig>\n\t\t\t<baseUrl>{endpoint}</baseUrl>\n\t\t\t<modelName>{model_name}</modelName>\n\t\t</LocalConfig>\n\t</ModSettings>",
                )

            # Update CloudConfigs
            clean_endpoint = endpoint.rstrip("/")
            new_li = (
                f"<li>\n\t\t\t\t<provider>Custom</provider>\n"
                f"\t\t\t\t<apiKey>{api_key}</apiKey>\n"
                f"\t\t\t\t<selectedModel>Custom</selectedModel>\n"
                f"\t\t\t\t<customModelName>{model_name}</customModelName>\n"
                f"\t\t\t\t<baseUrl>{clean_endpoint}</baseUrl>\n"
                f"\t\t\t</li>"
            )
            if "<CloudConfigs />" in xml:
                xml = xml.replace("<CloudConfigs />", f"<CloudConfigs>\n\t\t\t{new_li}\n\t\t</CloudConfigs>")
            elif "<CloudConfigs>" in xml:
                if "<provider>Custom</provider>" in xml:
                    xml = re.sub(
                        r"<li>\s*<provider>Custom</provider>[\s\S]*?</li>",
                        new_li,
                        xml,
                    )
                else:
                    xml = xml.replace("<CloudConfigs>", f"<CloudConfigs>\n\t\t\t{new_li}\n")
            else:
                xml = xml.replace(
                    "</ModSettings>",
                    f"\t<CloudConfigs>\n\t\t\t{new_li}\n\t\t</CloudConfigs>\n\t</ModSettings>",
                )
        else:
            # RimMind/RimSynapse format
            if "<apiKey>" in xml:
                xml = re.sub(r"<apiKey>[\s\S]*?</apiKey>", f"<apiKey>{api_key}</apiKey>", xml)
            else:
                xml = xml.replace("</ModSettings>", f"\t<apiKey>{api_key}</apiKey>\n\t</ModSettings>")

            if "<apiEndpoint>" in xml:
                xml = re.sub(r"<apiEndpoint>[\s\S]*?</apiEndpoint>", f"<apiEndpoint>{endpoint}</apiEndpoint>", xml)
            else:
                xml = xml.replace("</ModSettings>", f"\t<apiEndpoint>{endpoint}</apiEndpoint>\n\t</ModSettings>")

            if model_name:
                if "<modelName>" in xml:
                    xml = re.sub(r"<modelName>[\s\S]*?</modelName>", f"<modelName>{model_name}</modelName>", xml)
                else:
                    xml = xml.replace("</ModSettings>", f"\t<modelName>{model_name}</modelName>\n\t</ModSettings>")

        with open(target_path, "w", encoding="utf-8") as f:
            f.write(xml)
        return {"success": True}
    except Exception as err:
        return {"success": False, "message": str(err)}


# ---------------------------------------------------------------------------
# RimChat Prompt Presets Helpers
# ---------------------------------------------------------------------------

def find_rimchat_prompt_presets_file() -> str | None:
    config_dir = get_rimworld_config_path()
    if not config_dir:
        return None
    return os.path.join(config_dir, "RimChat", "Prompt", "Custom", "PromptPresets_Custom.json")


def read_rimchat_context() -> dict:
    file_path = find_rimchat_prompt_presets_file()
    if not file_path or not os.path.isfile(file_path):
        return {"found": False, "searchPath": os.path.join(get_rimworld_config_path() or "", "RimChat")}

    try:
        with open(file_path, "r", encoding="utf-8") as f:
            data = json.load(f)

        presets = data.get("Presets", [])
        active_id = data.get("ActivePresetId")
        active_preset = next((p for p in presets if p.get("Id") == active_id), presets[0] if presets else None)

        if not active_preset:
            return {"found": True, "noPresets": True, "filePath": file_path}

        payloads = active_preset.get("ChannelPayloads", {})
        diplomacy = payloads.get("Diplomacy", {})

        diplomacy_config = {}
        raw_json = diplomacy.get("SystemPromptCustomJson", "")
        if raw_json:
            try:
                diplomacy_config = json.loads(raw_json)
            except json.JSONDecodeError:
                pass

        env = diplomacy_config.get("EnvironmentPrompt", {})
        scene = env.get("SceneSystem", {})
        event = env.get("EventIntelPrompt", {})
        policy = diplomacy_config.get("PromptPolicy", {})

        return {
            "found": True,
            "filePath": file_path,
            "fileName": os.path.basename(file_path),
            "presetName": active_preset.get("Name", ""),
            "maxSceneChars": scene.get("MaxSceneChars", 1200),
            "maxTotalChars": scene.get("MaxTotalChars", 4000),
            "maxInjectedChars": event.get("MaxInjectedChars", 1200),
            "summaryCharBudget": policy.get("SummaryCharBudget", 1200),
        }
    except Exception as err:
        return {"found": False, "error": str(err)}


def update_rimchat_context(limits: dict) -> dict:
    file_path = find_rimchat_prompt_presets_file()
    if not file_path or not os.path.isfile(file_path):
        return {"success": False, "message": "Presets file not found."}

    try:
        with open(file_path, "r", encoding="utf-8") as f:
            data = json.load(f)

        presets = data.get("Presets", [])
        active_id = data.get("ActivePresetId")
        active_preset = next((p for p in presets if p.get("Id") == active_id), presets[0] if presets else None)

        if not active_preset:
            return {"success": False, "message": "No presets found in JSON."}

        payloads = active_preset.setdefault("ChannelPayloads", {})

        # Find fallback template
        fallback_template_str = ""
        for p in presets:
            cp = p.get("ChannelPayloads", {})
            for ch_name in ("Diplomacy", "Rpg"):
                ch_data = cp.get(ch_name, {})
                raw = ch_data.get("SystemPromptCustomJson", "")
                if raw and len(raw) > 50:
                    fallback_template_str = raw
                    break
            if fallback_template_str:
                break

        for ch in ("Diplomacy", "Rpg"):
            if ch not in payloads:
                payloads[ch] = {}

            config_obj = {}
            json_str = payloads[ch].get("SystemPromptCustomJson", "")
            if json_str.strip():
                try:
                    config_obj = json.loads(json_str)
                except json.JSONDecodeError:
                    config_obj = {}
            elif fallback_template_str:
                try:
                    config_obj = json.loads(fallback_template_str)
                except json.JSONDecodeError:
                    config_obj = {}

            env = config_obj.setdefault("EnvironmentPrompt", {})
            scene = env.setdefault("SceneSystem", {})
            event = env.setdefault("EventIntelPrompt", {})
            policy = config_obj.setdefault("PromptPolicy", {})

            scene["MaxSceneChars"] = int(limits["maxSceneChars"])
            scene["MaxTotalChars"] = int(limits["maxTotalChars"])
            event["MaxInjectedChars"] = int(limits["maxInjectedChars"])
            policy["SummaryCharBudget"] = int(limits["summaryCharBudget"])

            payloads[ch]["SystemPromptCustomJson"] = json.dumps(config_obj, indent=4)

        with open(file_path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=4)
        return {"success": True}
    except Exception as err:
        return {"success": False, "message": str(err)}


# ---------------------------------------------------------------------------
# Pawn Tracking Heuristics
# ---------------------------------------------------------------------------

def track_pawns_from_text(text: str):
    if not text:
        return

    role_patterns = [
        r"You are playing the role of ([A-Z][A-Za-z0-9'-]+)",
        r"You are ([A-Z][A-Za-z0-9'-]+)",
        r"name is ([A-Z][A-Za-z0-9'-]+)",
    ]
    listing_patterns = [
        r"Character:\s*([A-Z][A-Za-z0-9'-]+)",
        r"Name:\s*([A-Z][A-Za-z0-9'-]+)",
        r"Pawn:\s*([A-Z][A-Za-z0-9'-]+)",
        r"-\s*([A-Z][A-Za-z0-9'-]+)\s*\((?:Age|Gender|Health)",
        r"\*\s*([A-Z][A-Za-z0-9'-]+)\s*:\s*(?:Health|Mood|Age)",
        r"(?:Colonist|Pawn):\s*([A-Z][A-Za-z0-9'-]+)",
    ]

    for pattern in role_patterns + listing_patterns:
        for match in re.finditer(pattern, text, re.IGNORECASE if "Colonist|Pawn" in pattern else 0):
            name = match.group(1).strip()
            if name and name not in ("Pawn", "User", "Assistant"):
                active_pawns_tracker[name] = time.time()


def deparse_request(body: dict) -> str:
    messages = body.get("messages", [])
    if not messages:
        return "Received empty request to LLM"

    for msg in messages:
        if msg.get("content"):
            track_pawns_from_text(msg["content"])

    system_msg = next((m for m in messages if m.get("role") == "system"), None)
    system_content = system_msg["content"] if system_msg else ""

    user_messages = [m for m in messages if m.get("role") == "user"]
    last_user_msg = user_messages[-1]["content"] if user_messages else ""

    is_storyteller = bool(re.search(r"storyteller|director|incident|narrator|incidentmaker|threat", system_content, re.IGNORECASE))
    is_advisor = bool(re.search(r"advisor|helper", system_content, re.IGNORECASE))
    is_memory = bool(re.search(r"summary|summarizer|history|memories", system_content, re.IGNORECASE))

    tag = "[Personal Conversation]"
    if is_storyteller:
        tag = "[Storyteller]"
    elif is_advisor:
        tag = "[Advisor]"
    elif is_memory:
        tag = "[Colony Memory]"

    if is_storyteller:
        return f"{tag} Storyteller AI ➔ Began story event processing."
    if is_advisor:
        return f"{tag} Advisor AI ➔ Analyzing colony situation..."
    if is_memory:
        return f"{tag} Summarizer ➔ Generating memory summary..."

    pawn_name = "Pawn"
    name_patterns = [
        r"You are playing the role of ([A-Za-z0-9\s'-]+?)(?:,|\.|\\n|is a|Your traits|$)",
        r"You are ([A-Za-z0-9\s'-]+?)(?:,|\.|\\n|is a|Your traits|\(|\bYour\b|$)",
        r"name is ([A-Za-z0-9\s'-]+?)(?:,|\.|\\n|$)",
        r"Character:\s*([A-Za-z0-9\s'-]+?)(?:\\n|,|\.|$)",
        r"Name:\s*([A-Za-z0-9\s'-]+?)(?:\\n|,|\.|$)",
    ]

    for pattern in name_patterns:
        m = re.search(pattern, system_content, re.IGNORECASE)
        if m and m.group(1) and m.group(1).strip():
            pawn_name = m.group(1).strip()
            break

    if pawn_name and pawn_name != "Pawn":
        active_pawns_tracker[pawn_name] = time.time()

    clean_user_msg = re.sub(r"\[.*?\]", "", last_user_msg)
    clean_user_msg = re.sub(r"<.*?>", "", clean_user_msg).strip()
    if len(clean_user_msg) > 120:
        clean_user_msg = clean_user_msg[:120] + "..."
    if not clean_user_msg:
        clean_user_msg = "Generating dialogue/action response..."

    return f'{tag} User ➔ ({pawn_name}): "{clean_user_msg}"'


def deparse_response(body: dict, response_content: str) -> str:
    if not response_content:
        return "LLM returned empty response"

    track_pawns_from_text(response_content)

    system_msg = next((m for m in body.get("messages", []) if m.get("role") == "system"), None)
    system_content = system_msg["content"] if system_msg else ""

    is_storyteller = bool(re.search(r"storyteller|director|incident|narrator|incidentmaker|threat", system_content, re.IGNORECASE))
    is_advisor = bool(re.search(r"advisor|helper", system_content, re.IGNORECASE))
    is_memory = bool(re.search(r"summary|summarizer|history|memories", system_content, re.IGNORECASE))

    tag = "[Personal Conversation]"
    if is_storyteller:
        tag = "[Storyteller]"
    elif is_advisor:
        tag = "[Advisor]"
    elif is_memory:
        tag = "[Colony Memory]"

    if is_storyteller:
        clean = re.sub(r"```json[\s\S]*?```", "", response_content)
        clean = re.sub(r"```[\s\S]*?```", "", clean).strip()
        if len(clean) > 120:
            clean = clean[:120] + "..."
        return f'{tag} Storyteller AI ➔ Event processed: "{clean}"'

    if is_advisor:
        clean = response_content.strip()
        md_match = re.match(r"^```(?:json)?\s*([\s\S]*?)\s*```$", clean, re.IGNORECASE)
        parsed_content = md_match.group(1).strip() if md_match else clean

        if parsed_content.startswith("{") and parsed_content.endswith("}"):
            try:
                parsed = json.loads(parsed_content)
                if isinstance(parsed.get("advices"), list) and parsed["advices"]:
                    lines = []
                    for adv in parsed["advices"]:
                        target = adv.get("target") or adv.get("pawn") or "Colony"
                        if target and target != "Colony":
                            active_pawns_tracker[target] = time.time()
                        reason = adv.get("reason", "No reason specified")
                        if len(reason) > 100:
                            reason = reason[:100] + "..."
                        lines.append(f'{target} (Pawn Thoughts): "{reason}"')
                    return f"{tag} Advisor AI ➔ {' | '.join(lines)}"
            except json.JSONDecodeError:
                pass

        if len(clean) > 120:
            clean = clean[:120] + "..."
        return f'{tag} Advisor AI ➔ Recommendation: "{clean}"'

    if is_memory:
        clean = response_content.strip()
        if len(clean) > 120:
            clean = clean[:120] + "..."
        return f'{tag} Summarizer ➔ Memory summary: "{clean}"'

    # Dialogue response
    pawn_name = "Pawn"
    name_patterns = [
        r"You are playing the role of ([A-Za-z0-9\s'-]+?)(?:,|\.|\\n|is a|Your traits|$)",
        r"You are ([A-Za-z0-9\s'-]+?)(?:,|\.|\\n|is a|Your traits|\(|\bYour\b|$)",
        r"name is ([A-Za-z0-9\s'-]+?)(?:,|\.|\\n|$)",
        r"Character:\s*([A-Za-z0-9\s'-]+?)(?:\\n|,|\.|$)",
        r"Name:\s*([A-Za-z0-9\s'-]+?)(?:\\n|,|\.|$)",
    ]
    for pattern in name_patterns:
        m = re.search(pattern, system_content, re.IGNORECASE)
        if m and m.group(1) and m.group(1).strip():
            pawn_name = m.group(1).strip()
            break

    if pawn_name and pawn_name != "Pawn":
        active_pawns_tracker[pawn_name] = time.time()

    clean_response = response_content.strip()
    md_match = re.match(r"^```(?:json)?\s*([\s\S]*?)\s*```$", clean_response, re.IGNORECASE)
    parsed_content = md_match.group(1).strip() if md_match else clean_response

    if parsed_content.startswith("{") and parsed_content.endswith("}"):
        try:
            parsed = json.loads(parsed_content)
            if "reply" in parsed:
                clean_response = parsed["reply"]
            elif "message" in parsed:
                clean_response = parsed["message"]
            elif "response" in parsed:
                clean_response = parsed["response"]
            elif "content" in parsed:
                clean_response = parsed["content"]
            elif "dialogue" in parsed:
                clean_response = parsed["dialogue"]
            elif "narrative" in parsed:
                clean_response = parsed["narrative"]
            else:
                clean_response = parsed_content
        except json.JSONDecodeError:
            clean_response = re.sub(r"<think>[\s\S]*?</think>", "", parsed_content, flags=re.IGNORECASE).strip()
            clean_response = re.sub(r"\[.*?\]", "", clean_response)
            clean_response = re.sub(r"<.*?>", "", clean_response).strip()
    else:
        clean_response = re.sub(r"<think>[\s\S]*?</think>", "", clean_response, flags=re.IGNORECASE).strip()
        clean_response = re.sub(r"\[.*?\]", "", clean_response)
        clean_response = re.sub(r"<.*?>", "", clean_response).strip()

    if len(clean_response) > 120:
        clean_response = clean_response[:120] + "..."

    return f'{tag} {pawn_name} ➔ User: "{clean_response}"'


# ---------------------------------------------------------------------------
# LM Studio Keep-Alive Ping
# ---------------------------------------------------------------------------

def start_lm_studio_keep_alive():
    """Background loop that pings LM Studio every 4 minutes to prevent model unloading."""
    print("[server] Initializing background LM Studio keep-alive ping loop (every 4 minutes)...")

    def ping_loop():
        while True:
            time.sleep(240)
            try:
                lm_status = fetch_lm_studio_models()
                if lm_status["online"] and lm_status["models"]:
                    active_model = lm_status["models"][0]["id"]
                    headers = {"Content-Type": "application/json"}
                    if config.get("lmStudioApiKey"):
                        headers["Authorization"] = f"Bearer {config['lmStudioApiKey']}"

                    http_requests.post(
                        f"{config['lmStudioUrl']}/v1/chat/completions",
                        headers=headers,
                        json={
                            "model": active_model,
                            "messages": [{"role": "user", "content": "keep-alive ping"}],
                            "max_tokens": 1,
                        },
                        timeout=10,
                    )
                    print(f'[lm-studio] Sent stateless 1-token keep-alive ping to "{active_model}"')
            except Exception:
                pass

    t = threading.Thread(target=ping_loop, daemon=True)
    t.start()


# ---------------------------------------------------------------------------
# Flask Application
# ---------------------------------------------------------------------------
app = Flask(__name__, static_folder=str(PUBLIC_DIR), static_url_path="")
CORS(app)

# In-memory session stats (reset on restart, no persistence needed)
session_stats = {
    "context_calls": 0,
    "total_tokens_estimated": 0,
    "total_memories_included": 0,
    "last_colony": None,
    "last_pawns_seen": [],
    "recent_memories": [],
    "recent_threads": [],
}


# Route rewriter middleware: handle /v1/v1/... double prefix
@app.before_request
def rewrite_double_prefix():
    if request.path.startswith("/v1/v1/"):
        original = request.path
        new_path = request.path.replace("/v1/v1/", "/v1/", 1)
        log_to_dashboard("info", "proxy", f"Rewrote double-prefixed request: {original} -> {new_path}")
        # Flask doesn't support request.path mutation, so we use url_rule adapter
        from werkzeug.routing import RequestRedirect
        adapter = app.url_map.bind("")
        try:
            endpoint, values = adapter.match(new_path, method=request.method)
            request.url_rule = app.url_map.bind("").match(new_path, method=request.method)
        except Exception:
            pass
        request.environ["PATH_INFO"] = new_path


# Auth middleware for /v1/* routes
@app.before_request
def validate_bridge_auth():
    if not request.path.startswith("/v1"):
        return None
    if not config.get("bridgeApiKey"):
        return None

    auth_header = request.headers.get("Authorization", "")
    if not auth_header:
        log_to_dashboard("warn", "proxy", f"Rejected unauthenticated {request.method} {request.path}")
        return jsonify({
            "error": {
                "message": "Missing Authorization header. Set your API key in RimSynapse settings.",
                "type": "auth_error",
                "code": "missing_key",
            }
        }), 401

    token = re.sub(r"^Bearer\s+", "", auth_header, flags=re.IGNORECASE)
    if token != config["bridgeApiKey"]:
        log_to_dashboard("warn", "proxy", f"Rejected invalid API key on {request.method} {request.path}")
        return jsonify({
            "error": {
                "message": "Invalid API key.",
                "type": "auth_error",
                "code": "invalid_key",
            }
        }), 401

    return None


# ---------------------------------------------------------------------------
# Dashboard (Static Files)
# ---------------------------------------------------------------------------
@app.route("/")
def serve_index():
    return send_from_directory(str(PUBLIC_DIR), "index.html")


# ---------------------------------------------------------------------------
# SSE Endpoint for Live Logs
# ---------------------------------------------------------------------------
@app.route("/api/logs")
def sse_logs():
    from queue import Queue as Q

    client_queue = Q()

    # Send history first
    history_payload = json.dumps({"type": "history", "logs": log_buffer})

    with sse_clients_lock:
        sse_clients.append(client_queue)

    def generate():
        # Send existing history
        yield f"data: {history_payload}\n\n"

        try:
            while True:
                try:
                    data = client_queue.get(timeout=30)
                    yield data
                except Empty:
                    # Send keepalive comment to prevent timeout
                    yield ": keepalive\n\n"
        except GeneratorExit:
            pass
        finally:
            with sse_clients_lock:
                if client_queue in sse_clients:
                    sse_clients.remove(client_queue)

    return Response(
        generate(),
        mimetype="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no",
        },
    )


# ---------------------------------------------------------------------------
# Admin API: Status
# ---------------------------------------------------------------------------
@app.route("/api/status")
def api_status():
    lm_status = fetch_lm_studio_models()
    active_ctx = fetch_lm_studio_active_context()
    gpu_stats = fetch_gpu_stats()

    prune_active_pawns()
    active_pawn_count = len(active_pawns_tracker)
    active_pawn_names = list(active_pawns_tracker.keys())

    total_prompt = 0
    total_completion = 0
    total_duration = 0
    count = len(recent_requests_metrics)

    for m in recent_requests_metrics:
        total_prompt += m["promptTokens"]
        total_completion += m["completionTokens"]
        total_duration += m["durationMs"]

    avg_prompt = round(total_prompt / count) if count > 0 else 0
    avg_completion = round(total_completion / count) if count > 0 else 0
    avg_duration = round(total_duration / count) if count > 0 else 0

    tokens_per_pawn = (
        max(100, round(avg_prompt / active_pawn_count))
        if active_pawn_count > 0 and avg_prompt > 0
        else 1200
    )

    context_target = (
        active_ctx["contextLength"]
        if active_ctx and active_ctx.get("contextLength")
        else config.get("lastAllocatedTokenWindow", 34400)
    )

    max_pawns_by_context = context_target // tokens_per_pawn

    five_min_ago = (time.time() - 300) * 1000
    recent_calls = sum(1 for m in recent_requests_metrics if m["timestamp"] > five_min_ago)
    calls_per_minute = round(recent_calls / 5, 1)

    return jsonify({
        "config": config,
        "queueSize": queue_size,
        "processingQueue": processing_queue,
        "lmStudio": {
            "url": config["lmStudioUrl"],
            "online": lm_status["online"],
            "error": lm_status.get("error"),
            "models": [m["id"] for m in lm_status["models"]],
            "activeContext": active_ctx,
        },
        "gpu": gpu_stats,
        "capacity": {
            "activePawnCount": active_pawn_count,
            "activePawnNames": active_pawn_names,
            "avgPromptTokens": avg_prompt,
            "avgCompletionTokens": avg_completion,
            "avgDurationMs": avg_duration,
            "tokensPerPawn": tokens_per_pawn,
            "maxPawnsByContext": max_pawns_by_context,
            "callsPerMinute": calls_per_minute,
            "contextTarget": context_target,
        },
    })


# ---------------------------------------------------------------------------
# Admin API: Update Config
# ---------------------------------------------------------------------------
@app.route("/api/config", methods=["POST"])
def api_config_update():
    data = request.get_json(force=True)

    if "port" in data:
        config["port"] = int(data["port"])
    if "lmStudioUrl" in data:
        config["lmStudioUrl"] = data["lmStudioUrl"]
    if "bridgeApiKey" in data:
        config["bridgeApiKey"] = data["bridgeApiKey"]
    if "lmStudioApiKey" in data:
        config["lmStudioApiKey"] = data["lmStudioApiKey"]
    if "autoMapModel" in data:
        config["autoMapModel"] = bool(data["autoMapModel"])
    if "sanitizeResponse" in data:
        config["sanitizeResponse"] = bool(data["sanitizeResponse"])
    if "timeoutMs" in data:
        config["timeoutMs"] = int(data["timeoutMs"])

    if save_config():
        log_to_dashboard("success", "server", "Configuration updated successfully.")
        return jsonify({"success": True, "config": config})
    else:
        return jsonify({"success": False, "message": "Failed to write config file."}), 500


# ---------------------------------------------------------------------------
# Admin API: Test Connection
# ---------------------------------------------------------------------------
@app.route("/api/test-connection", methods=["POST"])
def api_test_connection():
    return jsonify(fetch_lm_studio_models())


# ---------------------------------------------------------------------------
# Integration API: RimWorld Config
# ---------------------------------------------------------------------------
@app.route("/api/rimworld-config", methods=["GET"])
def api_rimworld_config_get():
    custom_path = request.args.get("filePath")
    return jsonify(read_rimmind_config(custom_path))


@app.route("/api/rimworld-config", methods=["POST"])
def api_rimworld_config_post():
    data = request.get_json(force=True) or {}
    custom_file_path = data.get("filePath")
    model_name = data.get("modelName")

    endpoint = f"https://localhost:{config['port']}/"
    api_key = config.get("bridgeApiKey") or "rimmind-bridge"

    if not model_name:
        lm_status = fetch_lm_studio_models()
        if lm_status["online"] and lm_status["models"]:
            model_name = lm_status["models"][0]["id"]
        else:
            model_name = "auto"

    result = update_rimmind_config(endpoint, api_key, model_name, custom_file_path)
    if result["success"]:
        log_to_dashboard(
            "success", "server",
            f"Successfully injected configuration into RimWorld settings file (Endpoint: {endpoint}, Key: {api_key}, Model: {model_name})",
        )
        return jsonify({"success": True, "config": read_rimmind_config(custom_file_path)})
    else:
        log_to_dashboard("error", "server", f"Failed to inject RimWorld settings: {result['message']}")
        return jsonify({"success": False, "message": result["message"]}), 500


# ---------------------------------------------------------------------------
# Integration API: RimWorld Context
# ---------------------------------------------------------------------------
@app.route("/api/rimworld-context", methods=["GET"])
def api_rimworld_context_get():
    return jsonify(read_rimchat_context())


@app.route("/api/rimworld-context", methods=["POST"])
def api_rimworld_context_post():
    limits = request.get_json(force=True) or {}
    for key in ("maxSceneChars", "maxTotalChars", "maxInjectedChars", "summaryCharBudget"):
        if key not in limits:
            return jsonify({"success": False, "message": "Missing required character limits."}), 400

    result = update_rimchat_context(limits)
    if result["success"]:
        log_to_dashboard(
            "success", "server",
            f"Successfully injected context limits into RimWorld presets file "
            f"(MaxTotalChars: {limits['maxTotalChars']}, MaxSceneChars: {limits['maxSceneChars']}, "
            f"MaxInjectedChars: {limits['maxInjectedChars']}, SummaryCharBudget: {limits['summaryCharBudget']})",
        )
        return jsonify({"success": True, "config": read_rimchat_context()})
    else:
        log_to_dashboard("error", "server", f"Failed to inject RimWorld context: {result['message']}")
        return jsonify({"success": False, "message": result["message"]}), 500


# ---------------------------------------------------------------------------
# OpenAI Proxy: Models List
# ---------------------------------------------------------------------------
@app.route("/v1/models", methods=["GET"])
def proxy_models():
    log_to_dashboard("info", "proxy", "Received model list request from RimWorld/client.")
    lm_status = fetch_lm_studio_models()

    if lm_status["online"]:
        return jsonify({"object": "list", "data": lm_status["models"]})
    else:
        log_to_dashboard("warn", "proxy", "LM Studio is offline. Returning fallback model list.")
        return jsonify({
            "object": "list",
            "data": [{"id": "lm-studio-offline-fallback", "object": "model", "owned_by": "lm-studio"}],
        })


# ---------------------------------------------------------------------------
# OpenAI Proxy: Chat Completions (Queued)
# ---------------------------------------------------------------------------
@app.route("/v1/chat/completions", methods=["POST"])
def proxy_chat_completions():
    global queue_size
    request_id = f"req-{uuid.uuid4().hex[:9]}"
    start_timestamp = time.time() * 1000
    body = request.get_json(force=True)

    requested_model = body.get("model", "")
    prompt_summary = body["messages"][-1]["content"] if body.get("messages") else "No messages"

    request_summary = deparse_request(body)
    log_to_dashboard("info", "proxy", request_summary, {
        "requestId": request_id,
        "model": requested_model,
        "promptSummary": prompt_summary[:100] + ("..." if len(prompt_summary) > 100 else ""),
    })

    # We need to capture the response from the queued task
    result_holder = {"response": None, "status_code": 200}
    done_event = threading.Event()

    def execute_request():
        log_to_dashboard("info", "queue", f"Executing request [{request_id}] now...")

        try:
            target_model = requested_model

            # Auto Map Model
            if config.get("autoMapModel"):
                lm_status = fetch_lm_studio_models()
                if lm_status["online"] and lm_status["models"]:
                    active_model = lm_status["models"][0]["id"]
                    if active_model and active_model != requested_model:
                        log_to_dashboard(
                            "info", "proxy",
                            f'Auto-mapping requested model "{requested_model}" to active LM Studio model "{active_model}".',
                        )
                        body["model"] = active_model
                        target_model = active_model

            # Fix for LM Studio 400 error
            if body.get("response_format", {}).get("type") == "json_object":
                log_to_dashboard(
                    "info", "proxy",
                    'Removing unsupported "response_format.type: json_object" to prevent LM Studio 400 error.',
                )
                del body["response_format"]

            # Increase max_tokens for reasoning models
            if body.get("max_tokens") and body["max_tokens"] < 8192:
                log_to_dashboard(
                    "info", "proxy",
                    f"Increasing max_tokens from {body['max_tokens']} to 8192 to allow reasoning models sufficient tokens.",
                )
                body["max_tokens"] = 8192

            log_to_dashboard(
                "info", "lm-studio",
                f"Forwarding request [{request_id}] to LM Studio: {config['lmStudioUrl']}/v1/chat/completions",
            )

            # Forward to LM Studio
            proxy_headers = {"Content-Type": "application/json"}
            if config.get("lmStudioApiKey"):
                proxy_headers["Authorization"] = f"Bearer {config['lmStudioApiKey']}"

            resp = http_requests.post(
                f"{config['lmStudioUrl']}/v1/chat/completions",
                headers=proxy_headers,
                json=body,
                timeout=config["timeoutMs"] / 1000,
            )

            if not resp.ok:
                raise Exception(f"LM Studio returned {resp.status_code}: {resp.text}")

            response_data = resp.json()
            duration = time.time() * 1000 - start_timestamp

            # Handle reasoning content fallback and sanitization
            if response_data.get("choices") and response_data["choices"][0].get("message"):
                msg = response_data["choices"][0]["message"]

                if (not msg.get("content") or not msg["content"].strip()) and msg.get("reasoning_content"):
                    log_to_dashboard(
                        "warn", "proxy",
                        f"Assistant content was empty but reasoning_content was present. Using reasoning_content as fallback (length: {len(msg['reasoning_content'])} chars).",
                    )
                    msg["content"] = msg["reasoning_content"]

                if config.get("sanitizeResponse"):
                    system_msg = next((m for m in body.get("messages", []) if m.get("role") == "system"), None)
                    sys_content = system_msg["content"] if system_msg else ""
                    is_pawn_dialogue = bool(
                        re.search(
                            r"You are playing the role of|You are ([A-Za-z0-9\s'-]+?) in RimWorld|role-playing as a RimWorld colonist",
                            sys_content, re.IGNORECASE,
                        )
                    ) or bool(re.search(r"dialogue", sys_content, re.IGNORECASE)) or any(
                        m.get("content", "").find("Mood changed") != -1
                        for m in body.get("messages", [])
                    )

                    msg["content"] = sanitize_message_content(msg["content"], is_pawn_dialogue)

            prompt_tokens = response_data.get("usage", {}).get("prompt_tokens", 0)
            completion_tokens = response_data.get("usage", {}).get("completion_tokens", 0)

            resp_content = response_data.get("choices", [{}])[0].get("message", {}).get("content", "")
            response_summary = deparse_response(body, resp_content)
            log_to_dashboard("success", "proxy", response_summary, {
                "requestId": request_id,
                "durationMs": int(duration),
                "model": target_model,
                "promptTokens": prompt_tokens,
                "completionTokens": completion_tokens,
                "response": resp_content[:150] + "..." if resp_content else "No response text",
            })

            result_holder["response"] = response_data

        except Exception as err:
            duration = time.time() * 1000 - start_timestamp
            log_to_dashboard("error", "proxy", f"Request [{request_id}] failed in {int(duration)}ms: {err}")
            result_holder["response"] = {
                "error": {
                    "message": f"RimSynapse Bridge Error: {err}",
                    "type": "bridge_error",
                    "param": None,
                    "code": "internal_error",
                }
            }
            result_holder["status_code"] = 500

    # Push to queue
    with queue_lock:
        queue_size = request_queue.qsize() + 1
    request_queue.put((execute_request, done_event))

    log_to_dashboard("info", "queue", f"Starting queue processing. Queue length: {request_queue.qsize()}")

    # Wait for the request to complete
    done_event.wait(timeout=config["timeoutMs"] / 1000 + 5)

    return jsonify(result_holder["response"]), result_holder["status_code"]


# ---------------------------------------------------------------------------
# Fallback Error Handler
# ---------------------------------------------------------------------------
@app.errorhandler(Exception)
def handle_error(err):
    print(f"Flask Error: {err}")
    return jsonify({"error": {"message": str(err)}}), 500


# ---------------------------------------------------------------------------
# HTTPS / Certificate Setup
# ---------------------------------------------------------------------------

def load_ssl_context():
    """Convert PFX to PEM and return an SSL context, or None for HTTP fallback."""
    if not PFX_PATH.exists():
        return None

    try:
        from cryptography.hazmat.primitives.serialization import pkcs12, Encoding, PrivateFormat, NoEncryption

        pfx_data = PFX_PATH.read_bytes()
        private_key, certificate, additional_certs = pkcs12.load_key_and_certificates(
            pfx_data, PFX_PASS.encode("utf-8")
        )

        cert_pem_path = BASE_DIR / "certificate.pem"
        key_pem_path = BASE_DIR / "private_key.pem"

        cert_pem_path.write_bytes(certificate.public_bytes(Encoding.PEM))
        key_pem_path.write_bytes(private_key.private_bytes(Encoding.PEM, PrivateFormat.TraditionalOpenSSL, NoEncryption()))

        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ctx.load_cert_chain(str(cert_pem_path), str(key_pem_path))
        return ctx
    except Exception as err:
        print(f"[warn] Failed to load PFX certificate: {err}")
        return None


# ---------------------------------------------------------------------------
# Context Assembly & Dashboard Routes (Stateless)
# ---------------------------------------------------------------------------

@app.route('/api/context/build', methods=['POST'])
def api_context_build():
    """Stateless context assembly. Receives full game-state packet, returns prompt."""
    try:
        data = request.get_json()
        result = build_context(data)

        # Track session stats for dashboard
        session_stats["context_calls"] += 1
        session_stats["total_tokens_estimated"] += result.get("tokens_estimated", 0)
        session_stats["total_memories_included"] += result.get("memories_included", 0)

        # Track colony info
        colony = data.get("colony")
        if colony:
            session_stats["last_colony"] = colony

        # Track pawns seen
        for key in ("source_pawn", "target_pawn"):
            pawn = data.get(key)
            if pawn and pawn.get("name"):
                name = pawn["name"]
                if name not in session_stats["last_pawns_seen"]:
                    session_stats["last_pawns_seen"].append(name)
                # Track memories
                for m in pawn.get("memories", []):
                    entry = {
                        "pawn_name": name,
                        "summary": m.get("summary", ""),
                        "memory_type": m.get("type", m.get("memoryType", "event")),
                        "weight": m.get("weight", 0),
                    }
                    session_stats["recent_memories"].insert(0, entry)
                    session_stats["recent_memories"] = session_stats["recent_memories"][:50]

        # Track threads
        threads = data.get("narrative_threads", [])
        if threads:
            session_stats["recent_threads"] = threads

        log_to_dashboard('info', 'context',
            f"Context built: {result['memories_included']} memories, "
            f"{result['threads_included']} threads, ~{result['tokens_estimated']} tokens")
        return jsonify(result)
    except Exception as e:
        log_to_dashboard('error', 'context', f"Context build error: {e}")
        return jsonify({'error': str(e)}), 500


# --- Dashboard Stats (from session memory, not a database) ---

@app.route('/api/database/stats', methods=['GET'])
def api_dashboard_stats():
    """Session statistics for the dashboard."""
    colony = session_stats.get("last_colony", {})
    return jsonify({
        "schema_version": "stateless",
        "db_size_bytes": 0,
        "tables": {},
        "colonies": [colony] if colony else [],
        "mods": [],
        "weight_distribution": _compute_weight_dist(session_stats.get("recent_memories", [])),
        "thread_distribution": _compute_thread_dist(session_stats.get("recent_threads", [])),
        "totals": {
            "colonies": 1 if colony else 0,
            "pawns": len(session_stats.get("last_pawns_seen", [])),
            "memories": len(session_stats.get("recent_memories", [])),
            "relationships": 0,
            "interactions": session_stats.get("context_calls", 0),
            "threads": len(session_stats.get("recent_threads", [])),
        },
    })


@app.route('/api/database/memories/recent', methods=['GET'])
def api_dashboard_recent_memories():
    """Recent memories seen in context calls."""
    limit = request.args.get('limit', 20, type=int)
    return jsonify(session_stats.get("recent_memories", [])[:limit])


@app.route('/api/database/pawns', methods=['GET'])
def api_dashboard_pawns():
    """Pawns seen during this session."""
    return jsonify([{"name_nick": name} for name in session_stats.get("last_pawns_seen", [])])


@app.route('/api/database/threads', methods=['GET'])
def api_dashboard_threads():
    """Narrative threads from last context call."""
    return jsonify(session_stats.get("recent_threads", []))


def _compute_weight_dist(memories):
    """Compute weight distribution from in-memory list."""
    dist = {"decaying": 0, "stable": 0, "active": 0, "hot": 0}
    for m in memories:
        w = m.get("weight", 0)
        if w < 0.2:
            dist["decaying"] += 1
        elif w < 0.5:
            dist["stable"] += 1
        elif w < 0.8:
            dist["active"] += 1
        else:
            dist["hot"] += 1
    return dist


def _compute_thread_dist(threads):
    """Compute thread distribution from in-memory list."""
    dist = {"fading": 0, "active": 0, "hot": 0}
    for t in threads:
        w = t.get("weight", 0)
        if w < 0.3:
            dist["fading"] += 1
        elif w < 0.7:
            dist["active"] += 1
        else:
            dist["hot"] += 1
    return dist


# ---------------------------------------------------------------------------
# Server Entry Point
# ---------------------------------------------------------------------------

def main():
    write_ps_script()

    ssl_ctx = load_ssl_context()
    port = config["port"]

    if ssl_ctx:
        log_to_dashboard("success", "server", f"RimSynapse Bridge started on HTTPS port {port}")
        log_to_dashboard("info", "server", f"Dashboard: https://localhost:{port}")
        log_to_dashboard("info", "server", f"Set RimWorld endpoint to: https://localhost:{port}/")
    else:
        log_to_dashboard("warn", "server", "certificate.pfx not found — starting in plain HTTP mode.")
        log_to_dashboard("info", "server", 'Run "setup-certs.ps1" to generate a trusted certificate.')
        log_to_dashboard("success", "server", f"RimSynapse Bridge started on HTTP port {port}")
        log_to_dashboard("info", "server", f"Dashboard: http://localhost:{port}")
        log_to_dashboard("info", "server", f"Set RimWorld endpoint to: http://localhost:{port}/")

    log_to_dashboard("info", "server", f"Target LM Studio URL: {config['lmStudioUrl']}")

    start_lm_studio_keep_alive()

    app.run(
        host="0.0.0.0",
        port=port,
        ssl_context=ssl_ctx,
        threaded=True,
        debug=False,
    )


if __name__ == "__main__":
    main()
