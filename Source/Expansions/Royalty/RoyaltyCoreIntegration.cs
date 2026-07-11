using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using RimSynapse.Internal;
using RimSynapse.Models;

namespace RimSynapse.Expansions.Royalty
{
    [StaticConstructorOnStartup]
    public static class RoyaltyCoreIntegration
    {
        static RoyaltyCoreIntegration()
        {
            if (ModsConfig.RoyaltyActive)
            {
                Register();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Register()
        {
            ContextAssembler.OnAssemblePawnPacket += AssemblePawnPacket;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AssemblePawnPacket(Pawn pawn, PawnPacket packet, List<ContextAssembler.ContextSlot> slots)
        {
            if (pawn.royalty != null)
            {
                var title = pawn.royalty.MostSeniorTitle;
                if (title != null)
                {
                    packet.royaltyTitle = title.def.label;
                }
            }
        }
    }
}
