using System.Collections.Generic;
using Verse;

namespace RimSynapse.Models
{
    public enum ShortTermEventType
    {
        Generic,
        PositiveInteraction,
        NegativeInteraction,
        DeepTalk,
        GlobalEvent
    }

    /// <summary>
    /// Represents an event or interaction that occurred recently.
    /// Kept in a rolling 48-hour window to provide short-term context to the LLM.
    /// </summary>
    public class ShortTermEvent : IExposable
    {
        public int gameTick; // Used for calculating 48-hour expiration internally
        public SynapseDate date;
        public ShortTermEventType eventType;
        public string description;
        public List<string> involvedPawnIds = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref gameTick, "gameTick");
            Scribe_Deep.Look(ref date, "date");
            Scribe_Values.Look(ref eventType, "eventType", ShortTermEventType.Generic);
            Scribe_Values.Look(ref description, "description");
            Scribe_Collections.Look(ref involvedPawnIds, "involvedPawnIds", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (involvedPawnIds == null) involvedPawnIds = new List<string>();
            }
        }
    }
}
