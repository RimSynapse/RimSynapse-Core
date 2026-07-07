using System.Collections.Generic;

namespace RimSynapse
{
    /// <summary>
    /// Result from querying LM Studio's loaded models.
    /// </summary>
    public class ModelsResult
    {
        /// <summary>Whether LM Studio is reachable.</summary>
        public bool online;

        /// <summary>List of loaded model IDs.</summary>
        public List<string> modelIds = new List<string>();

        /// <summary>Error message if LM Studio is offline.</summary>
        public string error;

        /// <summary>Active model's context window size (if available from LM Studio native API).</summary>
        public int? contextLength;
    }
}
