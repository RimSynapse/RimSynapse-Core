using Verse;

namespace RimSynapse
{
    public class StorytellerVoiceExtension : DefModExtension
    {
        /// <summary>
        /// The path to a local voice cloning file (e.g., .wav) used by local TTS engines like Jan or Voicebox.
        /// </summary>
        public string localVoicePath = "";

        /// <summary>
        /// The Voice ID to use when routing through ElevenLabs.
        /// </summary>
        public string elevenLabsVoiceId = "";

        /// <summary>
        /// The Voice ID to use when routing through OpenAI (e.g., "nova", "alloy").
        /// </summary>
        public string openAIVoiceId = "";
    }
}
