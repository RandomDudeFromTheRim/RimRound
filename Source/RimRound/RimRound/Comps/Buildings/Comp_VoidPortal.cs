using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

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

        static readonly IntVec3 PocketMapSize = new IntVec3(130, 1, 130);
        const int CheckIntervalTicks = 120;

        Map mazeMap;
        Building linkedPortal; // set on exit portals: the entry portal that spawned this maze
        int mazeStartTick = -1;
        int nextSpawnTick = -1;
        bool generating = false;
        List<Pawn> pawnsInMaze = new List<Pawn>();

        // Pawns the seam is gulping down. The swallow plays out on the home map
        // before anyone drops into the maze: generating the maze covers the screen
        // with a loading screen, and entering jumps the camera away.
        const int GulpTicks = 150;
        List<Pawn> gulping = new List<Pawn>();
        int gulpEndTick = -1;

        // Not an override of CompInteractable.Active: the base CanInteract refuses
        // with "Already active" while Active is true, which locked everyone else out
        // of an open maze.
        public bool MazeOpen => mazeMap != null || generating;

        // studying seams pays off: longer runs, fewer fleshbeasts (and a bigger haul, above)
        public int MazeDurationTicks => UnityEngine.Mathf.RoundToInt(Props.mazeDurationTicks * (Utilities.GameComponent_RRStudyUnlocks.Has("seam_1") ? 1.25f : 1f));
        int RespawnIntervalTicks => UnityEngine.Mathf.RoundToInt(Props.respawnIntervalTicks * (Utilities.GameComponent_RRStudyUnlocks.Has("seam_2") ? 1.5f : 1f));

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
            Scribe_Collections.Look(ref gulping, "gulping", LookMode.Reference);
            Scribe_Values.Look(ref gulpEndTick, "gulpEndTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && gulping == null)
                gulping = new List<Pawn>();
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

                if (gulping.Contains(activateBy))
                    return "Already being swallowed.";
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

            if (pawnsInMaze.Contains(caster) || gulping.Contains(caster))
                return;

            StartGulp(caster);
        }

        /// <summary>
        /// The thing beneath the seam takes its meal: the pawn is held on the seam
        /// while the ground heaves, gurgles and shakes, then goes down.
        /// </summary>
        void StartGulp(Pawn pawn)
        {
            gulping.Add(pawn);
            gulpEndTick = Find.TickManager.TicksGame + GulpTicks;
            pawn.stances?.stunner?.StunFor(GulpTicks + 30, parent, addBattleLog: false, showMote: false);

            Map home = parent.Map;
            SoundDef.Named("RR_StomachGurgles_Heavy").PlayOneShot(new TargetInfo(parent.Position, home));
            GulpHeave(home, 1f);
            Messages.Message(
                $"The seam's lips close around {pawn.LabelShort}...",
                new LookTargets(parent),
                MessageTypeDefOf.NeutralEvent);
        }

        void GulpHeave(Map home, float strength)
        {
            for (int i = 0; i < 3; i++)
                FleckMaker.ThrowDustPuffThick(parent.DrawPos + Gen.RandomHorizontalVector(1.6f), home, Rand.Range(1.5f, 2.5f) * strength, new Color(0.6f, 0.35f, 0.42f));
            if (home == Find.CurrentMap)
                Find.CameraDriver.shaker.DoShake(0.5f * strength);
        }

        void TickGulp()
        {
            if (gulping.Count == 0)
                return;

            int now = Find.TickManager.TicksGame;
            if (now < gulpEndTick)
            {
                if (parent.IsHashIntervalTick(30))
                    GulpHeave(parent.Map, 0.5f);
                return;
            }

            // down they go
            List<Pawn> swallowed = gulping.Where(p => p != null && !p.Dead && p.Spawned && p.Map == parent.Map).ToList();
            gulping.Clear();
            if (swallowed.Count == 0)
                return;

            SoundDef.Named("RR_StomachBurp_Heavy").PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            GulpHeave(parent.Map, 1.4f);

            if (mazeMap != null)
            {
                foreach (Pawn p in swallowed)
                    EnterMaze(p);
                return;
            }
            if (generating)
                return;

            // Generate off the main thread like vanilla's labyrinth, but only move
            // the pawns and camera in the callback, which runs back on the main thread.
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
               callback: () =>
               {
                   foreach (Pawn p in swallowed)
                       EnterMaze(p);
               });
        }

        void GenerateMaze()
        {
            mazeMap = PocketMapUtility.GeneratePocketMap(
                PocketMapSize,
                DefDatabase<MapGeneratorDef>.GetNamed("RR_VoidMazeMapGen"),
                null,
                parent.MapHeld);
            mazeStartTick = Find.TickManager.TicksGame;
            nextSpawnTick = Find.TickManager.TicksGame + RespawnIntervalTicks / 2;

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

            // arrive at the start of the run; the way home is at the far end
            IntVec3 start = mazeMap.listerThings.ThingsOfDef(Defs.ThingDefOf.RR_VoidMazeEntryScar).FirstOrDefault()?.Position ?? mazeMap.Center;
            IntVec3 drop = CellFinder.RandomClosewalkCellNear(start, mazeMap, 3);
            SkipUtility.SkipTo(pawn, drop, mazeMap);
            ApplyMazeHediffs(pawn);
            pawnsInMaze.Add(pawn);

            Messages.Message(
                $"The seam swallows {pawn.LabelShort} whole. {pawn.LabelShort} slides down into the void maze...",
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
            RemoveMazeHediffs(pawn);

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
                if (Utilities.GameComponent_RRStudyUnlocks.Has("seam_3"))
                    count = UnityEngine.Mathf.CeilToInt(count * 1.5f);
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

        static void RemoveMazeHediffs(Pawn pawn)
        {
            Utilities.HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RR_VoidWarmth, pawn);
            Utilities.HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RR_VoidFascination, pawn);
            Utilities.HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RR_MeldGrowth, pawn);
            // saturation deliberately lingers and drains outside
        }

        /// <summary>
        /// Exit portal: the carrier brings a downed pawn (a void echo, or a fallen
        /// colonist) home with them. The carried pawn makes its own jump, because
        /// SkipUtility.SkipDeSpawn drops whatever an undrafted pawn is carrying.
        /// </summary>
        public void CarryHome(Pawn carrier, Pawn carried)
        {
            Comp_VoidPortal entry = EntryPortal;
            Map homeMap = entry?.parent.MapHeld;
            if (homeMap == null || carrier == null || carried == null)
                return;

            if (carrier.carryTracker.CarriedThing == carried)
                carrier.carryTracker.TryDropCarriedThing(carrier.Position, ThingPlaceMode.Near, out _);

            if (carried.Spawned)
            {
                IntVec3 drop = CellFinder.RandomClosewalkCellNear(entry.parent.Position, homeMap, 2);
                SkipUtility.SkipTo(carried, drop, homeMap);
                RemoveMazeHediffs(carried);
                entry.pawnsInMaze.Remove(carried);

                Messages.Message(
                    $"{carrier.LabelShort} hauls {carried.LabelShort} out of the void maze.",
                    new LookTargets(carried),
                    MessageTypeDefOf.NeutralEvent);
            }

            // the carrier leaves the usual way; this also closes an emptied maze
            ReturnPawn(carrier);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!Props.exitPortal && parent.Spawned)
                TickGulp();
            if (Props.exitPortal || mazeMap == null || !parent.Spawned)
                return;

            if (!parent.IsHashIntervalTick(CheckIntervalTicks))
                return;

            // MapHeld, not Map: a pawn inside a gorge maw is still in the maze
            pawnsInMaze.RemoveAll(p => p == null || p.Dead || p.MapHeld != mazeMap);

            if (pawnsInMaze.Count == 0)
            {
                CloseMaze();
                return;
            }

            if (Find.TickManager.TicksGame > nextSpawnTick)
                TrySpawnMeldBeast(mazeMap);

            if (Find.TickManager.TicksGame > mazeStartTick + MazeDurationTicks)
                ReturnEveryone();
        }

        void ReturnEveryone(Map homeMapOverride = null)
        {
            // swallowed pawns can't be skipped home from inside a maw
            RimRound.Buildings.Building_RRGorgeMaw.SpitOutAll(mazeMap);

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
            if (mazeMap.mapPawns.AllPawnsSpawned.Any(pa => pa.IsColonist || pa.IsPrisonerOfColony)
                || RimRound.Buildings.Building_RRGorgeMaw.AnyHolding(mazeMap))
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

        /// <summary>How far along the maze's run a pawn is: 0 at the healed tear, 1 at the way home.</summary>
        static float MazeProgress(Pawn pawn, Map map)
        {
            return map.GetComponent<MapComponent_RRVoidMaze>()?.ProgressAt(pawn.Position) ?? 0.5f;
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

            // gorge constrictors get likelier the deeper into the gut the pawn has made it
            PawnKindDef constrictor = DefDatabase<PawnKindDef>.GetNamedSilentFail("RR_GorgeConstrictor");
            float progress = MazeProgress(target, map);
            int wave = Mathf.Min(Rand.RangeInclusive(1, 2), Props.maxMeldBeasts - nearby);
            var spawned = new List<Pawn>();
            for (int i = 0; i < wave; i++)
            {
                PawnKindDef kind = constrictor != null && Rand.Chance(0.2f + 0.6f * progress) ? constrictor : PawnKindDefOf.Fingerspike;
                Pawn beast = PawnGenerator.GeneratePawn(kind, Faction.OfEntities);
                GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(target.Position, map, 6), map);
                spawned.Add(beast);
            }

            Messages.Message(
                spawned.Count == 1
                    ? $"A pulsating {spawned[0].LabelShort} oozes from the warm flesh walls!"
                    : $"Pulsating things ooze from the warm flesh walls: {string.Join(" and ", spawned.Select(b => b.LabelShort))}!",
                new LookTargets(spawned),
                MessageTypeDefOf.ThreatSmall);

            nextSpawnTick = Find.TickManager.TicksGame + RespawnIntervalTicks;
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            Comp_VoidPortal entry = EntryPortal;
            if (entry == null || entry.mazeMap == null || entry.mazeStartTick < 0)
                return text;

            int left = entry.mazeStartTick + entry.MazeDurationTicks - Find.TickManager.TicksGame;
            string timer = $"The maze pushes everyone out in {Mathf.Max(0, left).ToStringTicksToPeriod()}.";
            if (Props.exitPortal)
                timer += "\nMake it here in time to leave with a haul of void gluttonium.";
            return text.NullOrEmpty() ? timer : text + "\n" + timer;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;

            if (!Props.exitPortal && MazeOpen)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Force it to disgorge",
                    defaultDesc = "Make the thing beneath the seam spit everyone back up, early. They come back empty-handed.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Cancel"),
                    action = () => ReturnEveryone()
                };
            }
            else if (!Props.exitPortal)
            {
                // the portal can't be deconstructed, so give the player a way to be rid of it
                yield return new Command_Action
                {
                    defaultLabel = "Seal the seam",
                    defaultDesc = "Seal the seam for good. The flesh around it withers back. Another may split open some day.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DisableObelisk"), // Anomaly's "seal the obelisk" icon
                    action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Seal the seam for good?",
                        () => parent.Destroy(DestroyMode.Vanish),
                        destructive: true))
                };
            }
        }
    }
}
