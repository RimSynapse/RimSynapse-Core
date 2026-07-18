using System;
using System.Text;
using System.Linq;
using RimWorld;
using Verse;

namespace RimSynapse
{
    public static class SynapseLetterContextHook
    {
        public delegate void GetLetterContextDelegate(Letter letter, Pawn asker, StringBuilder contextBuilder);
        
        /// <summary>
        /// Hook subscribed to by storyteller comps or companion mods (like Factions or Royalty)
        /// to append specific narrative context (e.g., faction despair, nobility hierarchy).
        /// </summary>
        public static event GetLetterContextDelegate OnGatherLetterContext;

        static SynapseLetterContextHook()
        {
            // Subscribe default core nobility context handlers
            OnGatherLetterContext += GatherDefaultNobilityContext;
        }

        public static string GetAdditionalContext(Letter letter, Pawn asker)
        {
            var sb = new StringBuilder();
            if (OnGatherLetterContext != null)
            {
                try
                {
                    OnGatherLetterContext(letter, asker, sb);
                }
                catch (Exception ex)
                {
                    RimSynapse.SynapseLogger.Error($"Error in OnGatherLetterContext hook: {ex.Message}");
                }
            }
            return sb.ToString().Trim();
        }

        private static void GatherDefaultNobilityContext(Letter letter, Pawn asker, StringBuilder contextBuilder)
        {
            if (asker == null) return;

            // Check if royalty system is active and asker has titles
            if (asker.royalty != null && asker.royalty.MostSeniorTitle != null)
            {
                // Find the player's highest ranking noble colonist to compare ranks
                Pawn playerLeader = Find.CurrentMap?.mapPawns?.FreeColonistsSpawned?
                    .Where(p => !p.Downed && !p.Dead && p.royalty != null && p.royalty.MostSeniorTitle != null)
                    .OrderByDescending(p => p.royalty.MostSeniorTitle.def.seniority)
                    .FirstOrDefault();

                if (playerLeader != null && playerLeader.royalty != null && playerLeader.royalty.MostSeniorTitle != null)
                {
                    int askerSeniority = asker.royalty.MostSeniorTitle.def.seniority;
                    int playerSeniority = playerLeader.royalty.MostSeniorTitle.def.seniority;

                    if (askerSeniority < playerSeniority)
                    {
                        contextBuilder.AppendLine($"- Nobility Rank Context: You ({asker.Name.ToStringShort}) hold a lower nobility rank ({asker.royalty.MostSeniorTitle.def.LabelCap}) than the player's colony leader {playerLeader.Name.ToStringShort} ({playerLeader.royalty.MostSeniorTitle.def.LabelCap}). Write this request with deep respect, politeness, and deference to their superior noble standing.");
                    }
                    else if (askerSeniority > playerSeniority)
                    {
                        contextBuilder.AppendLine($"- Nobility Rank Context: You ({asker.Name.ToStringShort}) hold a higher nobility rank ({asker.royalty.MostSeniorTitle.def.LabelCap}) than the player's colony leader {playerLeader.Name.ToStringShort} ({playerLeader.royalty.MostSeniorTitle.def.LabelCap}). Speak with noble authority, dignity, and expected protocol, though remain professional.");
                    }
                }
            }
        }
    }
}
