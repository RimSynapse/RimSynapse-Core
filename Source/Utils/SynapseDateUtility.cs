using System;
using Verse;
using RimWorld;

namespace RimSynapse.Utils
{
    /// <summary>
    /// A custom date calculator for generating log entries and faction histories
    /// that bypasses vanilla RimWorld's absolute date checks and limitations,
    /// allowing for histories spanning thousands of years safely.
    /// </summary>
    public static class SynapseDateUtility
    {
        public const int TicksPerDay = 60000;
        public const int TicksPerYear = TicksPerDay * 60;

        /// <summary>
        /// Converts an age in years into an absolute string, gracefully handling
        /// dates that occur before the game's start year (e.g., negative years).
        /// </summary>
        public static string GetAbsoluteDateStringFromYearsAgo(int yearsAgo)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                int startingYear = 5500;
                return $"Year {startingYear - yearsAgo}";
            }

            int currentYear = GenDate.Year(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(0).x);
            int absoluteYear = currentYear - yearsAgo;
            
            // Format to avoid weird vanilla negative checks
            if (absoluteYear < 0)
            {
                return $"{Math.Abs(absoluteYear)} B.C.E. (Before Rim)";
            }
            return $"Year {absoluteYear}";
        }

        /// <summary>
        /// Returns a formatted string representing an absolute tick value, 
        /// even if the tick value represents a time long before the game started.
        /// </summary>
        public static string GetSafeAbsoluteDateString(long ticksAbs)
        {
            if (ticksAbs >= 0)
            {
                // Safe for vanilla
                return GenDate.DateFullStringAt(ticksAbs, Find.WorldGrid?.LongLatOf(0) ?? default(UnityEngine.Vector2));
            }
            else
            {
                // Negative ticks (before game epoch)
                long ticksAgo = Math.Abs(ticksAbs);
                int yearsAgo = (int)(ticksAgo / TicksPerYear);
                return GetAbsoluteDateStringFromYearsAgo(yearsAgo);
            }
        }

        /// <summary>
        /// Calculates an absolute tick value for a specific number of years in the past.
        /// </summary>
        public static long TicksAbsFromYearsAgo(int yearsAgo)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return -(yearsAgo * (long)TicksPerYear);
            }
            return Find.TickManager.TicksAbs - (yearsAgo * (long)TicksPerYear);
        }
    }
}
