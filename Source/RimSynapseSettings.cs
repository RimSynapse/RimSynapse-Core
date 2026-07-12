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

    public enum ProviderRouting
    {
        LocalOnly = 0,
        FirstAvailable = 1,
        Specific_OpenAI = 2,
        Specific_Gemini = 3,
        Specific_Claude = 4,
        Specific_Custom = 5
    }

    /// <summary>
    /// Persistent mod settings stored in RimWorld's config directory.
    /// Accessible in-game via Mod Settings -> RimSynapse Core.
    /// </summary>
    public class RimSynapseSettings : ModSettings
    {
        // --- Connection ---
        public ApiProvider apiProvider = ApiProvider.Local_LMStudio; // Default provider for backwards compatibility
        public string lmStudioUrl = "http://127.0.0.1:1234";
        public string lmStudioApiKey = "";
        
        public string openAiUrl = "https://api.openai.com";
        public string openAiApiKey = "";
        
        public string geminiUrl = "https://generativelanguage.googleapis.com";
        public string geminiApiKey = "";
        
        public string claudeUrl = "https://api.anthropic.com";
        public string claudeApiKey = "";
        
        public string customUrl = "";
        public string customApiKey = "";

        // --- Capabilities ---
        public LlmCapabilities capsLocal = LlmCapabilities.Text;
        public LlmCapabilities capsOpenAi = LlmCapabilities.Text | LlmCapabilities.Image | LlmCapabilities.Vision | LlmCapabilities.Audio;
        public LlmCapabilities capsGemini = LlmCapabilities.Text | LlmCapabilities.Vision | LlmCapabilities.Audio;
        public LlmCapabilities capsClaude = LlmCapabilities.Text | LlmCapabilities.Vision;
        public LlmCapabilities capsCustom = LlmCapabilities.Text;

        // --- Query Routing Ledger ---
        public System.Collections.Generic.Dictionary<string, ProviderRouting> queryRouting = new System.Collections.Generic.Dictionary<string, ProviderRouting>();

        // --- Token Tracking ---
        public int tokensPromptLocal = 0;
        public int tokensCompletionLocal = 0;
        public int tokensPromptOpenAi = 0;
        public int tokensCompletionOpenAi = 0;
        public int tokensPromptGemini = 0;
        public int tokensCompletionGemini = 0;
        public int tokensPromptClaude = 0;
        public int tokensCompletionClaude = 0;
        public int tokensPromptCustom = 0;
        public int tokensCompletionCustom = 0;

        /// <summary>
        /// True if the default API provider is cloud-based.
        /// </summary>
        public bool IsRemoteUrl => apiProvider == ApiProvider.Google_Gemini || 
                                   apiProvider == ApiProvider.OpenAI || 
                                   apiProvider == ApiProvider.Anthropic_Claude ||
                                   (!string.IsNullOrEmpty(lmStudioUrl) && 
                                    !lmStudioUrl.Contains("localhost") && 
                                    !lmStudioUrl.Contains("127.0.0.1") && 
                                    !lmStudioUrl.Contains("::1"));

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
            Scribe_Values.Look(ref openAiUrl, "openAiUrl", "https://api.openai.com");
            Scribe_Values.Look(ref openAiApiKey, "openAiApiKey", "");
            Scribe_Values.Look(ref geminiUrl, "geminiUrl", "https://generativelanguage.googleapis.com");
            Scribe_Values.Look(ref geminiApiKey, "geminiApiKey", "");
            Scribe_Values.Look(ref claudeUrl, "claudeUrl", "https://api.anthropic.com");
            Scribe_Values.Look(ref claudeApiKey, "claudeApiKey", "");
            Scribe_Values.Look(ref customUrl, "customUrl", "");
            Scribe_Values.Look(ref customApiKey, "customApiKey", "");

            Scribe_Collections.Look(ref queryRouting, "queryRouting", LookMode.Value, LookMode.Value);
            if (queryRouting == null) queryRouting = new System.Collections.Generic.Dictionary<string, ProviderRouting>();

            Scribe_Values.Look(ref capsLocal, "capsLocal", LlmCapabilities.Text);
            Scribe_Values.Look(ref capsOpenAi, "capsOpenAi", LlmCapabilities.Text | LlmCapabilities.Image | LlmCapabilities.Vision | LlmCapabilities.Audio);
            Scribe_Values.Look(ref capsGemini, "capsGemini", LlmCapabilities.Text | LlmCapabilities.Vision | LlmCapabilities.Audio);
            Scribe_Values.Look(ref capsClaude, "capsClaude", LlmCapabilities.Text | LlmCapabilities.Vision);
            Scribe_Values.Look(ref capsCustom, "capsCustom", LlmCapabilities.Text);

            Scribe_Values.Look(ref tokensPromptLocal, "tokensPromptLocal", 0);
            Scribe_Values.Look(ref tokensCompletionLocal, "tokensCompletionLocal", 0);
            Scribe_Values.Look(ref tokensPromptOpenAi, "tokensPromptOpenAi", 0);
            Scribe_Values.Look(ref tokensCompletionOpenAi, "tokensCompletionOpenAi", 0);
            Scribe_Values.Look(ref tokensPromptGemini, "tokensPromptGemini", 0);
            Scribe_Values.Look(ref tokensCompletionGemini, "tokensCompletionGemini", 0);
            Scribe_Values.Look(ref tokensPromptClaude, "tokensPromptClaude", 0);
            Scribe_Values.Look(ref tokensCompletionClaude, "tokensCompletionClaude", 0);
            Scribe_Values.Look(ref tokensPromptCustom, "tokensPromptCustom", 0);
            Scribe_Values.Look(ref tokensCompletionCustom, "tokensCompletionCustom", 0);
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
