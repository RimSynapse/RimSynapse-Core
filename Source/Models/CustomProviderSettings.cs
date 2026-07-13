using System;
using Verse;

namespace RimSynapse
{
    public class CustomProviderSettings : IExposable
    {
        public string id;
        public string name;
        public string url;
        public string apiKey;
        public string model;
        public LlmCapabilities caps;

        public CustomProviderSettings()
        {
            id = Guid.NewGuid().ToString();
            name = "Custom Provider";
            url = "";
            apiKey = "";
            model = "";
            caps = LlmCapabilities.Text;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", "");
            Scribe_Values.Look(ref name, "name", "Custom Provider");
            Scribe_Values.Look(ref url, "url", "");
            Scribe_Values.Look(ref apiKey, "apiKey", "");
            Scribe_Values.Look(ref model, "model", "");
            Scribe_Values.Look(ref caps, "caps", LlmCapabilities.Text);
        }
    }
}
