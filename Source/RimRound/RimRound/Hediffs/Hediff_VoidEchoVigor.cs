using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
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

                    var fnd = p.TryGetComp<Comps.FullnessAndDietStats_ThingComp>();
                    if (fnd != null && !fnd.Disabled)
                        fnd.activeWeightGainRequests.Enqueue(
                            new Comps.WeightGainRequest(15f, Find.TickManager.TicksGame + 5, 30000, false));
                }
                contained.RemoveAt(i);
            }
        }

        public override void Tick()
        {
            base.Tick();

            // voidmilk flows while the echo digests its guests on a holding platform
            if (contained.Count > 0 && pawn != null && !pawn.Dead &&
                pawn.holdingOwner != null && pawn.IsHashIntervalTick(25000))
            {
                Map map = pawn.Map;
                if (map != null)
                {
                    Thing milk = ThingMaker.MakeThing(ThingDef.Named("RR_VoidMilk"));
                    milk.stackCount = 2 + contained.Count * 2;
                    GenPlace.TryPlaceThing(milk, pawn.Position, map, ThingPlaceMode.Near);
                    Messages.Message(
                        $"The void echo produces {milk.stackCount} voidmilk.",
                        new LookTargets(pawn),
                        MessageTypeDefOf.PositiveEvent);
                }
            }

            if (pawn == null || pawn.Dead || !pawn.Spawned || !pawn.IsHashIntervalTick(120))
                return;

            if (pawn.CurJobDef?.defName == "RV2_VoreInitAsPredator" || pawn.InMentalState || pawn.Downed)
                return;

            Pawn prey = ChoosePrey();
            if (prey == null || prey.Downed)
                return;

            TryStartRV2OralHold(prey);
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
