using RimWorld.Planet;
using Verse;

namespace RimSynapse
{
    /// <summary>
    /// Minimal WorldComponent for Core's context embedding system.
    /// Persists: schema version + optional cached context JSON.
    ///
    /// All heavy persistence (memories, threads, faction trackers) lives
    /// in companion mod components. Core's save footprint is intentionally
    /// minimal.
    ///
    /// Safe for mod addition and removal:
    /// - Add to existing save: created with defaults, no errors.
    /// - Remove from save: RimWorld logs a warning, skips it, loads fine.
    /// </summary>
    public class SynapseContextWorldComponent : WorldComponent
    {
        /// <summary>
        /// Schema version for forward compatibility.
        /// Increment when the serialized format changes.
        /// </summary>
        private int contextVersion = 1;

        /// <summary>
        /// Optional: last assembled context as JSON string.
        /// Only populated if context persistence is enabled (debug feature).
        /// Useful for DevTools inspection and debugging.
        /// </summary>
        private string lastContextJson;

        public SynapseContextWorldComponent(World world) : base(world) { }

        /// <summary>Get the last cached context JSON (null if not persisting).</summary>
        public string LastContextJson => lastContextJson;

        /// <summary>Get the schema version.</summary>
        public int ContextVersion => contextVersion;

        /// <summary>
        /// Store a context snapshot for debugging/persistence.
        /// Only called when persistence is enabled in settings.
        /// </summary>
        internal void CacheContext(string contextJson)
        {
            lastContextJson = contextJson;
        }

        /// <summary>
        /// Clear the cached context.
        /// </summary>
        internal void ClearCache()
        {
            lastContextJson = null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref contextVersion, "contextVersion", 1);
            Scribe_Values.Look(ref lastContextJson, "lastContextJson");
        }
    }
}
