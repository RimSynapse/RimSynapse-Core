using Verse;

namespace RimSynapse
{
    /// <summary>
    /// Persistent mod settings stored in RimWorld's config directory.
    /// Accessible in-game via Mod Settings → RimSynapse Core.
    /// </summary>
    public class RimSynapseSettings : ModSettings
    {
        // --- Connection ---
        public string lmStudioUrl = "http://127.0.0.1:1234";

        /// <summary>
        /// True if the LM Studio URL points to a remote host instead of localhost.
        /// </summary>
        public bool IsRemoteUrl => !string.IsNullOrEmpty(lmStudioUrl) && 
                                   !lmStudioUrl.Contains("localhost") && 
                                   !lmStudioUrl.Contains("127.0.0.1") && 
                                   !lmStudioUrl.Contains("::1");
        public string lmStudioApiKey = "";

        // --- Behavior ---
        public bool autoMapModel = true;
        public string selectedModel = "";
        public bool sanitizeResponse = true;
        public bool enableKeepAlive = true;
        public bool disableThinking = true;

        // --- Context Embedding ---
        public bool enableContextEmbedding = false;

        // --- Performance ---
        public int timeoutSeconds = 120;
        public int maxRequestsPerMinute = 30;

        // --- Logging ---
        public LogLevel logLevel = LogLevel.Info;
        public bool enableFileLogging = false;

        // --- Notifications ---
        /// <summary>Show VRAM status on game load (default: true). Disable in settings.</summary>
        public bool showVramAdvisory = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lmStudioUrl, "lmStudioUrl", "http://127.0.0.1:1234");
            Scribe_Values.Look(ref lmStudioApiKey, "lmStudioApiKey", "");
            Scribe_Values.Look(ref autoMapModel, "autoMapModel", true);
            Scribe_Values.Look(ref selectedModel, "selectedModel", "");
            Scribe_Values.Look(ref sanitizeResponse, "sanitizeResponse", true);
            Scribe_Values.Look(ref enableKeepAlive, "enableKeepAlive", true);
            Scribe_Values.Look(ref disableThinking, "disableThinking", true);
            Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 120);
            Scribe_Values.Look(ref maxRequestsPerMinute, "maxRequestsPerMinute", 30);
            Scribe_Values.Look(ref enableContextEmbedding, "enableContextEmbedding", false);
            Scribe_Values.Look(ref logLevel, "logLevel", LogLevel.Info);
            Scribe_Values.Look(ref enableFileLogging, "enableFileLogging", false);
            Scribe_Values.Look(ref showVramAdvisory, "showVramAdvisory", true);
        }
    }
}
