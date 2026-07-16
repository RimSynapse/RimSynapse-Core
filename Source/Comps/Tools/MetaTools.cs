using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Meta-tools: list_available_tools and execute_game_tool.
    /// These let the LLM discover and invoke tools dynamically.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterMetaTools()
        {
            // Meta-Tool: list_available_tools
            RegisterTool(
                "list_available_tools",
                "Get the directory of all available game tools, including their names, descriptions, and required argument schemas.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                },
                args =>
                {
                    var list = new List<object>();
                    foreach (var tool in _tools.Values)
                    {
                        if (tool.isDebugAction || tool.name == "list_available_tools" || tool.name == "execute_game_tool")
                            continue;

                        list.Add(new
                        {
                            name = tool.name,
                            description = tool.description,
                            parameterSchema = tool.parameters
                        });
                    }
                    return JsonConvert.SerializeObject(list);
                }
            );

            // Meta-Tool: execute_game_tool
            RegisterTool(
                "execute_game_tool",
                "Execute any game tool from the directory by name and passing a JSON arguments string.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["tool_name"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The exact name of the tool to execute."
                        },
                        ["arguments_json"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The JSON string of the arguments to pass. E.g. '{}' if the tool has no parameters."
                        }
                    },
                    ["required"] = new List<string> { "tool_name", "arguments_json" }
                },
                args =>
                {
                    try
                    {
                        var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(args);
                        if (data == null || !data.TryGetValue("tool_name", out string toolName))
                        {
                            return "{\"error\": \"Missing tool_name parameter.\"}";
                        }
                        
                        string subArgs = "{}";
                        if (data.TryGetValue("arguments_json", out string tempArgs))
                        {
                            subArgs = tempArgs;
                        }

                        return ExecuteTool(toolName, subArgs);
                    }
                    catch (Exception ex)
                    {
                        return $"{{\"error\": \"Failed to parse meta-tool arguments: {ex.Message}\"}}";
                    }
                }
            );
        }
    }
}
