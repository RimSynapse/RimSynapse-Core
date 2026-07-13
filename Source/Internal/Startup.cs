using Verse;

namespace RimSynapse.Internal
{
    [StaticConstructorOnStartup]
    internal static class Startup
    {
        static Startup()
        {
            RimSynapse.SynapseLogger.InitMainThread();
        }
    }
}
