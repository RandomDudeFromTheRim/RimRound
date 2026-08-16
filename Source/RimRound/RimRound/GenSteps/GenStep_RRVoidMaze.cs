using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimRound.GenSteps
{
    /// <summary>
    /// Fills an entire pocket map with warm flesh and a full-coverage maze: walls
    /// everywhere except 2-wide carved corridors, open chambers, and the central
    /// portal chamber. Dead ends hide mineable nodes and void gluttonium.
    /// </summary>
    public class GenStep_RRVoidMaze : GenStep
    {
        public override int SeedPart => 20415307;

        const int MapSide = 110;
        const int Border = 4;     // starting tile of the maze footprint
        const int Pitch = 3;      // 1-thick walls, 2-wide corridors
        const int Cells = 34;     // logical cells per side (extent = 102)

        public override void Generate(Map map, GenStepParams parms)
        {
            TerrainDef floor = TerrainDef.Named("RR_FleshFloor");
            foreach (IntVec3 cell in map.AllCells)
                map.terrainGrid.SetTerrain(cell, floor);

            int extent = Cells * Pitch;
            int ox = Border, oz = Border;
            int startCell = Cells / 2;

            bool[,] passH = new bool[Cells - 1, Cells];
            bool[,] passV = new bool[Cells, Cells - 1];

            // recursive backtracker over all cells — guarantees full connectivity
            var visited = new bool[Cells, Cells];
            var stack = new Stack<int[]>();
            var options = new List<int[]>(4);
            stack.Push(new[] { startCell, startCell });
            visited[startCell, startCell] = true;
            while (stack.Count > 0)
            {
                int cx = stack.Peek()[0], cz = stack.Peek()[1];
                options.Clear();
                if (cx > 0 && !visited[cx - 1, cz]) options.Add(new[] { 0, cx - 1, cz });
                if (cx < Cells - 1 && !visited[cx + 1, cz]) options.Add(new[] { 1, cx + 1, cz });
                if (cz > 0 && !visited[cx, cz - 1]) options.Add(new[] { 2, cx, cz - 1 });
                if (cz < Cells - 1 && !visited[cx, cz + 1]) options.Add(new[] { 3, cx, cz + 1 });

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
            for (int r = 0; r < 12; r++)
            {
                int rx = Rand.RangeInclusive(0, Cells - 2);
                int rz = Rand.RangeInclusive(0, Cells - 2);
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
                if (startCell + d >= 0 && startCell + d < Cells - 1)
                {
                    passH[startCell + d, startCell] = true;
                    passV[startCell, startCell + d] = true;
                }
            }

            ThingDef blobWall = ThingDef.Named("RR_BlobWall");
            // Anomaly's own fleshmass wall when available, local variant otherwise
            ThingDef fleshWall = ModsConfig.AnomalyActive
                ? ThingDef.Named("Fleshmass_Active")
                : ThingDef.Named("RR_FleshWall");

            for (int tx = ox; tx < ox + extent; tx++)
            {
                for (int tz = oz; tz < oz + extent; tz++)
                {
                    int dx = tx - ox, dz = tz - oz;

                    bool wall;
                    if (dx < 2 || dz < 2 || dx >= extent - 2 || dz >= extent - 2)
                        wall = true;                                          // sealed border ring
                    else if (dx % Pitch == 0 && dz % Pitch == 0)
                        wall = true;                                          // pillar
                    else if (dx % Pitch == 0)
                        wall = !passH[dx / Pitch - 1, dz / Pitch];
                    else if (dz % Pitch == 0)
                        wall = !passV[dx / Pitch, dz / Pitch - 1];
                    else
                        wall = false;                                         // floor pocket

                    if (!wall)
                        continue;

                    IntVec3 cell = new IntVec3(tx, 0, tz);
                    if (cell.DistanceTo(map.Center) <= 3f)
                        continue; // keep the portal chamber open

                    ThingDef def = (dx < 2 || dz < 2 || dx >= extent - 2 || dz >= extent - 2)
                        ? blobWall
                        : (Rand.Value < 0.62f ? blobWall : fleshWall);

                    GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map);
                }
            }

            // dead ends: mineable nodes and gluttonium
            for (int ci = 0; ci < Cells; ci++)
            {
                for (int cj = 0; cj < Cells; cj++)
                {
                    int openings =
                        (ci > 0 && passH[ci - 1, cj] ? 1 : 0) +
                        (ci < Cells - 1 && passH[ci, cj] ? 1 : 0) +
                        (cj > 0 && passV[ci, cj - 1] ? 1 : 0) +
                        (cj < Cells - 1 && passV[ci, cj] ? 1 : 0);
                    if (openings != 1 || (ci == startCell && cj == startCell))
                        continue;

                    IntVec3 spot = new IntVec3(ox + ci * Pitch + 1, 0, oz + cj * Pitch + 1);
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
