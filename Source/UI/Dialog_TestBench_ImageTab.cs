using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSynapse.UI
{
    /// <summary>
    /// Test bench image tab: image generation testing with provider selection.
    /// </summary>
    public partial class Dialog_TestBench
    {
        private void DrawImageTab(Listing_Standard listing, RimSynapseSettings settings, Rect inRect)
        {
            if (_renderNextFrame && _imageToRenderBase64 != null)
            {
                _renderNextFrame = false;
                LoadTextureFromBase64(_imageToRenderBase64);
                _imageToRenderBase64 = null;
            }

            if (listing.ButtonText($"Target Provider: {_selectedRoutingIdImage}"))
            {
                var list = new List<FloatMenuOption>();
                list.Add(new FloatMenuOption(RoutingId.LocalOnly, () => _selectedRoutingIdImage = RoutingId.LocalOnly));
                list.Add(new FloatMenuOption(RoutingId.Jan, () => _selectedRoutingIdImage = RoutingId.Jan));
                list.Add(new FloatMenuOption("OpenAI", () => _selectedRoutingIdImage = RoutingId.OpenAI));
                list.Add(new FloatMenuOption(RoutingId.Pollinations, () => _selectedRoutingIdImage = RoutingId.Pollinations));
                foreach(var custom in settings.customProviders)
                {
                    string id = RoutingId.CustomPrefix + custom.id;
                    list.Add(new FloatMenuOption($"Custom: {custom.name}", () => _selectedRoutingIdImage = id));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }

            listing.Gap(10f);
            
            listing.Label("Target Model:");
            float selectBtnWidth = 80f;
            Rect modelRectImg = listing.GetRect(24f);
            Rect modelFieldRectImg = new Rect(modelRectImg.x, modelRectImg.y, modelRectImg.width - selectBtnWidth - 4f, modelRectImg.height);
            Rect selectBtnRectImg = new Rect(modelRectImg.xMax - selectBtnWidth, modelRectImg.y, selectBtnWidth, modelRectImg.height);
            
            if (pendingModelSelections.TryGetValue("Image", out string newImgM))
            {
                _selectedModelImage = newImgM;
                pendingModelSelections.Remove("Image");
            }
            
            _selectedModelImage = Widgets.TextField(modelFieldRectImg, _selectedModelImage);
            
            if (Widgets.ButtonText(selectBtnRectImg, "Select..."))
            {
                ApiProvider? pEnum = null;
                if (_selectedRoutingIdImage == RoutingId.LocalOnly) pEnum = ApiProvider.Local_LMStudio;
                else if (_selectedRoutingIdImage == RoutingId.Jan) pEnum = ApiProvider.Local_Jan;
                else if (_selectedRoutingIdImage == RoutingId.OpenAI) pEnum = ApiProvider.OpenAI;
                else if (_selectedRoutingIdImage == RoutingId.Gemini) pEnum = ApiProvider.Google_Gemini;
                else if (_selectedRoutingIdImage == RoutingId.Claude) pEnum = ApiProvider.Anthropic_Claude;
                else if (_selectedRoutingIdImage == RoutingId.Pollinations) pEnum = ApiProvider.Pollinations;
                else if (_selectedRoutingIdImage != null && _selectedRoutingIdImage.StartsWith(RoutingId.CustomPrefix)) pEnum = ApiProvider.Custom;
                
                if (pEnum.HasValue)
                {
                    RimSynapse.Internal.ModelDefUtility.ShowModelSelector(pEnum.Value, LlmCapabilities.Image, (selectedModel) => {
                        pendingModelSelections["Image"] = selectedModel;
                    });
                }
            }

            listing.Gap(10f);
            listing.Label("Image Prompt:");
            _customPromptImage = listing.TextEntry(_customPromptImage, 3);
            listing.Gap(4f);

            if (listing.ButtonText(_testBusyImage ? "Generating Image..." : "Generate Image"))
            {
                if (!_testBusyImage && !string.IsNullOrWhiteSpace(_customPromptImage))
                {
                    RunTestImage(_customPromptImage);
                }
            }

            if (!string.IsNullOrEmpty(_testStatusImage))
            {
                listing.Gap(10f);
                var prevColor = GUI.color;
                GUI.color = _testStatusColorImage;
                listing.Label(_testStatusImage);
                GUI.color = prevColor;
            }

            if (_textureToRender != null)
            {
                float imgSize = 400f;
                float startY = 320f;
                Rect imgRect = new Rect((inRect.width - imgSize) / 2f, startY, imgSize, imgSize);
                GUI.DrawTexture(imgRect, _textureToRender, ScaleMode.ScaleToFit);
            }
        }

        private void RunTestImage(string text)
        {
            _testBusyImage = true;
            _testStatusImage = $"Sending Image request to {_selectedRoutingIdImage} (model: {_selectedModelImage})...";
            _testStatusColorImage = Color.yellow;
            _textureToRender = null;

            if (_testHandleImage == null)
                _testHandleImage = SynapseCore.Register("rimsynapse.imagetest", "RimSynapse Image Test");

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Internal.HttpEngine.EnsureInitialized();
                    var options = ChatOptions.Default;
                    options.model = _selectedModelImage;
                    options.providerOverride = _selectedRoutingIdImage;
                    
                    RimSynapseMod.Instance.Settings.queryRoutingIds["rimsynapse.imagetest:default"] = _selectedRoutingIdImage;
                    
                    var req = new LlmImageRequest { Prompt = text };
                    var resultObj = Internal.HttpEngine.RouteRequestSync(_testHandleImage, req, LlmCapabilities.Image, options);
                    var result = resultObj as ImageResult;

                    if (result != null && result.success)
                    {
                        _testStatusImage = $"[{result.durationMs}ms | {result.model}]\nSuccess!";
                        _testStatusColorImage = Color.green;

                        _imageToRenderBase64 = result.base64Data;
                        _renderNextFrame = true;
                    }
                    else
                    {
                        string err = result != null ? result.error : "Unknown routing error";
                        _testStatusImage = $"Error: {err}";
                        _testStatusColorImage = Color.red;
                    }
                }
                catch (Exception ex)
                {
                    _testStatusImage = $"Error: {ex.Message}";
                    _testStatusColorImage = Color.red;
                }
                finally
                {
                    _testBusyImage = false;
                }
            });
        }

        private void LoadTextureFromBase64(string base64)
        {
            try
            {
                byte[] imgBytes = Convert.FromBase64String(base64);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(imgBytes);
                _textureToRender = tex;
            }
            catch (Exception ex)
            {
                SynapseLogger.Error($"Image render error: {ex.Message}");
                _testStatusImage = $"Render error: {ex.Message}";
                _testStatusColorImage = Color.red;
            }
        }
    }
}
