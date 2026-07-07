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
        public string lmStudioApiKey = "";

        // --- Behavior ---
        public bool autoMapModel = true;
        public bool sanitizeResponse = true;
        public bool enableKeepAlive = true;

        // --- Performance ---
        public int timeoutSeconds = 120;
        public int maxRequestsPerMinute = 30;

        // --- Logging ---
        public LogLevel logLevel = LogLevel.Info;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lmStudioUrl, "lmStudioUrl", "http://127.0.0.1:1234");
            Scribe_Values.Look(ref lmStudioApiKey, "lmStudioApiKey", "");
            Scribe_Values.Look(ref autoMapModel, "autoMapModel", true);
            Scribe_Values.Look(ref sanitizeResponse, "sanitizeResponse", true);
            Scribe_Values.Look(ref enableKeepAlive, "enableKeepAlive", true);
            Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 120);
            Scribe_Values.Look(ref maxRequestsPerMinute, "maxRequestsPerMinute", 30);
            Scribe_Values.Look(ref logLevel, "logLevel", LogLevel.Info);
        }
    }
}
