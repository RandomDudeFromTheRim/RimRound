using RimRound.Hediffs;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimRound.Comps
{
    public class Comp_VoidPortal : CompInteractable
    {
        public new CompProperties_VoidPortal Props => (CompProperties_VoidPortal)props;

        List<Pawn> pawnsInMaze = new List<Pawn>();
        List<Thing> spawnedWalls = new List<Thing>();
        int nextSpawnTick = -1;
        bool mazeGenerated = false;

        public override bool Active => pawnsInMaze.Count > 0;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref pawnsInMaze, "pawnsInMaze", LookMode.Reference);
            Scribe_Collections.Look(ref spawnedWalls, "spawnedWalls", LookMode.Reference);
            Scribe_Values.Look(ref nextSpawnTick, "nextSpawnTick");
            Scribe_Values.Look(ref mazeGenerated, "mazeGenerated");
        }

        protected override void OnInteracted(Pawn caster)
        {
            if (pawnsInMaze.Contains(caster))
                return;

            EnterMaze(caster);
        }

        public override AcceptanceReport CanInteract(Pawn activateBy = null, bool checkOptionalItems = true)
        {
            if (activateBy == null)
                return false;

            if (!activateBy.RaceProps.Humanlike)
                return "Only humanoids can enter the void portal.";

            var weight = activateBy.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
            float sev = weight?.Severity ?? 0;
            if (sev < Props.minWeightToEnter)
                return $"{activateBy.LabelShort} needs to be at least Chubby to enter the void portal.";

            if (pawnsInMaze.Contains(activateBy))
                return "Already in the void maze.";

            return base.CanInteract(activateBy, checkOptionalItems);
        }

        void GenerateMaze()
        {
            if (mazeGenerated || !parent.Spawned)
                return;
            mazeGenerated = true;

            Map map = parent.Map;
            IntVec3 center = parent.Position;

            const int cells = 13;  // logical cells per side; portal starts in the middle cell
            const int pitch = 3;   // 1-thick walls with 2-wide corridors
            int extent = cells * pitch;
            int startCell = cells / 2;

            // origin chosen so the portal lands inside the middle cell's floor pocket
            int ox = center.x - 1 - pitch * startCell;
            int oz = center.z - 1 - pitch * startCell;

            // passages between adjacent cells; every cell starts sealed
            bool[,] passH = new bool[cells - 1, cells]; // between (i,j) and (i+1,j)
            bool[,] passV = new bool[cells, cells - 1]; // between (i,j) and (i,j+1)

            // recursive backtracker — guarantees every cell is reachable
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
            for (int r = 0; r < 6; r++)
            {
                int rx = Rand.RangeInclusive(0, cells - 2);
                int rz = Rand.RangeInclusive(0, cells - 2);
                if (Mathf.Abs(rx - startCell) <= 1 && Mathf.Abs(rz - startCell) <= 1)
                    continue; // keep the portal chamber as corridors
                passH[rx, rz] = true;
                passH[rx, rz + 1] = true;
                passV[rx, rz] = true;
                passV[rx + 1, rz] = true;
            }

            // four entrances through the outer ring at the compass midpoints
            var entrances = new HashSet<IntVec3>();
            int midLo = startCell * pitch + 1;
            entrances.Add(new IntVec3(ox, 0, oz + midLo));
            entrances.Add(new IntVec3(ox, 0, oz + midLo + 1));
            entrances.Add(new IntVec3(ox + extent, 0, oz + midLo));
            entrances.Add(new IntVec3(ox + extent, 0, oz + midLo + 1));
            entrances.Add(new IntVec3(ox + midLo, 0, oz));
            entrances.Add(new IntVec3(ox + midLo + 1, 0, oz));
            entrances.Add(new IntVec3(ox + midLo, 0, oz + extent));
            entrances.Add(new IntVec3(ox + midLo + 1, 0, oz + extent));

            ThingDef blobWall = ThingDef.Named("RR_BlobWall");
            ThingDef mineable = ThingDef.Named("RR_BlobWallMineable");

            for (int tx = ox; tx <= ox + extent; tx++)
            {
                for (int tz = oz; tz <= oz + extent; tz++)
                {
                    int dx = tx - ox, dz = tz - oz;

                    bool wall;
                    if (dx == extent || dz == extent)
                        wall = true;                                          // north & east outer ring
                    else if (dx % pitch == 0 && dz % pitch == 0)
                        wall = true;                                          // pillar
                    else if (dx % pitch == 0)
                        wall = dx == 0 || !passH[dx / pitch - 1, dz / pitch]; // vertical strips incl. west ring
                    else if (dz % pitch == 0)
                        wall = dz == 0 || !passV[dx / pitch, dz / pitch - 1]; // horizontal strips incl. south ring
                    else
                        wall = false;                                         // floor pocket

                    if (!wall)
                        continue;

                    IntVec3 cell = new IntVec3(tx, 0, tz);
                    if (entrances.Contains(cell))
                        continue;
                    if (!cell.InBounds(map) || !cell.Standable(map))
                        continue;
                    if (cell.DistanceTo(center) <= 2f)
                        continue; // never wall in the portal itself
                    if (cell.GetThingList(map).Any(t => t is Pawn))
                        continue;

                    Thing wallThing = ThingMaker.MakeThing(blobWall);
                    GenSpawn.Spawn(wallThing, cell, map, Rot4.North);
                    spawnedWalls.Add(wallThing);
                }
            }

            // reward the dead ends: mineable blob nodes and scattered void gluttonium
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

                    IntVec3 spot = new IntVec3(ox + ci * pitch + 1, 0, oz + cj * pitch + 1);
                    if (!spot.InBounds(map) || !spot.Standable(map) || spot.DistanceTo(center) <= 2f)
                        continue;
                    if (spot.GetThingList(map).Any(t => t is Pawn))
                        continue;

                    if (Rand.Value < 0.4f)
                    {
                        Thing node = ThingMaker.MakeThing(mineable);
                        GenSpawn.Spawn(node, spot, map, Rot4.North);
                        spawnedWalls.Add(node);
                    }
                    else if (Rand.Value < 0.3f)
                    {
                        Thing loot = ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidGluttonium);
                        loot.stackCount = Rand.RangeInclusive(3, 8);
                        GenSpawn.Spawn(loot, spot, map, Rot4.North);
                        spawnedWalls.Add(loot);
                    }
                }
            }
        }

        void CleanupMaze()
        {
            Thing.allowDestroyNonDestroyable = true;
            foreach (Thing t in spawnedWalls)
            {
                if (t != null && !t.Destroyed)
                    t.Destroy();
            }
            spawnedWalls.Clear();
            mazeGenerated = false;
            Thing.allowDestroyNonDestroyable = false;
        }

        void EnterMaze(Pawn pawn)
        {
            pawnsInMaze.Add(pawn);
            GenerateMaze();

            var warmth = HediffMaker.MakeHediff(Defs.HediffDefOf.RR_VoidWarmth, pawn);
            warmth.Severity = 1f;
            pawn.health.AddHediff(warmth);

            var fascination = HediffMaker.MakeHediff(Defs.HediffDefOf.RR_VoidFascination, pawn);
            fascination.Severity = 0.25f;
            pawn.health.AddHediff(fascination);

            nextSpawnTick = Find.TickManager.TicksGame + (int)(Props.respawnIntervalTicks * 0.5f);

            Messages.Message(
                $"{pawn.LabelShort} steps through the warm, pulsing portal into the void maze...",
                new LookTargets(pawn),
                MessageTypeDefOf.NeutralEvent);
        }

        void ExitMaze(Pawn pawn, bool forced = false)
        {
            if (!pawnsInMaze.Contains(pawn))
                return;

            pawnsInMaze.Remove(pawn);

            RemoveHediffSafe(pawn, Defs.HediffDefOf.RR_VoidWarmth);
            RemoveHediffSafe(pawn, Defs.HediffDefOf.RR_VoidFascination);
            RemoveHediffSafe(pawn, Defs.HediffDefOf.RR_MeldGrowth);

            if (pawnsInMaze.Count == 0)
                CleanupMaze();

            if (!forced)
            {
                int count = Rand.RangeInclusive(5, 15);
                var glut = ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidGluttonium);
                glut.stackCount = count;
                GenPlace.TryPlaceThing(glut, pawn.Position, pawn.Map, ThingPlaceMode.Near);

                Messages.Message(
                    $"{pawn.LabelShort} emerges from the void maze, carrying {count} void gluttonium!",
                    new LookTargets(pawn),
                    MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Messages.Message(
                    $"{pawn.LabelShort} was pulled from the void maze early!",
                    new LookTargets(pawn),
                    MessageTypeDefOf.NeutralEvent);
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!Active || !parent.Spawned)
                return;

            if (!parent.IsHashIntervalTick(120))
                return;

            for (int i = pawnsInMaze.Count - 1; i >= 0; i--)
            {
                Pawn p = pawnsInMaze[i];
                if (p == null || p.Dead || !p.Spawned)
                {
                    ExitMaze(p, forced: true);
                    continue;
                }

                if (Find.TickManager.TicksGame > nextSpawnTick)
                    TrySpawnMeldBeast(p);
            }

            if (Find.TickManager.TicksGame > nextSpawnTick + Props.mazeDurationTicks)
            {
                for (int i = pawnsInMaze.Count - 1; i >= 0; i--)
                    ExitMaze(pawnsInMaze[i]);
            }
        }

        void TrySpawnMeldBeast(Pawn target)
        {
            if (target?.Map == null || !ModsConfig.AnomalyActive)
                return;

            int nearby = 0;
            foreach (Pawn p in target.Map.mapPawns.AllPawns)
            {
                if (FleshbeastUtility.IsFleshBeast(p.kindDef) && p.Position.DistanceTo(target.Position) < 30f)
                    nearby++;
            }
            if (nearby >= Props.maxMeldBeasts)
                return;

            Pawn beast = PawnGenerator.GeneratePawn(PawnKindDefOf.Fingerspike, Faction.OfEntities);
            GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(target.Position, target.Map, 4), target.Map);

            Messages.Message(
                $"A pulsating {beast.LabelShort} oozes from the warm flesh walls!",
                new LookTargets(beast),
                MessageTypeDefOf.ThreatSmall);

            nextSpawnTick = Find.TickManager.TicksGame + Props.respawnIntervalTicks;
        }

        static void RemoveHediffSafe(Pawn pawn, HediffDef def)
        {
            var h = pawn.health?.hediffSet?.GetFirstHediffOfDef(def);
            if (h != null)
                pawn.health.RemoveHediff(h);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;

            if (Active)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Close Portal",
                    defaultDesc = "Pull all pawns out of the void maze early.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Cancel"),
                    action = delegate
                    {
                        for (int i = pawnsInMaze.Count - 1; i >= 0; i--)
                            ExitMaze(pawnsInMaze[i], forced: true);
                    }
                };
            }
        }
    }
}
