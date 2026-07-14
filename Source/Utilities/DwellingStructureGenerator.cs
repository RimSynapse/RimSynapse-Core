using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimSynapse.Utilities
{
    public static class DwellingStructureGenerator
    {
        public static void Generate(Map map)
        {
            if (map == null) return;

            IntVec3 startSpot = FindValidSpot(map);
            if (startSpot == IntVec3.Invalid) return;

            // baseLoc is the bottom-left corner of the homestead area
            IntVec3 baseLoc = startSpot - new IntVec3(8, 0, 8);

            // Fetch defs safely
            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef doorDef = ThingDefOf.Door;
            ThingDef woodLog = ThingDefOf.WoodLog;
            ThingDef campfireDef = ThingDefOf.Campfire;
            
            ThingDef fenceDef = ThingDef.Named("Fence");
            ThingDef fenceGateDef = ThingDef.Named("FenceGate");
            ThingDef penMarkerDef = ThingDef.Named("PenMarker");

            TerrainDef woodFloor = TerrainDef.Named("WoodPlankFloor");
            TerrainDef soilTerrain = TerrainDefOf.Soil;

            // 1. Generate 4x4 interior wood roofed building (6x6 footprint with walls)
            // Walkable interior: x in [baseLoc.x + 1, baseLoc.x + 4], z in [baseLoc.z + 1, baseLoc.z + 4]
            for (int x = baseLoc.x; x <= baseLoc.x + 5; x++)
            {
                for (int z = baseLoc.z; z <= baseLoc.z + 5; z++)
                {
                    IntVec3 c = new IntVec3(x, 0, z);
                    if (!c.InBounds(map)) continue;

                    // Clear vegetation/rubble
                    ClearBlockers(c, map);

                    // Place walls on borders
                    bool isBorderX = (x == baseLoc.x || x == baseLoc.x + 5);
                    bool isBorderZ = (z == baseLoc.z || z == baseLoc.z + 5);

                    if (isBorderX || isBorderZ)
                    {
                        // Leave room for door at the top border (x = baseLoc.x + 3, z = baseLoc.z + 5)
                        if (x == baseLoc.x + 3 && z == baseLoc.z + 5)
                        {
                            SpawnThing(doorDef, woodLog, c, map);
                        }
                        else
                        {
                            SpawnThing(wallDef, woodLog, c, map);
                        }
                    }
                    else
                    {
                        // Interior floors
                        map.terrainGrid.SetTerrain(c, woodFloor);
                    }

                    // Add roof to the building
                    map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                }
            }

            // 2. Campfire outside (near the door)
            IntVec3 campfireLoc = new IntVec3(baseLoc.x + 3, 0, baseLoc.z + 8);
            ClearBlockers(campfireLoc, map);
            SpawnThing(campfireDef, null, campfireLoc, map);

            // Determine whether to spawn a pen OR a field (50/50 chance, seeded by location)
            System.Random random = new System.Random(baseLoc.x ^ baseLoc.z);
            bool spawnPen = random.NextDouble() < 0.5;

            if (spawnPen)
            {
                // 3. An empty 8x8 penned in area (fence)
                // Footprint: x in [baseLoc.x, baseLoc.x + 7], z in [baseLoc.z + 11, baseLoc.z + 18]
                for (int x = baseLoc.x; x <= baseLoc.x + 7; x++)
                {
                    for (int z = baseLoc.z + 11; z <= baseLoc.z + 18; z++)
                    {
                        IntVec3 c = new IntVec3(x, 0, z);
                        if (!c.InBounds(map)) continue;

                        ClearBlockers(c, map);

                        bool isBorderX = (x == baseLoc.x || x == baseLoc.x + 7);
                        bool isBorderZ = (z == baseLoc.z + 11 || z == baseLoc.z + 18);

                        if (isBorderX || isBorderZ)
                        {
                            // Place fence gate at bottom border (x = baseLoc.x + 4, z = baseLoc.z + 11)
                            if (x == baseLoc.x + 4 && z == baseLoc.z + 11)
                            {
                                SpawnThing(fenceGateDef, woodLog, c, map);
                            }
                            else
                            {
                                SpawnThing(fenceDef, woodLog, c, map);
                            }
                        }
                    }
                }

                // Spawn a Pen Marker inside the fenced area
                IntVec3 markerLoc = new IntVec3(baseLoc.x + 3, 0, baseLoc.z + 15);
                ClearBlockers(markerLoc, map);
                SpawnThing(penMarkerDef, woodLog, markerLoc, map);
            }
            else
            {
                // 4. An 8x8 crop field (registered as a growing zone)
                Zone_Growing zone = new Zone_Growing(map.zoneManager);
                map.zoneManager.RegisterZone(zone);
                for (int x = baseLoc.x + 7; x <= baseLoc.x + 14; x++)
                {
                    for (int z = baseLoc.z; z <= baseLoc.z + 7; z++)
                    {
                        IntVec3 c = new IntVec3(x, 0, z);
                        if (!c.InBounds(map)) continue;

                        ClearBlockers(c, map);
                        map.terrainGrid.SetTerrain(c, soilTerrain);
                        zone.AddCell(c);
                    }
                }
                zone.SetPlantDefToGrow(ThingDefOf.Plant_Potato);
            }
        }

        private static IntVec3 FindValidSpot(Map map)
        {
            IntVec3 center = map.Center;
            
            // Look for a flat, building-friendly starting area
            for (int i = 0; i < 1500; i++)
            {
                IntVec3 cell = CellFinder.RandomCell(map);
                if (cell.Walkable(map) && 
                    !cell.Fogged(map) && 
                    cell.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy) && 
                    cell.x > 22 && cell.x < map.Size.x - 22 && cell.z > 22 && cell.z < map.Size.z - 22)
                {
                    // Check if 20x20 area around it is clear of rock walls and water
                    bool clear = true;
                    for (int x = -10; x <= 10; x++)
                    {
                        for (int z = -10; z <= 10; z++)
                        {
                            IntVec3 c = cell + new IntVec3(x, 0, z);
                            if (!c.InBounds(map))
                            {
                                clear = false;
                                break;
                            }
                            var terrain = c.GetTerrain(map);
                            if (terrain.IsWater || c.GetEdifice(map) is Building b && b.def.building.isNaturalRock)
                            {
                                clear = false;
                                break;
                            }
                        }
                        if (!clear) break;
                    }
                    if (clear) return cell;
                }
            }

            return map.Center;
        }

        private static void ClearBlockers(IntVec3 cell, Map map)
        {
            var blockers = cell.GetThingList(map).ToList();
            foreach (var b in blockers)
            {
                if (b.def.destroyable && b.def.category != ThingCategory.Pawn)
                {
                    b.Destroy();
                }
            }
        }

        private static void SpawnThing(ThingDef def, ThingDef stuff, IntVec3 cell, Map map)
        {
            if (def == null) return;
            Thing thing = ThingMaker.MakeThing(def, stuff);
            GenSpawn.Spawn(thing, cell, map);
        }
    }
}
