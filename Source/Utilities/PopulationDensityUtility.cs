using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimSynapse.Utilities
{
    public static class PopulationDensityUtility
    {
        private struct QueueEntry
        {
            public PlanetTile tile;
            public float multiplier;

            public QueueEntry(PlanetTile tile, float multiplier)
            {
                this.tile = tile;
                this.multiplier = multiplier;
            }
        }

        public static int GetPopulationAtTile(int targetTile)
        {
            if (Find.World == null || Find.WorldGrid == null) return 0;

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null || !settlements.Any()) return 0;

            // Retrieve the start tile's PlanetTile reference safely via graph neighbors lookup
            PlanetTile startPlanetTile = PlanetTile.Invalid;
            var tempNeighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(targetTile, tempNeighbors);
            if (tempNeighbors.Any())
            {
                var doubleNeighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(tempNeighbors[0].tileId, doubleNeighbors);
                foreach (var t in doubleNeighbors)
                {
                    if (t.tileId == targetTile)
                    {
                        startPlanetTile = t;
                        break;
                    }
                }
            }

            if (startPlanetTile == PlanetTile.Invalid) return 0;

            float totalPop = 0f;
            var visited = new HashSet<int>();
            var queue = new Queue<QueueEntry>();

            queue.Enqueue(new QueueEntry(startPlanetTile, 1.0f));
            visited.Add(targetTile);

            var settlementList = new List<Settlement>(settlements);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                PlanetTile currentTile = current.tile;
                int currentTileId = currentTile.tileId;
                float currentMultiplier = current.multiplier;

                // Stop traversing if multiplier is practically zero to save performance
                if (currentMultiplier < 0.001f) continue;

                // Check if there is a settlement at this tile
                var settlement = settlementList.FirstOrDefault(s => s.Tile == currentTileId);
                if (settlement != null)
                {
                    int settlementPop = GetSettlementPopulation(settlement);
                    totalPop += (settlementPop * currentMultiplier);
                }

                // Traverse to neighbors
                var neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(currentTileId, neighbors);
                foreach (var neighbor in neighbors)
                {
                    int neighborId = neighbor.tileId;
                    if (!visited.Contains(neighborId))
                    {
                        visited.Add(neighborId);

                        float stepMultiplier = GetStepMultiplier(currentTile, neighbor);
                        if (stepMultiplier > 0f)
                        {
                            queue.Enqueue(new QueueEntry(neighbor, currentMultiplier * stepMultiplier));
                        }
                    }
                }
            }

            return UnityEngine.Mathf.RoundToInt(totalPop);
        }

        public static int GetSettlementPopulation(Settlement settlement)
        {
            if (settlement == null) return 0;

            if (settlement.Faction != null && settlement.Faction.IsPlayer)
            {
                var map = Find.Maps?.FirstOrDefault(m => m.Tile == settlement.Tile);
                if (map != null)
                {
                    return map.mapPawns?.FreeColonistsCount ?? 0;
                }
                return 0;
            }

            int basePop = 50;
            if (settlement.Faction != null)
            {
                var tech = settlement.Faction.def?.techLevel ?? TechLevel.Industrial;
                if (tech == TechLevel.Neolithic) basePop = 60;
                else if (tech == TechLevel.Industrial) basePop = 90;
                else if (tech >= TechLevel.Spacer) basePop = 150;
            }

            System.Random random = new System.Random(settlement.Tile);
            return basePop + random.Next(-10, 20);
        }

        private static float GetStepMultiplier(PlanetTile fromTile, PlanetTile toTile)
        {
            if (Find.WorldGrid == null) return 0f;

            Tile tileData = Find.WorldGrid[toTile.tileId];
            if (tileData == null) return 0f;

            // 1. Impassable mountains and water do not transfer any population
            if (tileData.hilliness == Hilliness.Impassable || 
                tileData.WaterCovered || 
                (tileData.PrimaryBiome != null && tileData.PrimaryBiome.impassable))
            {
                return 0f;
            }

            float factor = 1f;
            bool hasTerrainFeature = false;

            // Large Hills factor: 4
            if (tileData.hilliness == Hilliness.LargeHills)
            {
                factor *= 4f;
                hasTerrainFeature = true;
            }
            // Mountainous factor: 8
            else if (tileData.hilliness == Hilliness.Mountainous)
            {
                factor *= 8f;
                hasTerrainFeature = true;
            }

            // Swamp/Marsh factor: 8
            bool isSwampOrMarsh = tileData.swampiness > 0.1f || 
                (tileData.PrimaryBiome != null && 
                 (tileData.PrimaryBiome.defName.Contains("Swamp") || 
                  tileData.PrimaryBiome.defName.Contains("Marsh")));
            if (isSwampOrMarsh)
            {
                factor *= 8f;
                hasTerrainFeature = true;
            }

            // Default flat/small hills factor: 2
            if (!hasTerrainFeature)
            {
                factor = 2f;
            }

            // Along a road: factor degrades at 75% of previous amount (multiplier 0.75f vs 0.50f), which is a 2/3 factor multiplier
            RoadDef road = Find.WorldGrid.GetRoadDef(fromTile, toTile);
            if (road != null)
            {
                factor *= (2f / 3f);
            }

            // Next to water: factor degrades at 75% of previous amount (multiplier 0.75f vs 0.50f), which is a 2/3 factor multiplier
            if (IsNextToWater(toTile.tileId))
            {
                factor *= (2f / 3f);
            }

            float stepMultiplier = 1f / factor;

            // Cap stepMultiplier at 0.75f to prevent population increases/explosions
            if (stepMultiplier > 0.75f)
            {
                stepMultiplier = 0.75f;
            }

            return stepMultiplier;
        }

        private static bool IsNextToWater(int tileId)
        {
            Tile tile = Find.WorldGrid[tileId];
            if (tile == null) return false;
            if (tile.IsCoastal || tile.WaterCovered) return true;

            var neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(tileId, neighbors);
            foreach (var n in neighbors)
            {
                var nt = Find.WorldGrid[n.tileId];
                if (nt != null && nt.WaterCovered) return true;
            }
            return false;
        }
    }
}
