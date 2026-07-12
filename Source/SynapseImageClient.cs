using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using System.Collections.Generic;

namespace RimSynapse
{
    public static class SynapseImageClient
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        private static string AssetsDir
        {
            get
            {
                return Path.Combine(GenFilePaths.ConfigFolderPath, "RimSynapseAssets");
            }
        }

        public static void CleanupOrphanedAssets()
        {
            if (!Directory.Exists(AssetsDir)) return;

            string savesDir = GenFilePaths.SaveDataFolderPath;
            var assetFolders = Directory.GetDirectories(AssetsDir);

            foreach (var folder in assetFolders)
            {
                string saveName = new DirectoryInfo(folder).Name;
                string correspondingSave = Path.Combine(savesDir, saveName + ".rws");

                if (!File.Exists(correspondingSave))
                {
                    try
                    {
                        Directory.Delete(folder, true);
                        SynapseLogger.Message($"Deleted orphaned asset folder for deleted save: {saveName}");
                    }
                    catch (Exception ex)
                    {
                        SynapseLogger.Error($"Failed to delete orphaned asset folder {folder}: {ex.Message}");
                    }
                }
            }
        }

        public static void GenerateAndSaveImageAsync(
            SynapseModHandle mod, 
            string queryId, 
            string subjectContext, 
            string artStyle, 
            Action<Texture2D, string> callback)
        {
            if (mod == null)
            {
                SynapseLogger.Error("GenerateAndSaveImageAsync called with null mod handle.");
                callback?.Invoke(null, null);
                return;
            }

            var messages = new List<ChatMessage>
            {
                ChatMessage.System("You are a prompt engineer. Write a single-sentence visual description of the subject provided by the user. Do not include extra commentary."),
                ChatMessage.User(subjectContext)
            };

            var options = new ChatOptions
            {
                queryId = queryId,
                requestName = "Generating Image Prompt",
                maxTokens = 150,
                temperature = 0.7f
            };

            SynapseClient.ChatAsync(mod, messages, options, (result) =>
            {
                if (!result.success)
                {
                    SynapseLogger.Warning($"Image prompt generation failed: {result.error}", mod.ModId);
                    callback?.Invoke(null, null);
                    return;
                }

                string prompt = result.content.Trim();
                string finalPrompt = $"{prompt}, {artStyle}";
                
                string worldName = Find.World?.info?.name ?? "UnknownWorld";
                int tick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                string saveName = Current.Game?.Info?.permadeathMode == true ? Current.Game.Info.permadeathModeUniqueName : worldName;
                
                if (string.IsNullOrEmpty(saveName)) saveName = worldName;
                
                string safeSaveName = string.Join("_", saveName.Split(Path.GetInvalidFileNameChars()));
                string safeWorldName = string.Join("_", worldName.Split(Path.GetInvalidFileNameChars()));

                Task.Run(async () =>
                {
                    try
                    {
                        string url = $"https://image.pollinations.ai/prompt/{Uri.EscapeDataString(finalPrompt)}?width=512&height=512&nologo=true";
                        byte[] imageBytes = await _httpClient.GetByteArrayAsync(url);

                        string saveDir = Path.Combine(AssetsDir, safeSaveName);
                        Directory.CreateDirectory(saveDir);
                        
                        int index = 0;
                        string filePath = Path.Combine(saveDir, $"{safeWorldName}_{tick}_{index}.jpg");
                        while (File.Exists(filePath))
                        {
                            index++;
                            filePath = Path.Combine(saveDir, $"{safeWorldName}_{tick}_{index}.jpg");
                        }

                        File.WriteAllBytes(filePath, imageBytes);

                        SynapseGameComponent.Enqueue(() =>
                        {
                            Texture2D tex = new Texture2D(2, 2);
                            tex.LoadImage(imageBytes);
                            callback?.Invoke(tex, filePath);
                        });
                    }
                    catch (Exception ex)
                    {
                        SynapseLogger.Error($"Failed to download or save image from Pollinations: {ex.Message}");
                        SynapseGameComponent.Enqueue(() =>
                        {
                            callback?.Invoke(null, null);
                        });
                    }
                });
            });
        }
    }
}
