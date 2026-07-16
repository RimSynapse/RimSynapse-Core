using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RimSynapse
{
    /// <summary>
    /// Search result model for the find_items_on_map tool.
    /// </summary>
    public class MapItemSearchResult
    {
        public string label;
        public string defName;
        public int x;
        public int z;
        public int distance;
        public int stackCount;
    }
}
