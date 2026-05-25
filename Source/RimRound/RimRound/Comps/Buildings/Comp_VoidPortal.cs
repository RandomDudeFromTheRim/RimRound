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

            // Ring of blob walls at radius 8
            for (int i = 0; i < 30; i++)
            {
                float angle = (float)i / 30f * 360f;
                int radius = (i % 3 == 0) ? 7 : 9;
                IntVec3 cell = center + new IntVec3(
                    (int)(Mathf.Sin(angle * Mathf.Deg2Rad) * radius),
                    0,
                    (int)(Mathf.Cos(angle * Mathf.Deg2Rad) * radius));

                if (!cell.InBounds(map) || !cell.Standable(map))
                    continue;

                Thing wall = ThingMaker.MakeThing(ThingDef.Named("RR_BlobWall"));
                GenSpawn.Spawn(wall, cell, map, Rot4.North);
                spawnedWalls.Add(wall);
            }

            // Mineable nodes scattered inside
            for (int i = 0; i < 5; i++)
            {
                IntVec3 cell = center + new IntVec3(
                    Rand.RangeInclusive(-5, 5),
                    0,
                    Rand.RangeInclusive(-5, 5));

                if (!cell.InBounds(map) || !cell.Standable(map) || cell == center)
                    continue;

                Thing node = ThingMaker.MakeThing(ThingDef.Named("RR_BlobWallMineable"));
                GenSpawn.Spawn(node, cell, map, Rot4.North);
                spawnedWalls.Add(node);
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
