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

        static readonly IntVec3[] Dirs = { IntVec3.East, IntVec3.West, IntVec3.North, IntVec3.South };

        const int MainTunnels = 16;
        const int FeederTunnels = 60;
        const float ShellFraction = 0.48f; // tunnels stay inside this fraction of the map width
        const int HardenedShellDepth = 3;
        const int MaxRewards = 45;

        // Per-run state. GenStep instances belong to the (shared) def, so these
        // are cleared again once generation finishes.
        Map map;
        IntVec3 center;
        float carveLimit;
        HashSet<IntVec3> carved;
        List<IntVec3> carvedList; // carved cells that new tunnels and rewards can start from

        public override void Generate(Map map, GenStepParams parms)
        {
            this.map = map;
            center = map.Center;
            carveLimit = map.Size.x * ShellFraction;
            carved = new HashSet<IntVec3>();
            carvedList = new List<IntVec3> { center };
            try
            {
                GenerateInternal();
            }
            finally
            {
                this.map = null;
                carved = null;
                carvedList = null;
            }
        }

        void GenerateInternal()
        {
            TerrainDef floor = TerrainDef.Named("RR_FleshFloor");
            foreach (IntVec3 cell in map.AllCells)
                map.terrainGrid.SetTerrain(cell, floor);

            // the portal chamber
            CarveRadius(center, 4);

            for (int i = 0; i < MainTunnels; i++)
                CarveMainTunnel();

            // short feeder crawls off existing tunnels for dead ends
            for (int i = 0; i < FeederTunnels; i++)
                CarveFeederTunnel();

            FillUncarvedWithFlesh();
            PlaceRewards();

            // the way home
            GenSpawn.Spawn(ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidPortalReturn), center, map);

            MapGenerator.PlayerStartSpot = IntVec3.Zero;
        }

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

        bool InsideShell(IntVec3 pos) => pos.InHorDistOf(center, carveLimit);

        static IntVec3 RandomDir() => Dirs[Rand.Range(0, Dirs.Length)];

        IntVec3 RandomCarvedCell() => carvedList[Rand.Range(0, carvedList.Count)];

        /// <summary>A long winding walker: mostly keeps direction, squeezes and widens, and occasionally opens a chamber.</summary>
        void CarveMainTunnel()
        {
            IntVec3 pos = RandomCarvedCell();
            IntVec3 dir = RandomDir();
            int length = Rand.RangeInclusive(180, 420);
            int squeezeLeft = 0, chamberCooldown = 0;

            for (int step = 0; step < length; step++)
            {
                // momentum: mostly keep direction, sometimes swerve hard
                if (Rand.Value < 0.25f)
                    dir = RandomDir();

                pos += dir;
                if (!InsideShell(pos))
                    break; // stay off the hardened shell

                // width rhythm: squeeze to 1-wide, breathe back to 2
                if (squeezeLeft > 0)
                    squeezeLeft--;
                else if (Rand.Value < 0.18f)
                    squeezeLeft = Rand.RangeInclusive(12, 30);

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

        void CarveFeederTunnel()
        {
            IntVec3 pos = RandomCarvedCell();
            IntVec3 dir = RandomDir();
            int length = Rand.RangeInclusive(10, 40);

            for (int step = 0; step < length; step++)
            {
                if (Rand.Value < 0.35f)
                    dir = RandomDir();
                pos += dir;
                if (!InsideShell(pos))
                    break;
                CarveRadius(pos, Rand.Value < 0.75f ? 0 : 1); // mostly 1-wide
                carvedList.Add(pos);
            }
        }

        /// <summary>Hardened shell near the edge, flesh mass elsewhere; carved cells stay open.</summary>
        void FillUncarvedWithFlesh()
        {
            ThingDef hardWall = ThingDef.Named("RR_HardenedFleshWall");
            ThingDef blobWall = ThingDef.Named("RR_BlobWall");
            ThingDef fleshWall = ModsConfig.AnomalyActive
                ? ThingDef.Named("Fleshmass_Active")
                : ThingDef.Named("RR_FleshWall");

            int sizeX = map.Size.x, sizeZ = map.Size.z;
            foreach (IntVec3 cell in map.AllCells)
            {
                if (carved.Contains(cell))
                    continue;

                int edgeDist = System.Math.Min(
                    System.Math.Min(cell.x, sizeX - 1 - cell.x),
                    System.Math.Min(cell.z, sizeZ - 1 - cell.z));

                ThingDef def = edgeDist <= HardenedShellDepth
                    ? hardWall
                    : (Rand.Value < 0.62f ? blobWall : fleshWall);

                GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map);
            }
        }

        /// <summary>Mineables and gluttonium tucked into dead pockets away from the portal.</summary>
        void PlaceRewards()
        {
            ThingDef mineable = ThingDef.Named("RR_BlobWallMineable");
            int placed = 0;
            for (int i = 0; i < 400 && placed < MaxRewards; i++)
            {
                IntVec3 spot = RandomCarvedCell();
                if (spot.DistanceTo(center) < 12)
                    continue;
                if (!spot.Standable(map) || spot.GetFirstThing<Building>(map) != null)
                    continue;
                if (!IsDeadPocket(spot))
                    continue;

                if (Rand.Value < 0.55f)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(mineable), spot, map);
                }
                else
                {
                    Thing loot = ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidGluttonium);
                    loot.stackCount = Rand.RangeInclusive(3, 8);
                    GenSpawn.Spawn(loot, spot, map);
                }
                placed++;
            }
        }

        /// <summary>Mostly surrounded by wall: no more than 3 of the 8 neighbours are open.</summary>
        bool IsDeadPocket(IntVec3 spot)
        {
            int openNeighbors = 0;
            foreach (IntVec3 n in GenAdj.AdjacentCells)
                if (carved.Contains(spot + n))
                    openNeighbors++;
            return openNeighbors <= 3;
        }
    }
}
