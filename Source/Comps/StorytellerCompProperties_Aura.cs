using RimWorld;

namespace RimSynapse.Comps
{
    public class StorytellerCompProperties_Aura : StorytellerCompProperties
    {
        public float incidentsTargetDays = 10f;
        public float threatsTargetDays = 10f;

        public string pacingSystemPrompt;
        public string selectionSystemPrompt;

        public string metricsTemplate = @"Colony General and Resource Metrics:
- Overall Wealth: {wealth} (Items, buildings, and pawns)
- Available Silver: {silver} (Stored or mined on map)
- Food Reserves: {food} nutrition points
- Growing Season: {growingSeason} of the year growable (Winter Resource Burden Multiplier: {winterBurden}x)
- Greenhouse Capacity: {greenhouse}
- Population: {population} colonists
- Livestock: {livestock}
- Legendary Art: {legendaryArt} pieces (Total Value: {legendaryArtValue} silver)
- Combat Capability: {combat}
- Medical Status: {medical}
- Average Mood: {mood}";

        public string livestockTemplate = "{tameCount} tamed animals (Total Value: {tameWealth} silver)\n  - Detail: {animalReport}";

        public string greenhouseTemplate = "{greenhouseCells} active growable cells at midday (Hydroponics: {activeHydroponics} active basins, Sun Lamps: {activeSunLamps} powered, Skylights/Solar Roofs: {activeSkylightsCount}, Trend: {trend})";

        public StorytellerCompProperties_Aura()
        {
            this.compClass = typeof(StorytellerComp_Aura);
        }
    }
}
