using System;

namespace RimSynapse
{
    [Flags]
    public enum LlmCapabilities
    {
        None = 0,
        Text = 1,
        Image = 2,
        Vision = 4,
        Audio = 8,
        All = Text | Image | Vision | Audio
    }
}
