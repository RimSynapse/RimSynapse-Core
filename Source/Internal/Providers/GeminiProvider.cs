using System;
using System.Net.Http;

namespace RimSynapse.Internal.Providers
{
    public class GeminiProvider : ILlmProvider
    {
        private readonly OpenAiProvider _openAiProxy;

        public GeminiProvider(HttpClient client)
        {
            // Google Gemini provides an OpenAI-compatible endpoint.
            // For now, we will proxy Gemini requests through the OpenAiProvider
            // to leverage their compatibility layer, ensuring EnforceJson works correctly.
            _openAiProxy = new OpenAiProvider(client);
        }

        public ChatResult SendTextRequestSync(LlmTextRequest request, string baseUrl, string apiKey, string model)
        {
            return _openAiProxy.SendTextRequestSync(request, baseUrl, apiKey, model);
        }

        public ChatResult SendVisionRequestSync(LlmVisionRequest request, string baseUrl, string apiKey, string model)
        {
            return _openAiProxy.SendVisionRequestSync(request, baseUrl, apiKey, model);
        }

        public ImageResult SendImageRequestSync(LlmImageRequest request, string baseUrl, string apiKey, string model)
        {
            return ImageResult.Failure("Gemini Image generation not implemented yet.");
        }

        public AudioResult SendAudioRequestSync(LlmAudioRequest request, string baseUrl, string apiKey, string model)
        {
            return AudioResult.Failure("Gemini Audio generation not implemented yet.");
        }
    }
}
