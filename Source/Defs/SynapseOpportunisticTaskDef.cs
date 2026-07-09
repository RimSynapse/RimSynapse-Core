namespace RimSynapse
{
    /// <summary>
    /// XML-definable opportunistic task. Companion mods ship these in
    /// Defs/OpportunisticTasks/*.xml so users can tune task frequency,
    /// priority, and weight without touching C# code.
    ///
    /// Priority scale:
    ///   10 = Critical background task (e.g., clinical evaluation fallback)
    ///    5 = Standard (e.g., memory generation from events)
    ///    2 = Low (e.g., visitor backstory generation)
    ///    0 = Disabled
    ///
    /// Weight is used for weighted random selection among eligible tasks.
    /// After a task fires, its effective weight decays and recovers over
    /// its cooldown period, preventing any single task from monopolizing idle time.
    /// </summary>
    public class SynapseOpportunisticTaskDef : Verse.Def
    {
        /// <summary>
        /// Higher priority tasks are checked first. Tasks with equal priority
        /// are selected by weighted random using <see cref="weight"/>.
        /// </summary>
        public int priority = 5;

        /// <summary>
        /// Base weight for weighted random selection among tasks at the same priority.
        /// After firing, effective weight decays to 0 and recovers linearly over the cooldown.
        /// </summary>
        public float weight = 1.0f;

        /// <summary>
        /// Minimum in-game ticks between invocations.
        /// 60000 ticks ≈ 1 in-game day. Default 15000 ≈ 6 hours.
        /// </summary>
        public int cooldownTicks = 15000;

        /// <summary>
        /// Whether this task is currently enabled. Can be toggled in-game.
        /// </summary>
        public bool enabled = true;
    }
}
