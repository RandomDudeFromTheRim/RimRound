using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimRound.GenSteps
{
    /// <summary>
    /// Fills an entire pocket map with flesh, then carves cramped, winding
    /// caverns through it — undercave style, not a grid maze. Border and map
    /// edge are immovable hardened flesh; tunnels are mostly 1-2 cells wide
    /// with occasional small chambers. Dead pockets hide mineable nodes and
    /// void gluttonium.
    /// </summary>
    public class GenStep_RRVoidMaze : GenStep
    {
        public override int SeedPart => 20415307;

        public override void Generate(Map map, GenStepParams parms)
        {
            TerrainDef floor = TerrainDef.Named("RR_FleshFloor");
            foreach (IntVec3 cell in map.AllCells)
                map.terrainGrid.SetTerrain(cell, floor);

            int sideX = map.Size.x, sideZ = map.Size.z;
            IntVec3 center = map.Center;
            var carved = new HashSet<IntVec3>();

            void CarveRadius(IntVec3 c, int r)
            {
                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx * dx + dz * dz > r * r + 1)
                        continue;
                    var cell = new IntVec3(c.x + dx, 0, c.z + dz);
                    if (cell.InBounds(map))
                        carved.Add(cell);
                }
            }

            // the portal chamber
            CarveRadius(center, 4);

            // winding walkers: start from carved ground and tunnel outward
            var carvedList = new List<IntVec3> { center };
            var dirs = new[] { new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0), new IntVec3(0, 0, 1), new IntVec3(0, 0, -1) };
            for (int tunnel = 0; tunnel < 16; tunnel++)
            {
                IntVec3 pos = carvedList[Rand.Range(0, carvedList.Count)];
                IntVec3 dir = dirs[Rand.Range(0, 4)];
                int length = Rand.RangeInclusive(180, 420);
                int squeezeLeft = 0, chamberCooldown = 0;

                for (int step = 0; step < length; step++)
                {
                    // momentum: mostly keep direction, sometimes swerve hard
                    if (Rand.Value < 0.25f)
                        dir = dirs[Rand.Range(0, 4)];

                    pos += dir;
                    if (!pos.InHorDistOf(center, sideX * 0.48f))
                        break; // stay off the hardened shell

                    // width rhythm: squeeze to 1-wide, breathe back to 2
                    if (squeezeLeft > 0)
                    {
                        squeezeLeft--;
                    }
                    else if (Rand.Value < 0.18f)
                    {
                        squeezeLeft = Rand.RangeInclusive(12, 30);
                    }

                    if (chamberCooldown > 0)
                        chamberCooldown--;

                    if (chamberCooldown == 0 && Rand.Value < 0.03f)
                    {
                        CarveRadius(pos, Rand.RangeInclusive(2, 3)); // small gullet chamber
                        chamberCooldown = 60;
                    }
                    else
                    {
                        CarveRadius(pos, squeezeLeft > 0 ? 0 : 1);
                    }

                    if (step % 5 == 0)
                        carvedList.Add(pos);
                }
            }

            // second pass: short feeder crawls off existing tunnels for dead ends
            for (int i = 0; i < 60; i++)
            {
                IntVec3 pos = carvedList[Rand.Range(0, carvedList.Count)];
                IntVec3 dir = dirs[Rand.Range(0, 4)];
                for (int step = 0; step < Rand.RangeInclusive(10, 40); step++)
                {
                    if (Rand.Value < 0.35f)
                        dir = dirs[Rand.Range(0, 4)];
                    pos += dir;
                    if (!pos.InHorDistOf(center, sideX * 0.48f))
                        break;
                    CarveRadius(pos, Rand.Value < 0.75f ? 0 : 1); // mostly 1-wide
                    carvedList.Add(pos);
                }
            }

            ThingDef hardWall = ThingDef.Named("RR_HardenedFleshWall");
            ThingDef blobWall = ThingDef.Named("RR_BlobWall");
            ThingDef fleshWall = ModsConfig.AnomalyActive
                ? ThingDef.Named("Fleshmass_Active")
                : ThingDef.Named("RR_FleshWall");

            // paint: hardened shell near the edge, flesh mass elsewhere, carve stays open
            foreach (IntVec3 cell in map.AllCells)
            {
                if (carved.Contains(cell))
                    continue;

                int edgeDist = System.Math.Min(
                    System.Math.Min(cell.x, sideX - 1 - cell.x),
                    System.Math.Min(cell.z, sideZ - 1 - cell.z));

                ThingDef def = edgeDist <= 3
                    ? hardWall
                    : (Rand.Value < 0.62f ? blobWall : fleshWall);

                GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map);
            }

            // rewards in the dead pockets: mineables and gluttonium
            int placed = 0;
            for (int i = 0; i < 400 && placed < 45; i++)
            {
                IntVec3 spot = carvedList[Rand.Range(0, carvedList.Count)];
                if (spot.DistanceTo(center) < 12)
                    continue;
                if (!spot.Standable(map) || spot.GetFirstThing<Building>(map) != null)
                    continue;
                // dead pocket feel: mostly surrounded by wall
                int openNeighbors = 0;
                foreach (IntVec3 n in GenAdj.AdjacentCells)
                    if (carved.Contains(spot + n))
                        openNeighbors++;
                if (openNeighbors > 3)
                    continue;

                if (Rand.Value < 0.55f)
                    GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("RR_BlobWallMineable")), spot, map);
                else
                {
                    Thing loot = ThingMaker.MakeThing(ThingDef.Named("RR_VoidGluttonium"));
                    loot.stackCount = Rand.RangeInclusive(3, 8);
                    GenSpawn.Spawn(loot, spot, map);
                }
                placed++;
            }

            // the way home
            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("RR_VoidPortalReturn")), center, map);

            MapGenerator.PlayerStartSpot = IntVec3.Zero;
        }
    }
}
