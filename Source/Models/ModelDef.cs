using System;
using System.Collections.Generic;
using Verse;

namespace RimSynapse
{
    public class ModelDef : Def
    {
        public ApiProvider provider;
        public string modelId;
        public List<string> capabilities = new List<string>();

        public LlmCapabilities Capabilities
        {
            get
            {
                LlmCapabilities result = LlmCapabilities.None;
                if (capabilities != null)
                {
                    foreach (var c in capabilities)
                    {
                        if (Enum.TryParse<LlmCapabilities>(c, true, out var cap))
                        {
                            result |= cap;
                        }
                    }
                }
                return result;
            }
        }
    }
}
