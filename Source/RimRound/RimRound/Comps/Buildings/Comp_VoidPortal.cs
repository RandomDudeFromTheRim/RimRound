using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimRound.Comps
{
    /// <summary>
    /// Entry portal: generates a true pocket-map flesh dimension (Anomaly pocket map
    /// system, same as the Labyrinth) and teleports pawns into it. Exit portal
    /// (Props.exitPortal): brings a pawn back to the linked entry portal.
    /// </summary>
    public class Comp_VoidPortal : CompInteractable
    {
        public new CompProperties_VoidPortal Props => (CompProperties_VoidPortal)props;

        static readonly IntVec3 PocketMapSize = new IntVec3(110, 1, 110);
        const int CheckIntervalTicks = 120;

        Map mazeMap;
        Building linkedPortal; // set on exit portals: the entry portal that spawned this maze
        int mazeStartTick = -1;
        int nextSpawnTick = -1;
        bool generating = false;
        List<Pawn> pawnsInMaze = new List<Pawn>();

        public override bool Active => mazeMap != null || generating;

        /// <summary>The entry portal that owns the maze: this comp, or the one an exit portal links back to.</summary>
        Comp_VoidPortal EntryPortal => Props.exitPortal ? linkedPortal?.GetComp<Comp_VoidPortal>() : this;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref mazeMap, "mazeMap");
            Scribe_References.Look(ref linkedPortal, "linkedPortal");
            Scribe_Collections.Look(ref pawnsInMaze, "pawnsInMaze", LookMode.Reference);
            Scribe_Values.Look(ref mazeStartTick, "mazeStartTick");
            Scribe_Values.Look(ref nextSpawnTick, "nextSpawnTick");
        }

        public void SetLinkedPortal(Building source)
        {
            linkedPortal = source;
        }

        public override AcceptanceReport CanInteract(Pawn activateBy = null, bool checkOptionalItems = true)
        {
            if (activateBy == null)
                return false;

            if (!activateBy.RaceProps.Humanlike)
                return "Only humanoids can enter the void portal.";

            if (!Props.exitPortal)
            {
                // Race-aware threshold: raw hediff severity means different
                // kilogram amounts for small races like Ratkin
                if (!BodyTypeUtility.PawnIsOverWeightThreshold(activateBy, Defs.BodyTypeDefOf.F_006_Chonky))
                    return $"{activateBy.LabelShort} needs to be at least Chubby to enter the void portal.";

                if (pawnsInMaze.Contains(activateBy))
                    return "Already in the void maze.";
            }

            return base.CanInteract(activateBy, checkOptionalItems);
        }

        protected override void OnInteracted(Pawn caster)
        {
            if (Props.exitPortal)
            {
                ReturnPawn(caster);
                return;
            }

            if (pawnsInMaze.Contains(caster) || generating)
                return;

            if (mazeMap != null)
            {
                EnterMaze(caster);
                return;
            }

            // Generate off the main thread like vanilla's labyrinth, but only move
            // the pawn and camera in the callback, which runs back on the main thread.
            generating = true;
            LongEventHandler.QueueLongEvent(delegate
            {
                try
                {
                    GenerateMaze();
                }
                finally
                {
                    generating = false;
                }
            }, "GeneratingLabyrinth", doAsynchronously: true,
               GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap,
               showExtraUIInfo: false, forceHideUI: false,
               callback: () => EnterMaze(caster));
        }

        void GenerateMaze()
        {
            mazeMap = PocketMapUtility.GeneratePocketMap(
                PocketMapSize,
                DefDatabase<MapGeneratorDef>.GetNamed("RR_VoidMazeMapGen"),
                null,
                parent.MapHeld);
            mazeStartTick = Find.TickManager.TicksGame;
            nextSpawnTick = Find.TickManager.TicksGame + Props.respawnIntervalTicks / 2;

            Building exitPortal = mazeMap.listerThings
                .ThingsOfDef(Defs.ThingDefOf.RR_VoidPortalReturn)
                .FirstOrDefault() as Building;
            exitPortal?.GetComp<Comp_VoidPortal>().SetLinkedPortal((Building)parent);
        }

        void EnterMaze(Pawn pawn)
        {
            // the pawn may have died or left while the maze was generating
            if (mazeMap == null || pawn == null || pawn.Dead || !pawn.Spawned)
                return;

            IntVec3 drop = CellFinder.RandomClosewalkCellNear(mazeMap.Center, mazeMap, 4);
            SkipUtility.SkipTo(pawn, drop, mazeMap);
            ApplyMazeHediffs(pawn);
            pawnsInMaze.Add(pawn);

            Messages.Message(
                $"{pawn.LabelShort} steps through the warm, pulsing portal into the void maze...",
                new LookTargets(pawn),
                MessageTypeDefOf.NeutralEvent);
            CameraJumper.TryJump(pawn, CameraJumper.MovementMode.Cut);
        }

        static void ApplyMazeHediffs(Pawn pawn)
        {
            Utilities.HediffUtility.AddHediffWithSeverity(Defs.HediffDefOf.RR_VoidWarmth, pawn, 1f);
            Utilities.HediffUtility.AddHediffWithSeverity(Defs.HediffDefOf.RR_VoidFascination, pawn, 0.25f);

            if (Utilities.HediffUtility.GetHediffOfDefFrom(Defs.HediffDefOf.RR_VoidSaturation, pawn) == null)
                Utilities.HediffUtility.AddHediffWithSeverity(Defs.HediffDefOf.RR_VoidSaturation, pawn, 0.05f);
        }

        /// <param name="forced">Pulled out early (timeout, "Close Portal", portal destroyed): no loot.</param>
        /// <param name="homeMapOverride">The entry portal's map, for when it can no longer be read off the portal (after it is destroyed).</param>
        void ReturnPawn(Pawn pawn, bool forced = false, Map homeMapOverride = null)
        {
            Comp_VoidPortal entry = EntryPortal;
            Map homeMap = homeMapOverride ?? entry?.parent.MapHeld ?? parent.MapHeld;
            if (homeMap == null || pawn == null)
                return;

            IntVec3 drop = entry != null
                ? CellFinder.RandomClosewalkCellNear(entry.parent.Position, homeMap, 2)
                : homeMap.Center;

            SkipUtility.SkipTo(pawn, drop, homeMap);

            Utilities.HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RR_VoidWarmth, pawn);
            Utilities.HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RR_VoidFascination, pawn);
            Utilities.HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RR_MeldGrowth, pawn);
            // saturation deliberately lingers and drains outside

            if (forced)
            {
                Messages.Message(
                    $"{pawn.LabelShort} was pulled from the void maze early!",
                    new LookTargets(pawn),
                    MessageTypeDefOf.NeutralEvent);
            }
            else
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

            if (entry != null)
            {
                entry.pawnsInMaze.Remove(pawn);
                if (entry.pawnsInMaze.Count == 0)
                    entry.CloseMaze();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (Props.exitPortal || mazeMap == null || !parent.Spawned)
                return;

            if (!parent.IsHashIntervalTick(CheckIntervalTicks))
                return;

            pawnsInMaze.RemoveAll(p => p == null || p.Dead || p.Map != mazeMap);

            if (pawnsInMaze.Count == 0)
            {
                CloseMaze();
                return;
            }

            if (Find.TickManager.TicksGame > nextSpawnTick)
                TrySpawnMeldBeast(mazeMap);

            if (Find.TickManager.TicksGame > mazeStartTick + Props.mazeDurationTicks)
                ReturnEveryone();
        }

        void ReturnEveryone(Map homeMapOverride = null)
        {
            for (int i = pawnsInMaze.Count - 1; i >= 0; i--)
            {
                Pawn p = pawnsInMaze[i];
                if (p != null && !p.Dead && p.Map == mazeMap)
                    ReturnPawn(p, forced: true, homeMapOverride);
                else
                    pawnsInMaze.RemoveAt(i);
            }
            CloseMaze();
        }

        void CloseMaze()
        {
            if (mazeMap == null)
                return;

            // never strand anyone inside
            if (mazeMap.mapPawns.AllPawnsSpawned.Any(pa => pa.IsColonist || pa.IsPrisonerOfColony))
                return;

            PocketMapUtility.DestroyPocketMap(mazeMap);
            mazeMap = null;
            pawnsInMaze.Clear();
            mazeStartTick = -1;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            // parent.MapHeld is already null here, so hand the returning pawns the old map
            if (!Props.exitPortal)
                ReturnEveryone(previousMap);
        }

        void TrySpawnMeldBeast(Map map)
        {
            Pawn target = pawnsInMaze.Where(p => p != null && p.Spawned && p.Map == map).RandomElementWithFallback();
            if (target == null || !ModsConfig.AnomalyActive)
                return;

            int nearby = map.mapPawns.AllPawns.Count(p =>
                FleshbeastUtility.IsFleshBeast(p.kindDef) && p.Position.DistanceTo(target.Position) < 30f);
            if (nearby >= Props.maxMeldBeasts)
                return;

            Pawn beast = PawnGenerator.GeneratePawn(PawnKindDefOf.Fingerspike, Faction.OfEntities);
            GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(target.Position, map, 6), map);

            Messages.Message(
                $"A pulsating {beast.LabelShort} oozes from the warm flesh walls!",
                new LookTargets(beast),
                MessageTypeDefOf.ThreatSmall);

            nextSpawnTick = Find.TickManager.TicksGame + Props.respawnIntervalTicks;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;

            if (!Props.exitPortal && Active)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Close Portal",
                    defaultDesc = "Pull all pawns out of the void maze early.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Cancel"),
                    action = () => ReturnEveryone()
                };
            }
        }
    }
}
