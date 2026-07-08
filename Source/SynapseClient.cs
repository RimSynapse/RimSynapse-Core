using System;
using System.Collections.Generic;
using RimSynapse.Internal;

namespace RimSynapse
{
    /// <summary>
    /// Main LLM client API. Consumer mods use this to send chat completions
    /// to LM Studio. All calls are queued, budget-enforced, and async with
    /// callbacks delivered on the main thread.
    /// </summary>
    public static class SynapseClient
    {
        /// <summary>Whether LM Studio was reachable on the last check.</summary>
        public static bool IsOnline => ModelManager.IsOnline;

        /// <summary>
        /// The currently active model ID as reported by LM Studio's API.
        /// Updated automatically via keep-alive polling (~30s cache).
        /// Returns null if no model is loaded or LM Studio is offline.
        /// </summary>
        public static string ActiveModelName => ModelManager.ActiveModel;

        /// <summary>Total queue depth across all mods.</summary>
        public static int TotalQueueDepth => RequestQueue.QueueDepth;

        /// <summary>
        /// Current dynamic throttle level.
        /// 1.0 = full speed, 0.0 = paused.
        /// </summary>
        public static float ThrottleLevel => RequestQueue.ThrottleLevel;

        /// <summary>
        /// GPU stats framework. Populated by an external GPU monitor mod.
        /// Set this property from your GPU monitor mod to push stats into Core.
        /// </summary>
        public static GpuStats Gpu { get; set; } = new GpuStats();

        /// <summary>
        /// Send a chat completion request to LM Studio.
        /// The request is queued and budget-enforced. The callback is invoked
        /// on the main thread when the response arrives.
        /// </summary>
        /// <param name="mod">Handle from <see cref="SynapseCore.Register"/></param>
        /// <param name="messages">OpenAI-format message list</param>
        /// <param name="options">Optional: model, max_tokens, temperature, etc.</param>
        /// <param name="callback">Called on the main thread with the result</param>
        public static void ChatAsync(
            SynapseModHandle mod,
            List<ChatMessage> messages,
            ChatOptions options,
            Action<ChatResult> callback)
        {
            if (mod == null)
            {
                SynapseLog.Error("client",
                    "ChatAsync called with null mod handle. Register first via SynapseCore.Register().");
                callback?.Invoke(ChatResult.Failure("Mod not registered. Call SynapseCore.Register() first."));
                return;
            }

            // Validate messages
            string validationError = InputConverter.Validate(messages);
            if (validationError != null)
            {
                SynapseLog.Warn("client", $"Input validation failed: {validationError}", mod.ModId);
                callback?.Invoke(ChatResult.Failure(validationError));
                return;
            }

            RequestQueue.Enqueue(mod, messages, options ?? ChatOptions.Default, callback);
        }

        /// <summary>
        /// Quick helper: send a system prompt + user message.
        /// Equivalent to ChatAsync with two messages.
        /// </summary>
        public static void PromptAsync(
            SynapseModHandle mod,
            string systemPrompt,
            string userMessage,
            Action<ChatResult> callback,
            ChatOptions options = null)
        {
            var messages = new List<ChatMessage>();
            if (!string.IsNullOrEmpty(systemPrompt))
                messages.Add(ChatMessage.System(systemPrompt));
            if (!string.IsNullOrEmpty(userMessage))
                messages.Add(ChatMessage.User(userMessage));

            ChatAsync(mod, messages, options, callback);
        }

        /// <summary>
        /// Quick helper: send a JSON string as input. Automatically parses
        /// and converts to ChatMessage list.
        /// </summary>
        public static void ChatFromJsonAsync(
            SynapseModHandle mod,
            string json,
            Action<ChatResult> callback,
            ChatOptions options = null)
        {
            var messages = InputConverter.FromJson(json, out string error);
            if (messages == null)
            {
                SynapseLog.Warn("client", $"JSON input conversion failed: {error}", mod?.ModId);
                callback?.Invoke(ChatResult.Failure(error));
                return;
            }

            ChatAsync(mod, messages, options, callback);
        }

        /// <summary>
        /// Quick helper: send an XML string as input. Automatically parses
        /// and converts to ChatMessage list.
        /// </summary>
        public static void ChatFromXmlAsync(
            SynapseModHandle mod,
            string xml,
            Action<ChatResult> callback,
            ChatOptions options = null)
        {
            var messages = InputConverter.FromXml(xml, out string error);
            if (messages == null)
            {
                SynapseLog.Warn("client", $"XML input conversion failed: {error}", mod?.ModId);
                callback?.Invoke(ChatResult.Failure(error));
                return;
            }

            ChatAsync(mod, messages, options, callback);
        }

        /// <summary>
        /// Query loaded models from LM Studio.
        /// Callback runs on the main thread.
        /// </summary>
        public static void GetModelsAsync(Action<ModelsResult> callback)
        {
            ModelManager.GetModels(callback);
        }
    }
}
