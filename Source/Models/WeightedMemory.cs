using System.Collections.Generic;
using Verse;

namespace RimSynapse.Models
{
    public class WeightedMemory : IExposable
    {
        public string summary;
        public string memoryType;        // raid, social, event, trade, quest, backstory, etc.
        public List<string> tags = new List<string>();
        public List<string> subjectPawnIds = new List<string>();
        public bool isLongTerm = false;
        
        /// <summary>Absolute tick when this memory occurred. Used for date display and chronological sorting.</summary>
        public long absTick;
        
        /// <summary>DEPRECATED — kept for save compatibility. New code should use absTick.</summary>
        public int gameTick;
        
        public float weight = 1.0f;             // 0.0 to 1.0
        public float baseWeight = 1.0f;
        public float decayRate = 0.05f;          // default 0.05
        public int timesReferenced;

        public WeightedMemory()
        {
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref summary, "summary");
            Scribe_Values.Look(ref memoryType, "memoryType");
            Scribe_Collections.Look(ref tags, "tags", LookMode.Value);
            Scribe_Values.Look(ref gameTick, "gameTick");
            Scribe_Values.Look(ref absTick, "absTick", 0L);
            Scribe_Values.Look(ref weight, "weight", 1.0f);
            Scribe_Values.Look(ref baseWeight, "baseWeight", 1.0f);
            Scribe_Values.Look(ref decayRate, "decayRate", 0.05f);
            Scribe_Values.Look(ref timesReferenced, "timesReferenced", 0);
            Scribe_Values.Look(ref isLongTerm, "isLongTerm", false);
            Scribe_Collections.Look(ref subjectPawnIds, "subjectPawnIds", LookMode.Value);
            
            // Ensure lists are initialized after loading
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (tags == null) tags = new List<string>();
                if (subjectPawnIds == null) subjectPawnIds = new List<string>();
            }
        }

        /// <summary>
        /// Called after all game data is loaded. Migrates old gameTick-only memories
        /// to use absTick by applying the adjustment offset.
        /// Should be called from the owning comp's PostLoadInit or equivalent.
        /// </summary>
        public void MigrateTickIfNeeded()
        {
            if (absTick == 0L && gameTick != 0)
            {
                // Old save: absTick was never written. Convert gameTick to absolute.
                absTick = Utils.SynapseDateHelper.GameTickToAbsTick(gameTick);
            }
        }
    }
}

