using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Validates and converts input from various formats (JSON string, XML string,
    /// dictionary) into a List&lt;ChatMessage&gt; ready for the LLM.
    /// </summary>
    internal static class InputConverter
    {
        /// <summary>
        /// Validate a list of ChatMessages. Returns an error string if invalid, null if OK.
        /// </summary>
        internal static string Validate(List<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0)
                return "Messages list is null or empty.";

            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (msg == null)
                    return $"Message at index {i} is null.";

                if (string.IsNullOrEmpty(msg.role))
                    return $"Message at index {i} has no role.";

                if (msg.role != "system" && msg.role != "user" && msg.role != "assistant")
                    return $"Message at index {i} has invalid role: \"{msg.role}\". " +
                           $"Expected: system, user, or assistant.";

                if (string.IsNullOrEmpty(msg.content))
                {
                    SynapseLog.Warn("input",
                        $"Message at index {i} (role: {msg.role}) has empty content.");
                }

                // Warn on very long messages (likely will blow context window)
                if (msg.content != null && msg.content.Length > 50000)
                {
                    SynapseLog.Warn("input",
                        $"Message at index {i} is very long ({msg.content.Length} chars). " +
                        $"This may exceed the model's context window.");
                }
            }

            return null; // Valid
        }

        /// <summary>
        /// Try to parse a JSON string into a ChatMessage list.
        /// Supports formats:
        /// - Array of messages: [{"role": "user", "content": "..."}]
        /// - Single message object: {"role": "user", "content": "..."}
        /// - Simple prompt: {"prompt": "..."}
        /// - System + user: {"system": "...", "user": "..."}
        /// </summary>
        internal static List<ChatMessage> FromJson(string json, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON string is null or empty.";
                return null;
            }

            try
            {
                string trimmed = json.Trim();

                // Array of messages
                if (trimmed.StartsWith("["))
                {
                    var array = JArray.Parse(trimmed);
                    var messages = new List<ChatMessage>();
                    foreach (var item in array)
                    {
                        messages.Add(new ChatMessage
                        {
                            role = item["role"]?.ToString() ?? "user",
                            content = item["content"]?.ToString() ?? "",
                        });
                    }
                    error = Validate(messages);
                    return error == null ? messages : null;
                }

                // Single object
                var obj = JObject.Parse(trimmed);

                // Array inside object: {"messages": [...]}
                if (obj["messages"] is JArray msgArray)
                {
                    var messages = new List<ChatMessage>();
                    foreach (var item in msgArray)
                    {
                        messages.Add(new ChatMessage
                        {
                            role = item["role"]?.ToString() ?? "user",
                            content = item["content"]?.ToString() ?? "",
                        });
                    }
                    error = Validate(messages);
                    return error == null ? messages : null;
                }

                // Simple prompt: {"prompt": "..."}
                if (obj["prompt"] != null)
                {
                    var messages = new List<ChatMessage>
                    {
                        ChatMessage.User(obj["prompt"].ToString()),
                    };
                    return messages;
                }

                // System + user: {"system": "...", "user": "..."}
                if (obj["system"] != null || obj["user"] != null)
                {
                    var messages = new List<ChatMessage>();
                    if (obj["system"] != null)
                        messages.Add(ChatMessage.System(obj["system"].ToString()));
                    if (obj["user"] != null)
                        messages.Add(ChatMessage.User(obj["user"].ToString()));
                    if (messages.Count == 0)
                    {
                        error = "Both 'system' and 'user' fields are empty.";
                        return null;
                    }
                    return messages;
                }

                // Single message object: {"role": "user", "content": "..."}
                if (obj["role"] != null && obj["content"] != null)
                {
                    var messages = new List<ChatMessage>
                    {
                        new ChatMessage
                        {
                            role = obj["role"].ToString(),
                            content = obj["content"].ToString(),
                        }
                    };
                    error = Validate(messages);
                    return error == null ? messages : null;
                }

                error = "Unrecognized JSON format. Expected messages array, prompt object, or system+user object.";
                return null;
            }
            catch (JsonException ex)
            {
                error = $"JSON parse error: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Try to extract chat messages from an XML string.
        /// Supports format:
        /// &lt;messages&gt;
        ///   &lt;message role="system"&gt;...&lt;/message&gt;
        ///   &lt;message role="user"&gt;...&lt;/message&gt;
        /// &lt;/messages&gt;
        /// </summary>
        internal static List<ChatMessage> FromXml(string xml, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(xml))
            {
                error = "XML string is null or empty.";
                return null;
            }

            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);

                var messages = new List<ChatMessage>();
                var messageNodes = doc.SelectNodes("//message");
                if (messageNodes == null || messageNodes.Count == 0)
                {
                    error = "No <message> elements found in XML.";
                    return null;
                }

                foreach (System.Xml.XmlNode node in messageNodes)
                {
                    string role = node.Attributes?["role"]?.Value ?? "user";
                    string content = node.InnerText ?? "";
                    messages.Add(new ChatMessage(role, content));
                }

                error = Validate(messages);
                return error == null ? messages : null;
            }
            catch (System.Xml.XmlException ex)
            {
                error = $"XML parse error: {ex.Message}";
                return null;
            }
        }
    }
}
