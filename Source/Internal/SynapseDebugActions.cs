using LudeonTK;
using RimSynapse.UI;
using Verse;

namespace RimSynapse.Internal
{
    public static class SynapseDebugActions
    {
        [DebugAction("RimSynapse", "View Session Logs", actionType = DebugActionType.Action)]
        public static void ViewSessionLogs()
        {
            Find.WindowStack.Add(new Dialog_SynapseLogs());
        }
    }
}
