namespace RimSynapse
{
    public enum ApiProvider
    {
        Local_LMStudio = 0,
        Google_Gemini = 1,
        OpenAI = 2,
        Anthropic_Claude = 3,
        Custom = 4,
        Pollinations = 5,
        ElevenLabs = 6,
        Local_Jan = 7,
        Voicebox = 8
    }

    // Deprecated, use string routing IDs instead.
    public enum ProviderRouting
    {
        LocalOnly = 0,
        FirstAvailable = 1,
        Specific_OpenAI = 2,
        Specific_Gemini = 3,
        Specific_Claude = 4,
        Specific_Custom = 5
    }

    public static class RoutingId
    {
        public const string LocalOnly = "LocalOnly";
        public const string OpenAI = "Specific_OpenAI";
        public const string Gemini = "Specific_Gemini";
        public const string Claude = "Specific_Claude";
        public const string CustomPrefix = "Custom_";
        public const string Pollinations = "Pollinations.ai";
        public const string ElevenLabs = "Specific_ElevenLabs";
        public const string Jan = "Local_Jan";
        public const string Voicebox = "Specific_Voicebox";
    }
}
