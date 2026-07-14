namespace RimSynapse
{
    /// <summary>
    /// Represents the resolution outcome category of a tracked game or world event.
    /// </summary>
    public enum EventOutcome
    {
        /// <summary>Outcome is not yet determined or is irrelevant.</summary>
        Unknown,
        
        /// <summary>The event was successfully adjudicated or completed (e.g. helping beggars, completing quests).</summary>
        Success,
        
        /// <summary>The event failed to complete or was unsuccessful (e.g. failed quest, failed peace talks).</summary>
        Failed,
        
        /// <summary>The event expired or was ignored by the player (e.g. beggars ignored, quest expired).</summary>
        Ignored,
        
        /// <summary>The event resolved in tragedy (e.g. colonist deaths, major base damage).</summary>
        Tragedy,
        
        /// <summary>The event resolved in complete triumph (e.g. raid repelled cleanly, major threat neutralized).</summary>
        Triumph,
        
        /// <summary>The event resulted in open conflict or hostilites (e.g. attacking guests, declaring war).</summary>
        Conflict
    }
}
