using System;
using System.Collections.Generic;

namespace RimSynapse
{
    /// <summary>
    /// GPU statistics. Populated by an external GPU monitor mod via
    /// <see cref="SynapseClient.Gpu"/>. RimSynapse Core provides the
    /// framework — a separate mod does the actual polling.
    /// </summary>
    public class GpuStats
    {
        /// <summary>Whether GPU monitoring is supported and active.</summary>
        public bool supported;

        /// <summary>GPU core utilization (0-100%).</summary>
        public int utilizationPercent;

        /// <summary>Currently used VRAM in GB.</summary>
        public float usedVramGb;

        /// <summary>Total VRAM capacity in GB.</summary>
        public float totalVramGb;

        /// <summary>Optional per-process VRAM breakdown.</summary>
        public List<GpuProcess> processes = new List<GpuProcess>();

        /// <summary>When the stats were last updated.</summary>
        public DateTime lastUpdated;

        /// <summary>VRAM usage as a percentage (0.0 - 1.0).</summary>
        public float VramUsagePercent =>
            totalVramGb > 0f ? usedVramGb / totalVramGb : 0f;
    }

    /// <summary>
    /// A single process consuming GPU VRAM.
    /// </summary>
    public class GpuProcess
    {
        /// <summary>Process ID.</summary>
        public int pid;

        /// <summary>Process name (e.g., "RimWorld", "LM Studio").</summary>
        public string name;

        /// <summary>VRAM usage in MB.</summary>
        public float vramMb;
    }
}
