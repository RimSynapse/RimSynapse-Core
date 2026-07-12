using System;

namespace RimSynapse.Internal.Providers
{
    /// <summary>
    /// Interface for provider-specific LLM translators.
    /// Each provider module implements this to translate unified requests into bespoke JSON.
    /// </summary>
    public interface ILlmProvider
    {
        ChatResult SendTextRequestSync(LlmTextRequest request, string baseUrl, string apiKey, string model);
        ChatResult SendVisionRequestSync(LlmVisionRequest request, string baseUrl, string apiKey, string model);
        ImageResult SendImageRequestSync(LlmImageRequest request, string baseUrl, string apiKey, string model);
        AudioResult SendAudioRequestSync(LlmAudioRequest request, string baseUrl, string apiKey, string model);
    }
}
