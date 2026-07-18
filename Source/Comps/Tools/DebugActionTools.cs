using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Verse;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Dynamically discovers and registers RimWorld's built-in debug actions
    /// (ToolMapForPawns type) as LLM-callable tools at runtime.
    /// </summary>
    public static partial class SynapseToolRegistry
    {
        private static void RegisterDynamicDebugActions()
        {
            try
            {
                var tAttr = HarmonyLib.AccessTools.TypeByName("LudeonTK.DebugActionAttribute");
                var tEnum = HarmonyLib.AccessTools.TypeByName("LudeonTK.DebugActionType");
                if (tAttr == null || tEnum == null) return;

                object toolMapForPawnsVal = Enum.Parse(tEnum, "ToolMapForPawns");
                var actionTypeField = tAttr.GetField("actionType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var nameField = tAttr.GetField("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var categoryField = tAttr.GetField("category", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (actionTypeField == null || nameField == null) return;

                var asm = tAttr.Assembly;
                var methods = asm.GetTypes()
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    .Where(m =>
                    {
                        var attrs = m.GetCustomAttributes(tAttr, true);
                        if (attrs.Length == 0) return false;
                        var attr = attrs[0];
                        object val = actionTypeField.GetValue(attr);
                        if (val == null) return false;
                        if ((int)val != (int)toolMapForPawnsVal) return false;
                        var gps = m.GetParameters();
                        return gps.Length == 1 && gps[0].ParameterType == typeof(Pawn);
                    })
                    .ToList();

                foreach (var method in methods)
                {
                    var attrs = method.GetCustomAttributes(tAttr, true);
                    var attr = attrs[0];
                    string debugName = nameField.GetValue(attr)?.ToString() ?? method.Name;
                    string category = categoryField?.GetValue(attr)?.ToString() ?? "Other";
                    
                    string typePrefix = method.DeclaringType.Name.Replace("DebugTools", "").Replace("DebugActions", "").ToLower();
                    string toolName = "debug_pawn_" + (string.IsNullOrEmpty(typePrefix) ? "" : typePrefix + "_") + method.Name.ToLower();

                    // Avoid duplicate registrations
                    RegisterTool(
                        toolName,
                        $"[DEBUG ACTION] {debugName} (Category: {category}). Directly invokes the game's debug option on the specified colonist.",
                        new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["pawnName"] = new Dictionary<string, string>
                                {
                                    ["type"] = "string",
                                    ["description"] = "The name of the target colonist/pawn."
                                }
                            },
                            ["required"] = new List<string> { "pawnName" }
                        },
                        args =>
                        {
                            if (Find.CurrentMap == null) return "{\"success\": false, \"reason\": \"No active map loaded.\"}";
                            try
                            {
                                var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, string>>(args);
                                if (parsedArgs == null || !parsedArgs.TryGetValue("pawnName", out var pawnName))
                                {
                                    return "{\"success\": false, \"reason\": \"Missing required argument 'pawnName'.\"}";
                                }

                                Pawn pawn = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p => p.LabelShort.Equals(pawnName, StringComparison.OrdinalIgnoreCase));
                                if (pawn == null)
                                {
                                    return $"{{\"success\": false, \"reason\": \"Pawn '{pawnName}' not found on active map.\"}}";
                                }

                                method.Invoke(null, new object[] { pawn });
                                return $"{{\"success\": true, \"message\": \"Invoked debug action '{debugName}' on pawn '{pawnName}' successfully.\"}}";
                            }
                            catch (Exception ex)
                            {
                                return $"{{\"success\": false, \"reason\": \"Failed to execute debug action: {ex.Message}\"}}";
                            }
                        },
                        true
                    );
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimSynapse] Failed to dynamically register debug actions: {ex.Message}");
            }
        }
    }
}
