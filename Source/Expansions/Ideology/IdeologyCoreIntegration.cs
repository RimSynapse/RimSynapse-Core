using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using RimSynapse.Internal;
using RimSynapse.Models;

namespace RimSynapse.Expansions.Ideology
{
    [StaticConstructorOnStartup]
    public static class IdeologyCoreIntegration
    {
        static IdeologyCoreIntegration()
        {
            if (ModsConfig.IdeologyActive)
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
            if (!RimSynapseMod.Instance.Settings.testIdeologyActive) return;

            if (pawn.Ideo != null)
            {
                packet.ideology = pawn.Ideo.name;
                packet.precepts = pawn.Ideo.PreceptsListForReading?
                    .Select(p => p.Label).ToList();
                    
                slots.Add(ContextAssembler.MakeSlot("ideology", $"[Ideology] {packet.ideology}"));
            }
        }
    }
}
