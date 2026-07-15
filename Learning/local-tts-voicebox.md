# Local TTS: Voicebox Setup Guide

Voicebox is a multi-engine local speech synthesis (Text-to-Speech) server designed to deliver high-quality audio generation directly from your own hardware. By integrating Voicebox, RimSynapse allows you to experience immersive storyteller and character dialogue without relying on cloud APIs like ElevenLabs.

Voicebox wraps seven distinct TTS engines behind a single unified API:
*   **Kokoro**: Extremely fast, CPU-friendly (approx. 150 MB VRAM)
*   **LuxTTS**: Fast, CPU-friendly (approx. 1 GB VRAM)
*   **Chatterbox Turbo**: Balanced local TTS model (approx. 1.5 GB VRAM)
*   **Qwen 0.6B / Qwen CustomVoice 0.6B**: Local TTS model with voice cloning (approx. 2 GB VRAM)
*   **Chatterbox Multilingual**: Multilingual local TTS model (approx. 3 GB VRAM)
*   **TADA 1B / 3B**: Advanced local TTS model (approx. 4 GB / 8 GB VRAM)
*   **Qwen 1.7B / Qwen CustomVoice 1.7B**: State-of-the-art local TTS model with instruct-mode support (approx. 6 GB VRAM)

---

## 🚀 Voicebox Setup and Configuration

### 1. Connection settings
To use Voicebox, open the **Customize LLM Providers** window in the RimSynapse Core settings and configure the Voicebox panel:

*   **Endpoint**: The URL where your local Voicebox instance is running. By default, Voicebox uses:
    `http://127.0.0.1:23432`
*   **Key**: Optional API key (if your Voicebox instance requires authorization). Can be left blank for default local setups.
*   **Model**: Specifies which TTS engine and (optionally) model size to use. You can specify this using the format `engine:size` or simply `engine`.
    *   *Examples*: `kokoro`, `qwen`, `qwen:1.7B`, `luxtts`, `chatterbox_turbo`
    *   Clicking **Revert** next to the Model field resets the value to `kokoro`.

### 2. Testing Connection and Profiling
*   Click **Test Connection** in the Voicebox section to verify that Core can reach your local Voicebox instance. It sends a quick request to the profiles list endpoint.
*   Click the **`...`** (Fetch Models/Profiles) button to query all active voice profiles created or stored inside your Voicebox. If the call succeeds, a float menu will open containing all retrieved profiles, formatted as `Name (Engine)`. Selecting a profile assigns its UUID directly as the active test model.

---

## 🎙️ Voice Profiles and Cloned Voices

Voicebox separates voices into two types:
1.  **Preset Profiles**: Pre-built voices shipped natively with the engines (e.g. Kokoro's `am_adam`, Qwen CustomVoice's `Ryan`).
2.  **Cloned Profiles**: Custom voices generated from one or more reference audio samples.

### Dynamic Resolution
When sending an audio request, RimSynapse provides the voice identifier in the `Voice` parameter. VoiceboxProvider automatically resolves this identifier:
*   If you supply a **Profile name** (e.g. `"Aura"`), Core queries Voicebox's `/profiles` endpoint, searches for a profile with that name (case-insensitive), and resolves it to its corresponding UUID on the fly.
*   If you supply a **UUID** directly, Core passes it straight to the generator.
*   If the voice identifier is empty, Core retrieves all profiles from Voicebox and automatically falls back to using the first available profile in the database.

---

## 🎛️ Audio Routing

You can route your storyteller or other companion mod audio queries directly to Voicebox:
1.  Open **LLM Query Routing** in the RimSynapse Core settings.
2.  In the **Audio Queries** dropdown under Capability Defaults, select **Voicebox**.
3.  Specify the default model/engine size if desired (e.g., `kokoro` or `qwen:1.7B`).

---

## 🧪 Test Bench Integration

You can test Voicebox synthesis in-game via the **RimSynapse Test Bench**:
1.  Open the Test Bench and go to the **Audio** tab.
2.  Select **Voicebox** as your **Target Provider**.
3.  Click **Select Voice** to open a dropdown of all active voice profiles fetched dynamically from your local Voicebox instance.
4.  Type your desired text and click **Generate and Play Audio**. Core will send the request, download the generated WAV file, extract the raw PCM data using our native subchunk parser, and play it directly in-game.
