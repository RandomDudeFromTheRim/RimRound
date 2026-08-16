using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimRound.GenSteps
{
    /// <summary>
    /// Fills an entire pocket map with warm flesh and a full-coverage maze: the map
    /// edge and border are immovable hardened flesh, the interior is 2-thick flesh
    /// walls with 2-wide carved corridors, open chambers, and a central portal
    /// chamber. Dead ends hide mineable nodes and void gluttonium.
    /// </summary>
    public class GenStep_RRVoidMaze : GenStep
    {
        public override int SeedPart => 20415307;

        const int Pitch = 4; // 2-thick walls, 2-wide corridors

        public override void Generate(Map map, GenStepParams parms)
        {
            TerrainDef floor = TerrainDef.Named("RR_FleshFloor");
            foreach (IntVec3 cell in map.AllCells)
                map.terrainGrid.SetTerrain(cell, floor);

            int side = map.Size.x;
            int cells = (side - 2) / Pitch;          // logical cells per side
            int extent = cells * Pitch;              // maze footprint in tiles
            int ox = (side - extent) / 2;
            int oz = (side - extent) / 2;
            int startCell = cells / 2;

            bool[,] passH = new bool[cells - 1, cells];
            bool[,] passV = new bool[cells, cells - 1];

            // recursive backtracker over all cells — guarantees full connectivity
            var visited = new bool[cells, cells];
            var stack = new Stack<int[]>();
            var options = new List<int[]>(4);
            stack.Push(new[] { startCell, startCell });
            visited[startCell, startCell] = true;
            while (stack.Count > 0)
            {
                int cx = stack.Peek()[0], cz = stack.Peek()[1];
                options.Clear();
                if (cx > 0 && !visited[cx - 1, cz]) options.Add(new[] { 0, cx - 1, cz });
                if (cx < cells - 1 && !visited[cx + 1, cz]) options.Add(new[] { 1, cx + 1, cz });
                if (cz > 0 && !visited[cx, cz - 1]) options.Add(new[] { 2, cx, cz - 1 });
                if (cz < cells - 1 && !visited[cx, cz + 1]) options.Add(new[] { 3, cx, cz + 1 });

                if (options.Count == 0)
                {
                    stack.Pop();
                    continue;
                }

                int[] pick = options[Rand.Range(0, options.Count)];
                switch (pick[0])
                {
                    case 0: passH[cx - 1, cz] = true; break;
                    case 1: passH[cx, cz] = true; break;
                    case 2: passV[cx, cz - 1] = true; break;
                    case 3: passV[cx, cz] = true; break;
                }
                visited[pick[1], pick[2]] = true;
                stack.Push(new[] { pick[1], pick[2] });
            }

            // carve open chambers out of random 2x2 cell clusters
            for (int r = 0; r < 10; r++)
            {
                int rx = Rand.RangeInclusive(0, cells - 2);
                int rz = Rand.RangeInclusive(0, cells - 2);
                if (System.Math.Abs(rx - startCell) <= 1 && System.Math.Abs(rz - startCell) <= 1)
                    continue;
                passH[rx, rz] = true;
                passH[rx, rz + 1] = true;
                passV[rx, rz] = true;
                passV[rx + 1, rz] = true;
            }

            // open a plaza around the portal chamber
            for (int d = -1; d <= 1; d++)
            {
                if (startCell + d >= 0 && startCell + d < cells - 1)
                {
                    passH[startCell + d, startCell] = true;
                    passV[startCell, startCell + d] = true;
                }
            }

            ThingDef hardWall = ThingDef.Named("RR_HardenedFleshWall");
            ThingDef blobWall = ThingDef.Named("RR_BlobWall");
            // Anomaly's own fleshmass wall when available, local variant otherwise
            ThingDef fleshWall = ModsConfig.AnomalyActive
                ? ThingDef.Named("Fleshmass_Active")
                : ThingDef.Named("RR_FleshWall");

            // paint every tile of the map — nothing is left bare
            foreach (IntVec3 cell in map.AllCells)
            {
                int dx = cell.x - ox, dz = cell.z - oz;
                bool insideFootprint = dx >= 0 && dz >= 0 && dx < extent && dz < extent;

                ThingDef def;
                if (!insideFootprint || dx < 2 || dz < 2 || dx >= extent - 2 || dz >= extent - 2)
                {
                    def = hardWall; // map edge + border ring: the immovable carcass
                }
                else
                {
                    int lx = dx % Pitch, lz = dz % Pitch;
                    bool wall;
                    if (lx < 2 && lz < 2)
                        wall = true; // pillar
                    else if (lx < 2)
                        wall = !passH[dx / Pitch - 1, dz / Pitch];
                    else if (lz < 2)
                        wall = !passV[dx / Pitch, dz / Pitch - 1];
                    else
                        wall = false; // floor pocket

                    if (!wall)
                        continue;

                    def = Rand.Value < 0.62f ? blobWall : fleshWall;
                }

                if (cell.DistanceTo(map.Center) <= 3f && def != hardWall)
                    continue; // keep the portal chamber open

                GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map);
            }

            // dead ends: mineable nodes and gluttonium
            for (int ci = 0; ci < cells; ci++)
            {
                for (int cj = 0; cj < cells; cj++)
                {
                    int openings =
                        (ci > 0 && passH[ci - 1, cj] ? 1 : 0) +
                        (ci < cells - 1 && passH[ci, cj] ? 1 : 0) +
                        (cj > 0 && passV[ci, cj - 1] ? 1 : 0) +
                        (cj < cells - 1 && passV[ci, cj] ? 1 : 0);
                    if (openings != 1 || (ci == startCell && cj == startCell))
                        continue;

                    IntVec3 spot = new IntVec3(ox + ci * Pitch + 2, 0, oz + cj * Pitch + 2);
                    if (spot.DistanceTo(map.Center) <= 3f)
                        continue;

                    if (Rand.Value < 0.35f)
                        GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("RR_BlobWallMineable")), spot, map);
                    else if (Rand.Value < 0.4f)
                    {
                        Thing loot = ThingMaker.MakeThing(ThingDef.Named("RR_VoidGluttonium"));
                        loot.stackCount = Rand.RangeInclusive(3, 8);
                        GenSpawn.Spawn(loot, spot, map);
                    }
                }
            }

            // the way home
            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("RR_VoidPortalReturn")), map.Center, map);

            MapGenerator.PlayerStartSpot = IntVec3.Zero;
        }
    }
}
