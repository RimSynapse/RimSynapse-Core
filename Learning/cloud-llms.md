# Cloud LLM Setup Guide

RimSynapse Core provides seamless integrations with three of the most powerful frontier model providers: **OpenAI**, **Google Gemini**, and **Anthropic Claude**. It also includes a completely free, built-in image provider (**Pollinations.ai**) that requires no setup whatsoever!

This guide will walk you through how to generate the API keys necessary to enable the frontier text and vision models.

---

## 🎨 Free Image Generation (No Setup Required!)

Before diving into API keys, you should know that RimSynapse Core already has a fully integrated, open, and free image generation provider: **Pollinations.ai**. 
- **Cost**: 100% Free
- **API Key**: None required! 
- **Setup**: None! It is hardwired into the Core and is ready to generate pictures for your Factions and World News events the moment you install the mod.

---

## 1. OpenAI (GPT-4o, GPT-4-Turbo)

OpenAI provides industry-leading reasoning, vision, and text generation. It operates on a **Pay-As-You-Go** model. You must load your account with at least $5.00 in credits to use the API.

![OpenAI API Keys Dashboard](file:///C:/Users/sealt/.gemini/antigravity-ide/brain/9d9ad06c-a8dd-4ffe-a841-64ad74dcf29d/openai_api_keys_1783867094846.png)

### Steps to Generate an API Key:
1. Navigate to the [OpenAI API Keys Dashboard](https://platform.openai.com/api-keys).
2. Log in with your OpenAI account.
3. On the left-hand sidebar, click **API Keys**.
4. Click the black **Create new secret key** button in the center.

![Create Key Dialog](file:///C:/Users/sealt/.gemini/antigravity-ide/brain/9d9ad06c-a8dd-4ffe-a841-64ad74dcf29d/openai_create_key_dialog_1783867105590.png)
5. Name your key something recognizable (e.g., "RimWorld Synapse").
6. Click **Create secret key**.
7. **IMPORTANT:** Copy the key immediately (it starts with `sk-proj-`). You will never be able to see it in full again once you close the window.

![Copy Secret Key](file:///C:/Users/sealt/.gemini/antigravity-ide/brain/9d9ad06c-a8dd-4ffe-a841-64ad74dcf29d/openai_key_generated_1783867208145_redacted.png)

8. Paste this key into the **OpenAI Key** field in the RimSynapse Provider Settings window.

---

## 2. Google Gemini (Gemini 1.5 Pro)

Google Gemini offers a massive context window and lightning-fast vision capabilities. **Google currently offers a generous Free Tier** for Gemini, allowing you to use it without paying a dime (subject to rate limits)!

![Google AI Studio Dashboard](file:///C:/Users/sealt/.gemini/antigravity-ide/brain/9d9ad06c-a8dd-4ffe-a841-64ad74dcf29d/gemini_dashboard_1783867364004.png)

### Steps to Generate an API Key:
1. Navigate to [Google AI Studio](https://aistudio.google.com/app/apikey).
2. Log in with your Google account.
3. Click the **Get API key** button on the left sidebar.
4. Click the white **Create API key** button in the top right.
5. Click **Create API key in new project** (or select an existing one).

![Copy API Key](file:///C:/Users/sealt/.gemini/antigravity-ide/brain/9d9ad06c-a8dd-4ffe-a841-64ad74dcf29d/gemini_api_key_created_1783867644418_redacted.png)

6. Copy your generated API key (it starts with `AIzaSy`).
7. Paste this key into the **Google Gemini Key** field in the RimSynapse Provider Settings window.

---

## 3. Anthropic Claude (Claude 3.5 Sonnet)

Anthropic's Claude 3.5 Sonnet is arguably the best overall model for RimSynapse right now, balancing high-speed responses with top-tier intelligence. **Anthropic uses a Pay-As-You-Go model**, meaning you will need to add a small amount of billing credits to use the API. 

### Steps to Generate an API Key:
1. Navigate to the [Anthropic API Console](https://console.anthropic.com/settings/keys).
2. Log in with your email or Google account.
3. If this is your first time, you may need to verify a phone number and add a minimum balance of $5 to your billing account.
4. Click the black **Create key** button in the top right.
5. Name your key something recognizable like "RimSynapse" and click Create.

![Copy Anthropic Key](file:///C:/Users/sealt/.gemini/antigravity-ide/brain/9d9ad06c-a8dd-4ffe-a841-64ad74dcf29d/claude_api_keys_1783868238832_redacted.png)

6. Copy the generated API key immediately (it starts with `sk-ant-`).
7. Paste this key into the **Anthropic Claude Key** field in the RimSynapse Provider Settings window.

---

### Security Reminder
**Never share your API keys.** If you accidentally paste your API key in a public Discord server or GitHub issue, the provider will automatically detect it, revoke the key, and you will have to generate a new one.

---

## 4. ElevenLabs (TTS Audio)

ElevenLabs provides the gold standard in AI speech synthesis for immersive roleplay. They offer an incredible Free tier (10,000 characters per month) if you create an account, making it perfect for trying out high-quality game voices.

![ElevenLabs Signup](file:///d:/github/RimSynapse-Wiki/RimSynapse-Core/images/elevenlabs_signup.png)

### Steps to Generate an API Key:
1. Navigate to [ElevenLabs](https://elevenlabs.io/).
2. Create an account or log in.
3. If presented with a subscription pricing page during signup, you can simply click **Skip** at the bottom to remain on the free tier.
![ElevenLabs Skip Subscription](file:///d:/github/RimSynapse-Wiki/RimSynapse-Core/images/elevenlabs_skip.png)

> [!TIP]
> **Shortcut:** Once logged in, you can bypass the navigation steps below by going directly to the API Keys page: [https://elevenlabs.io/app/api/api-keys](https://elevenlabs.io/app/api/api-keys)

4. **Manual Navigation:** Expand the left menu on the dashboard, and select **ElevenAPI** from the top-left dropdown (which may default to ElevenCreative or ElevenAgents).
![ElevenLabs API Menu](file:///d:/github/RimSynapse-Wiki/RimSynapse-Core/images/elevenlabs_menu.png)
5. Once in the ElevenAPI section, scroll to the bottom of the page to the **Quick Links** section and click **Create an API Key**.
![ElevenLabs Quick Links](file:///d:/github/RimSynapse-Wiki/RimSynapse-Core/images/elevenlabs_quicklinks.png)
6. A dialog box will appear. Give your key a name, and ensure that both the **Text to Speech** endpoint is toggled to **Access** and the **Voices** endpoint is toggled to **Read**. (This allows the mod to fetch your custom voices). Then click **Create Key**.
![ElevenLabs API Permissions](file:///d:/github/RimSynapse-Wiki/RimSynapse-Core/images/elevenlabs_apikey_permissions.png)
7. Click the eye icon next to your new API key to reveal it, then copy it.
8. Paste this key into the **ElevenLabs Key** field in the RimSynapse Provider Settings window.
