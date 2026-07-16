using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Describes a single game tool the LLM can invoke.
    /// </summary>
    public class GameTool
    {
        public string name;
        public string description;
        public object parameters; // JSON Schema parameter description
        public Func<string, string> handler;
        public bool isDebugAction = false;
    }

    /// <summary>
    /// Central registry for all game tools the LLM can call.
    /// Tool handler registrations are split across partial-class files in the Tools/ subfolder.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static readonly Dictionary<string, GameTool> _tools = new Dictionary<string, GameTool>();
        private static bool _initialized = false;

        public static Func<Pawn, string, string, int?, int?, bool> CustomBreakHandler;

        public static void RegisterTool(string name, string description, object parametersSchema, Func<string, string> handler, bool isDebug = false)
        {
            EnsureInitialized();
            _tools[name] = new GameTool
            {
                name = name,
                description = description,
                parameters = parametersSchema,
                handler = handler,
                isDebugAction = isDebug
            };
        }

        public static IEnumerable<GameTool> AllTools
        {
            get
            {
                EnsureInitialized();
                return _tools.Values;
            }
        }

        public static IEnumerable<GameTool> NonDebugTools
        {
            get
            {
                EnsureInitialized();
                return _tools.Values.Where(t => !t.isDebugAction);
            }
        }

        public static string ExecuteTool(string name, string argumentsJson)
        {
            EnsureInitialized();
            if (_tools.TryGetValue(name, out var tool))
            {
                try
                {
                    return tool.handler(argumentsJson);
                }
                catch (Exception ex)
                {
                    return $"{{\"error\": \"Exception during tool execution: {ex.Message}\"}}";
                }
            }
            return $"{{\"error\": \"Tool '{name}' not found.\"}}";
        }

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // Register Built-in Tools
            RegisterMetaTools();
            RegisterColonistTools();
            RegisterStockpileTools();
            RegisterThreatTools();
            RegisterMoodTools();
            RegisterEnvironmentTools();
            RegisterIncidentTools();
            RegisterPossessionTools();
            RegisterBreakTools();
            RegisterCombatTools();
            RegisterHackingTools();
            RegisterPawnStateTools();
            RegisterObjectStateTools();
            RegisterDynamicDebugActions();
        }
    }
}
