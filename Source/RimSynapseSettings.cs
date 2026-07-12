using Verse;

namespace RimSynapse
{
    public enum ApiProvider
    {
        Local_LMStudio = 0,
        Google_Gemini = 1,
        OpenAI = 2,
        Anthropic_Claude = 3,
        Custom = 4
    }

    /// <summary>
    /// Persistent mod settings stored in RimWorld's config directory.
    /// Accessible in-game via Mod Settings → RimSynapse Core.
    /// </summary>
    public class RimSynapseSettings : ModSettings
    {
        // --- Connection ---
        public ApiProvider apiProvider = ApiProvider.Local_LMStudio;
        public string lmStudioUrl = "http://127.0.0.1:1234";

        /// <summary>
        /// True if using a cloud provider, or if the Custom/LMStudio URL points to a remote host.
        /// </summary>
        public bool IsRemoteUrl => apiProvider == ApiProvider.Google_Gemini || 
                                   apiProvider == ApiProvider.OpenAI || 
                                   apiProvider == ApiProvider.Anthropic_Claude ||
                                   (!string.IsNullOrEmpty(lmStudioUrl) && 
                                    !lmStudioUrl.Contains("localhost") && 
                                    !lmStudioUrl.Contains("127.0.0.1") && 
                                    !lmStudioUrl.Contains("::1"));
        public string lmStudioApiKey = "";

        // --- Behavior ---
        public bool autoMapModel = true;
        public string selectedModel = "";
        public bool sanitizeResponse = true;
        public bool enableKeepAlive = true;
        public bool disableThinking = true;

        // --- Context Embedding ---
        public bool enableContextEmbedding = false;
        public float shortTermMemoryHours = 48f;

        // --- Performance ---
        public int timeoutSeconds = 120;
        public int maxRequestsPerMinute = 30;
        public int maxConcurrentRequests = 2;

        // --- Opportunistic Tasks ---
        /// <summary>-1 = Auto, 0 = Aggressive, 1 = Balanced, 2 = Conservative</summary>
        public int opportunisticThrottleMode = -1;
        /// <summary>Max tasks to fire per idle check in Aggressive mode (1-5).</summary>
        public int opportunisticBurstSize = 3;

        // --- Logging & Troubleshooting ---
        public bool traceDebugMode = false;
        public bool testIdeologyActive = true;
        public bool testRoyaltyActive = true;
        public bool testBiotechActive = true;
        public bool testAnomalyActive = true;

        // --- Notifications ---
        /// <summary>Show VRAM status on game load (default: true). Disable in settings.</summary>
        public bool showVramAdvisory = true;

        /// <summary>Show Queue Monitor icon in the bottom-right toolbar (default: true).</summary>
        public bool showQueueMonitorIcon = true;

        // --- Queue Monitor Columns ---
        public bool qmShowPrio = true;
        public bool qmShowMod = true;
        public bool qmShowTarget = true;
        public bool qmShowTask = true;
        public bool qmShowAge = true;
        
        public bool qmShowStatus = false;
        public bool qmShowScore = false;
        public bool qmShowTimeout = false;
        public bool qmShowTokens = false;
        public bool qmShowPrompt = false;
        public bool qmShowResponse = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref apiProvider, "apiProvider", ApiProvider.Local_LMStudio);
            Scribe_Values.Look(ref lmStudioUrl, "lmStudioUrl", "http://127.0.0.1:1234");
            Scribe_Values.Look(ref lmStudioApiKey, "lmStudioApiKey", "");
            Scribe_Values.Look(ref autoMapModel, "autoMapModel", true);
            Scribe_Values.Look(ref selectedModel, "selectedModel", "");
            Scribe_Values.Look(ref sanitizeResponse, "sanitizeResponse", true);
            Scribe_Values.Look(ref enableKeepAlive, "enableKeepAlive", true);
            Scribe_Values.Look(ref disableThinking, "disableThinking", true);
            Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 120);
            Scribe_Values.Look(ref maxRequestsPerMinute, "maxRequestsPerMinute", 30);
            Scribe_Values.Look(ref maxConcurrentRequests, "maxConcurrentRequests", 2);
            Scribe_Values.Look(ref opportunisticThrottleMode, "opportunisticThrottleMode", -1);
            Scribe_Values.Look(ref opportunisticBurstSize, "opportunisticBurstSize", 3);
            Scribe_Values.Look(ref enableContextEmbedding, "enableContextEmbedding", false);
            Scribe_Values.Look(ref shortTermMemoryHours, "shortTermMemoryHours", 48f);
            Scribe_Values.Look(ref traceDebugMode, "traceDebugMode", false);
            Scribe_Values.Look(ref testIdeologyActive, "testIdeologyActive", true);
            Scribe_Values.Look(ref testRoyaltyActive, "testRoyaltyActive", true);
            Scribe_Values.Look(ref testBiotechActive, "testBiotechActive", true);
            Scribe_Values.Look(ref testAnomalyActive, "testAnomalyActive", true);
            Scribe_Values.Look(ref showVramAdvisory, "showVramAdvisory", true);
            Scribe_Values.Look(ref showQueueMonitorIcon, "showQueueMonitorIcon", true);

            Scribe_Values.Look(ref qmShowPrio, "qmShowPrio", true);
            Scribe_Values.Look(ref qmShowMod, "qmShowMod", true);
            Scribe_Values.Look(ref qmShowTarget, "qmShowTarget", true);
            Scribe_Values.Look(ref qmShowTask, "qmShowTask", true);
            Scribe_Values.Look(ref qmShowAge, "qmShowAge", true);
            
            Scribe_Values.Look(ref qmShowStatus, "qmShowStatus", false);
            Scribe_Values.Look(ref qmShowScore, "qmShowScore", false);
            Scribe_Values.Look(ref qmShowTimeout, "qmShowTimeout", false);
            Scribe_Values.Look(ref qmShowTokens, "qmShowTokens", false);
            Scribe_Values.Look(ref qmShowPrompt, "qmShowPrompt", false);
            Scribe_Values.Look(ref qmShowResponse, "qmShowResponse", false);
        }
    }
}
