using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.Hediffs
{
    /// <summary>
    /// Void vigor on a void echo: keeps it moving despite its mass, hardens it
    /// against retaliation, and — when RimVore2 is loaded — makes it hellbent on
    /// swallowing its original whole via RV2's forced OralHold path (swallow,
    /// hold, regurgitate: non-fatal).
    /// </summary>
    public class Hediff_VoidEchoVigor : Hediff
    {
        const int MilkIntervalTicks = 25000;

        public Pawn markedPrey;
        List<Pawn> contained = new List<Pawn>();

        public int ContainedCount => contained.Count;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref markedPrey, "markedPrey");
            Scribe_Collections.Look(ref contained, "containedPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.LoadingVars && contained == null)
                contained = new List<Pawn>();
        }

        /// <summary>Swallow a pawn whole: they vanish into the echo, retrievable later.</summary>
        public void Contain(Pawn meal)
        {
            if (meal == null || contained.Contains(meal))
                return;

            meal.DeSpawn();
            Find.WorldPawns.PassToWorld(meal, PawnDiscardDecideMode.KeepForever);
            contained.Add(meal);
        }

        /// <summary>Everyone climbs back out — dazed, heavier, and full of stories.</summary>
        public void ReleaseAll(IntVec3 at, Map map)
        {
            for (int i = contained.Count - 1; i >= 0; i--)
            {
                Pawn p = contained[i];
                if (p == null)
                    continue;

                if (Find.WorldPawns.Contains(p))
                    Find.WorldPawns.RemovePawn(p);

                if (!p.Dead)
                {
                    GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(at, map, 2), map);
                    p.stances?.stunner?.StunFor(600, p, addBattleLog: false, showMote: true);
                    Utilities.HediffUtility.QueueWeightGain(p, 15f);
                }
                contained.RemoveAt(i);
            }
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            ReleaseOnLoss();
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            ReleaseOnLoss();
        }

        /// <summary>
        /// A dead or cured echo can't hold anyone. Swallowed pawns live in the world
        /// pawn list, so without this they would be lost for good.
        /// </summary>
        void ReleaseOnLoss()
        {
            if (contained.Count == 0 || pawn == null)
                return;

            Map map = pawn.MapHeld ?? pawn.prevMap;
            if (map == null)
                return;

            IntVec3 at = pawn.PositionHeld.InBounds(map) ? pawn.PositionHeld : map.Center;
            int count = contained.Count;
            ReleaseAll(at, map);
            Messages.Message(
                $"The void echo's hold breaks — {count} swallowed {(count == 1 ? "pawn spills" : "pawns spill")} back out.",
                new TargetInfo(at, map),
                MessageTypeDefOf.NeutralEvent);
        }

        /// <summary>Plays the gorged belly heave while someone is inside, and stops it once they're out.</summary>
        public void UpdateGorgedAnimation()
        {
            PawnRenderer renderer = pawn?.Drawer?.renderer;
            if (renderer == null)
                return;

            AnimationDef gorged = Defs.RRAnimationDefOf.RR_EchoGorged;
            if (contained.Count > 0 && !pawn.Dead)
            {
                if (renderer.CurAnimation != gorged)
                    renderer.SetAnimation(gorged);
            }
            else if (renderer.CurAnimation == gorged)
            {
                renderer.SetAnimation(null);
            }
        }

        public override void Tick()
        {
            base.Tick();

            // on a holding platform the platform owns the animation (see the
            // BuildingHoldingPlatform_UpdateAnimation patch)
            if (pawn != null && pawn.Spawned && pawn.IsHashIntervalTick(60))
                UpdateGorgedAnimation();

            if (contained.Count > 0 && pawn != null && !pawn.Dead &&
                pawn.holdingOwner != null && pawn.IsHashIntervalTick(MilkIntervalTicks))
                ProduceVoidMilk();

            if (pawn == null || pawn.Dead || !pawn.Spawned || !pawn.IsHashIntervalTick(120))
                return;

            if (pawn.CurJobDef?.defName == "RV2_VoreInitAsPredator" || pawn.InMentalState || pawn.Downed)
                return;

            Pawn prey = ChoosePrey();
            if (prey == null || prey.Downed)
                return;

            TryStartRV2OralHold(prey);
        }

        /// <summary>
        /// Voidmilk flows while the echo digests its guests on a holding platform.
        /// A held pawn is despawned, so it has to go through MapHeld/PositionHeld.
        /// </summary>
        void ProduceVoidMilk()
        {
            Map map = pawn.MapHeld;
            if (map == null)
                return;

            // only a colony that follows the Feedees knows how to milk it
            if (!Utilities.FeedeesUtility.PlayerFollowsFeedees())
            {
                Messages.Message(
                    "The void echo swells with voidmilk, but nobody here knows how to draw it out. It seeps away. (Requires an ideoligion with the Feedees meme.)",
                    new LookTargets(pawn.SpawnedParentOrMe),
                    MessageTypeDefOf.NeutralEvent);
                return;
            }

            int count = 2 + contained.Count * 2;
            Building rig = MilkingRig();
            if (rig != null)
            {
                // the rig draws it off steadily instead of letting it seep out
                count = Mathf.CeilToInt(count * MilkingRigYieldFactor);
                float nutrition = count * Defs.ThingDefOf.RR_VoidMilk.GetStatValueAbstract(StatDefOf.Nutrition);
                if (FeedingTube.FoodNetworkAccess.Current.TryStore(rig, nutrition, VoidMilkDensity))
                {
                    Messages.Message(
                        $"The milking rig draws {count} voidmilk out of the void echo and into the feed lines.",
                        new LookTargets(rig),
                        MessageTypeDefOf.PositiveEvent);
                    return;
                }
            }

            Thing milk = ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidMilk);
            milk.stackCount = count;
            GenPlace.TryPlaceThing(milk, rig?.Position ?? pawn.PositionHeld, map, ThingPlaceMode.Near);
            Messages.Message(
                rig != null
                    ? $"The milking rig draws {count} voidmilk out of the void echo. With no room on the feed lines, it bottles it."
                    : $"The void echo produces {count} voidmilk.",
                new LookTargets(rig ?? pawn.SpawnedParentOrMe),
                MessageTypeDefOf.PositiveEvent);
        }

        const float MilkingRigYieldFactor = 1.5f;
        // voidmilk is thick: it fills a feed line faster than the nutrition it carries
        const float VoidMilkDensity = 1.5f;

        /// <summary>A powered void milking rig linked to the holding platform the echo is on, if any.</summary>
        Building MilkingRig()
        {
            if (!(pawn.ParentHolder is Thing platform))
                return null;
            var facilities = platform.TryGetComp<CompAffectedByFacilities>();
            if (facilities == null)
                return null;
            foreach (Thing f in facilities.LinkedFacilitiesListForReading)
            {
                if (f.def.defName != "RR_VoidMilkingRig" || !(f is Building b))
                    continue;
                if (b.TryGetComp<CompPowerTrader>() is CompPowerTrader power && !power.PowerOn)
                    continue;
                return b;
            }
            return null;
        }

        Pawn ChoosePrey()
        {
            if (markedPrey != null && markedPrey.Spawned && !markedPrey.Dead &&
                markedPrey.Map == pawn.Map && markedPrey.Position.DistanceTo(pawn.Position) < 20f)
                return markedPrey;

            return pawn.Map.mapPawns.FreeColonistsAndPrisonersSpawned
                .Where(p => p.Position.DistanceTo(pawn.Position) < 15f)
                .OrderBy(p => p.Position.DistanceTo(pawn.Position))
                .FirstOrDefault();
        }

        static bool rv2Missing;
        static MethodInfo makerMI, pathGetMI;
        static FieldInfo pathFI, forcedFI;
        static JobDef predJobDef;
        static Def oralHoldDef;

        void TryStartRV2OralHold(Pawn prey)
        {
            if (rv2Missing)
                return;
            try
            {
                if (makerMI == null)
                {
                    var makerT = AccessTools.TypeByName("RimVore2.VoreJobMaker");
                    var voreJobT = AccessTools.TypeByName("RimVore2.VoreJob");
                    var pathDefT = AccessTools.TypeByName("RimVore2.VorePathDef");
                    if (makerT == null || voreJobT == null || pathDefT == null)
                    {
                        rv2Missing = true;
                        return;
                    }

                    makerMI = AccessTools.Method(makerT, "MakeJob", new[] { typeof(JobDef), typeof(Pawn), typeof(LocalTargetInfo) });
                    var dbT = typeof(DefDatabase<>).MakeGenericType(pathDefT);
                    pathGetMI = AccessTools.Method(dbT, "GetNamed", new[] { typeof(string), typeof(bool) });
                    pathFI = AccessTools.Field(voreJobT, "VorePath");
                    forcedFI = AccessTools.Field(voreJobT, "IsForced");
                    predJobDef = DefDatabase<JobDef>.GetNamed("RV2_VoreInitAsPredator", false);
                    oralHoldDef = pathGetMI?.Invoke(null, new object[] { "OralHold", false }) as Def;

                    if (makerMI == null || pathFI == null || forcedFI == null || predJobDef == null || oralHoldDef == null)
                    {
                        rv2Missing = true;
                        return;
                    }
                }

                Job job = makerMI.Invoke(null, new object[] { predJobDef, pawn, (LocalTargetInfo)prey }) as Job;
                if (job == null)
                    return;

                pathFI.SetValue(job, oralHoldDef);
                forcedFI.SetValue(job, true);
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            }
            catch
            {
                rv2Missing = true; // RV2 present but incompatible — stop trying
            }
        }
    }
}
