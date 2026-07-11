using Verse;
using RimWorld;

namespace RimSynapse.Models
{
    /// <summary>
    /// Explicitly tracks date and time without relying on absolute ticks.
    /// This fixes the bug where negative absolute ticks (pre-5500) fail to parse properly when reloading saves.
    /// </summary>
    public struct SynapseDate : IExposable
    {
        public int year;
        public int quadrum; // 0-3 (Aprimay, Jugust, Septober, Decembary)
        public int day;     // 0-14
        public int hour;    // 0-23

        public SynapseDate(int year, int quadrum, int day, int hour)
        {
            this.year = year;
            this.quadrum = quadrum;
            this.day = day;
            this.hour = hour;
        }

        /// <summary>
        /// Captures the current date and time on the given map.
        /// If map is null, defaults to the world (longitude 0).
        /// </summary>
        public static SynapseDate Now(Map map = null)
        {
            float longitude = 0f;
            if (map != null)
            {
                longitude = Find.WorldGrid.LongLatOf(map.Tile).x;
            }
            else if (Find.AnyPlayerHomeMap != null)
            {
                longitude = Find.WorldGrid.LongLatOf(Find.AnyPlayerHomeMap.Tile).x;
            }

            long ticksAbs = GenTicks.TicksAbs;
            int y = GenDate.Year(ticksAbs, longitude);
            int q = (int)GenDate.Quadrum(ticksAbs, longitude);
            int d = GenDate.DayOfQuadrum(ticksAbs, longitude);
            int h = GenDate.HourOfDay(ticksAbs, longitude);

            return new SynapseDate(y, q, d, h);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref year, "year", 5500);
            Scribe_Values.Look(ref quadrum, "quadrum", 0);
            Scribe_Values.Look(ref day, "day", 0);
            Scribe_Values.Look(ref hour, "hour", 0);
        }

        public override string ToString()
        {
            string quadrumName = "Quadrum " + quadrum;
            switch (quadrum)
            {
                case 0: quadrumName = "Aprimay"; break;
                case 1: quadrumName = "Jugust"; break;
                case 2: quadrumName = "Septober"; break;
                case 3: quadrumName = "Decembary"; break;
            }
            return $"{day + 1} of {quadrumName}, {year} at {hour}h";
        }
    }
}
