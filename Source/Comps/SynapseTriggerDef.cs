using Verse;

namespace RimSynapse
{
    public class SynapseTriggerDef : Def
    {
        public string eventName;      // e.g. "PawnInjured", "PawnMentalBreak", "PawnDeath"
        public string condition;      // e.g. "The pawn has a noble title", "The pawn is bleeding heavily" (optional)
        public string instruction;    // Plain English instruction, e.g. "Heal their legs and lock their bedroom door"
    }
}
