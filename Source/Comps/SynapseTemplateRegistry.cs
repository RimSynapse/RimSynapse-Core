using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Verse;

namespace RimSynapse
{
    public class PromptTemplate
    {
        public string modId;
        public string templateId;
        public string template;
        public string fingerprint;
    }

    public static class SynapseTemplateRegistry
    {
        private static readonly Dictionary<string, PromptTemplate> LoadedTemplates = new Dictionary<string, PromptTemplate>();
        private static bool _initialized = false;

        private static string StorageDirectory
        {
            get
            {
                string path = Path.Combine(GenFilePaths.ConfigFolderPath, "RimSynapse_Templates");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                string dir = StorageDirectory;
                var files = Directory.GetFiles(dir, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var template = JsonConvert.DeserializeObject<PromptTemplate>(json);
                        if (template != null && !string.IsNullOrEmpty(template.modId) && !string.IsNullOrEmpty(template.templateId))
                        {
                            string key = $"{template.modId}_{template.templateId}".ToLower();
                            LoadedTemplates[key] = template;
                        }
                    }
                    catch (Exception ex)
                    {
                        SynapseLogger.Error($"Failed to load template file '{file}': {ex.Message}");
                    }
                }
                SynapseLogger.Message($"Initialized SynapseTemplateRegistry: Loaded {LoadedTemplates.Count} templates from disk.");
            }
            catch (Exception ex)
            {
                SynapseLogger.Error($"Failed to initialize SynapseTemplateRegistry: {ex.Message}");
            }
        }

        public static string CalculateFingerprint(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            using (var sha = SHA256.Create())
            {
                byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public static void RegisterTemplate(string modId, string templateId, string templateText)
        {
            Initialize();

            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(templateId)) return;

            string key = $"{modId}_{templateId}".ToLower();
            string fingerprint = CalculateFingerprint(templateText);

            var promptTemplate = new PromptTemplate
            {
                modId = modId,
                templateId = templateId,
                template = templateText,
                fingerprint = fingerprint
            };

            // Cache in memory
            LoadedTemplates[key] = promptTemplate;

            // Persist to disk
            try
            {
                string filePath = Path.Combine(StorageDirectory, $"{modId}_{templateId}.json");
                string json = JsonConvert.SerializeObject(promptTemplate, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                SynapseLogger.Error($"Failed to write template '{templateId}' for mod '{modId}' to disk: {ex.Message}");
            }
        }

        public static List<string> SyncHandshake(string modId, Dictionary<string, string> clientTemplates)
        {
            Initialize();

            var actionRequired = new List<string>();
            if (clientTemplates == null) return actionRequired;

            foreach (var kvp in clientTemplates)
            {
                string templateId = kvp.Key;
                string clientFingerprint = kvp.Value;

                string key = $"{modId}_{templateId}".ToLower();
                if (LoadedTemplates.TryGetValue(key, out var cachedTemplate))
                {
                    if (cachedTemplate.fingerprint != clientFingerprint)
                    {
                        // Fingerprint mismatch, need refresh
                        actionRequired.Add(templateId);
                    }
                }
                else
                {
                    // Template not found in Core registry, needs registration
                    actionRequired.Add(templateId);
                }
            }

            return actionRequired;
        }

        public static string GetTemplateText(string modId, string templateId)
        {
            Initialize();

            string key = $"{modId}_{templateId}".ToLower();
            if (LoadedTemplates.TryGetValue(key, out var promptTemplate))
            {
                return promptTemplate.template;
            }
            return "";
        }
    }
}
