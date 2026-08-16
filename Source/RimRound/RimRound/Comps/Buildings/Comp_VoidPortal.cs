using RimRound.Hediffs;
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

        Map mazeMap;
        Building linkedPortal; // set on exit portals: the entry portal that spawned this maze
        int mazeStartTick = -1;
        int nextSpawnTick = -1;
        bool generating = false;
        List<Pawn> pawnsInMaze = new List<Pawn>();

        public override bool Active => mazeMap != null || generating;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref mazeMap, "mazeMap");
            Scribe_References.Look(ref linkedPortal, "linkedPortal");
            Scribe_Collections.Look(ref pawnsInMaze, "pawnsInMaze", LookMode.Reference);
            Scribe_Values.Look(ref mazeStartTick, "mazeStartTick");
            Scribe_Values.Look(ref nextSpawnTick, "nextSpawnTick");
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

            if (mazeMap == null)
            {
                generating = true;
                Pawn enterer = caster;
                LongEventHandler.QueueLongEvent(delegate
                {
                    try
                    {
                        mazeMap = PocketMapUtility.GeneratePocketMap(
                            new IntVec3(110, 1, 110),
                            DefDatabase<MapGeneratorDef>.GetNamed("RR_VoidMazeMapGen"),
                            null,
                            parent.MapHeld);
                        mazeStartTick = Find.TickManager.TicksGame;
                        nextSpawnTick = Find.TickManager.TicksGame + Props.respawnIntervalTicks / 2;

                        Building exitPortal = mazeMap.listerThings
                            .ThingsOfDef(ThingDef.Named("RR_VoidPortalReturn"))
                            .FirstOrDefault() as Building;
                        exitPortal?.GetComp<Comp_VoidPortal>().SetLinkedPortal((Building)parent);
                    }
                    finally
                    {
                        generating = false;
                    }
                    TeleportIntoMaze(enterer);
                    CameraJumper.TryJump(enterer, CameraJumper.MovementMode.Cut);
                }, "GeneratingLabyrinth", doAsynchronously: true,
                   GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap,
                   showExtraUIInfo: false, forceHideUI: false, callback: null);
            }
            else
            {
                TeleportIntoMaze(caster);
                CameraJumper.TryJump(caster, CameraJumper.MovementMode.Cut);
            }
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
                var weight = activateBy.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
                float sev = weight?.Severity ?? 0;
                if (sev < Props.minWeightToEnter)
                    return $"{activateBy.LabelShort} needs to be at least Chubby to enter the void portal.";

                if (pawnsInMaze.Contains(activateBy))
                    return "Already in the void maze.";
            }

            return base.CanInteract(activateBy, checkOptionalItems);
        }

        void TeleportIntoMaze(Pawn pawn)
        {
            if (mazeMap == null)
                return;

            IntVec3 drop = CellFinder.RandomClosewalkCellNear(mazeMap.Center, mazeMap, 4);
            SkipUtility.SkipTo(pawn, drop, mazeMap);
            ApplyMazeHediffs(pawn);
            pawnsInMaze.Add(pawn);

            Messages.Message(
                $"{pawn.LabelShort} steps through the warm, pulsing portal into the void maze...",
                new LookTargets(pawn),
                MessageTypeDefOf.NeutralEvent);
        }

        void ApplyMazeHediffs(Pawn pawn)
        {
            var warmth = HediffMaker.MakeHediff(Defs.HediffDefOf.RR_VoidWarmth, pawn);
            warmth.Severity = 1f;
            pawn.health.AddHediff(warmth);

            var fascination = HediffMaker.MakeHediff(Defs.HediffDefOf.RR_VoidFascination, pawn);
            fascination.Severity = 0.25f;
            pawn.health.AddHediff(fascination);

            var existingSaturation = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_VoidSaturation);
            if (existingSaturation == null)
            {
                var saturation = HediffMaker.MakeHediff(Defs.HediffDefOf.RR_VoidSaturation, pawn);
                saturation.Severity = 0.05f;
                pawn.health.AddHediff(saturation);
            }
        }

        void ReturnPawn(Pawn pawn, bool forced = false)
        {
            Comp_VoidPortal source = linkedPortal?.GetComp<Comp_VoidPortal>();
            Map homeMap = source?.parent?.MapHeld ?? parent.MapHeld;
            if (homeMap == null || pawn == null)
                return;

            IntVec3 drop = source != null
                ? CellFinder.RandomClosewalkCellNear(source.parent.Position, homeMap, 2)
                : homeMap.Center;

            SkipUtility.SkipTo(pawn, drop, homeMap);

            RemoveHediffSafe(pawn, Defs.HediffDefOf.RR_VoidWarmth);
            RemoveHediffSafe(pawn, Defs.HediffDefOf.RR_VoidFascination);
            RemoveHediffSafe(pawn, Defs.HediffDefOf.RR_MeldGrowth);
            // saturation deliberately lingers and drains outside

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

            if (source != null)
            {
                source.pawnsInMaze.Remove(pawn);
                if (source.pawnsInMaze.Count == 0)
                    source.CloseMaze();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (Props.exitPortal || mazeMap == null || !parent.Spawned)
                return;

            if (!parent.IsHashIntervalTick(120))
                return;

            for (int i = pawnsInMaze.Count - 1; i >= 0; i--)
            {
                Pawn p = pawnsInMaze[i];
                if (p == null || p.Dead || p.Map != mazeMap)
                    pawnsInMaze.RemoveAt(i);
            }

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

        void ReturnEveryone()
        {
            for (int i = pawnsInMaze.Count - 1; i >= 0; i--)
            {
                Pawn p = pawnsInMaze[i];
                if (p != null && !p.Dead && p.Map == mazeMap)
                    ReturnPawn(p, forced: true);
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
            if (!Props.exitPortal)
                ReturnEveryone();
        }

        void TrySpawnMeldBeast(Map map)
        {
            Pawn target = pawnsInMaze.Where(p => p != null && p.Spawned && p.Map == map).RandomElementWithFallback();
            if (target == null || !ModsConfig.AnomalyActive)
                return;

            int nearby = 0;
            foreach (Pawn p in map.mapPawns.AllPawns)
            {
                if (FleshbeastUtility.IsFleshBeast(p.kindDef) && p.Position.DistanceTo(target.Position) < 30f)
                    nearby++;
            }
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

            if (!Props.exitPortal && Active)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Close Portal",
                    defaultDesc = "Pull all pawns out of the void maze early.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Cancel"),
                    action = delegate
                    {
                        ReturnEveryone();
                    }
                };
            }
        }
    }
}
