using System.Linq;
using RimRound.Hediffs;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.Comps
{
    public class CompProperties_RRConstrictorHunt : CompProperties
    {
        public float huntRadius = 40f;

        public CompProperties_RRConstrictorHunt()
        {
            compClass = typeof(Comp_RRConstrictorHunt);
        }
    }

    /// <summary>
    /// Drives the gorge constrictor: it seeks out the nearest hostile humanlike and
    /// crawls onto them to latch on. Unlike vanilla combat AI it goes for downed
    /// pawns too - which includes anyone too big to stand.
    ///
    /// It also carries the constrictor's load: it arrives swollen with slurry, enough
    /// to burst an ordinary pawn, and every wound it takes before it latches spills
    /// some of it. Wear it down enough and it can no longer burst anyone; empty it and
    /// it gives up the hunt.
    /// </summary>
    public class Comp_RRConstrictorHunt : ThingComp
    {
        const int CheckIntervalTicks = 60;
        public static readonly FloatRange ArrivalLoad = new FloatRange(1100f, 1600f);
        public const float FullLoad = 1350f;
        public const float MaxLoad = 1600f;
        const float SpentBelow = 20f;
        // a wound worth this share of its lethal damage spills this share of its arrival load, times this
        const float SpillPerDamageShare = 1.4f;

        public float load = -1f;
        float arrivalLoad = -1f;

        public CompProperties_RRConstrictorHunt Props => (CompProperties_RRConstrictorHunt)props;

        Pawn Beast => (Pawn)parent;

        public bool Spent => load >= 0f && load < SpentBelow;

        /// <summary>How swollen it looks, 0 (spent) to 1 (as full as they come).</summary>
        public float Swell => load < 0f ? FullLoad / MaxLoad : Mathf.Clamp01(load / MaxLoad);

        // Drawn width of the sac, in cells, by the kilos it carries: about as wide as a
        // human pawn carrying that much extra weight is drawn (Obese ~1.7 at 200 kg,
        // Gigantic/Titanic ~4 at 1000-1400 kg), capped so the biggest stay readable.
        public static readonly SimpleCurve WidthByLoad = new SimpleCurve
        {
            new CurvePoint(0f, 0.9f),
            new CurvePoint(200f, 1.7f),
            new CurvePoint(660f, 3.0f),
            new CurvePoint(1000f, 3.9f),
            new CurvePoint(1410f, 4.1f),
            new CurvePoint(1860f, 4.6f),
        };

        public float DrawWidth => WidthByLoad.Evaluate(load < 0f ? FullLoad : load);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (load < 0f)
                load = arrivalLoad = ArrivalLoad.RandomInRange;
            if (arrivalLoad < 0f)
                arrivalLoad = Mathf.Max(load, FullLoad);
        }

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);
            Pawn beast = Beast;
            if (totalDamageDealt <= 0f || load <= 0f || !beast.Spawned)
                return;

            float share = totalDamageDealt / Mathf.Max(1f, beast.health.LethalDamageThreshold);
            float spilt = Mathf.Min(load, arrivalLoad * share * SpillPerDamageShare);
            load -= spilt;

            // it bleeds slurry: sweet muck on the ground and a puff of fattening gas
            FilthMaker.TryMakeFilth(beast.Position, beast.Map, ThingDefOf.Filth_Vomit, spilt > 150f ? 2 : 1);
            beast.Map.GetComponent<MapComp_RRGasGrid>()?.AddGas(beast.Position, RRGasType.fatteningGas, Mathf.RoundToInt(Mathf.Clamp(spilt / 10f, 5f, 60f)));
            if (Spent)
                Messages.Message("The gorge constrictor has spilt everything it was carrying. It shrivels and loses interest in feeding.", beast, MessageTypeDefOf.PositiveEvent, historical: false);
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn beast = Beast;
            if (!beast.Spawned || !beast.IsHashIntervalTick(CheckIntervalTicks))
                return;
            if (beast.Dead || beast.Downed || beast.InMentalState || beast.stances.stunner.Stunned || Spent)
                return;
            if (beast.CurJobDef == Defs.JobDefOf.RR_ConstrictorLatchOn)
                return;

            Pawn prey = beast.Map.mapPawns.AllPawnsSpawned
                .Where(p => IsPrey(beast, p) && p.Position.InHorDistOf(beast.Position, Props.huntRadius))
                .OrderBy(p => p.Position.DistanceToSquared(beast.Position))
                .FirstOrDefault(p => beast.CanReach(p, PathEndMode.Touch, Danger.Deadly));
            if (prey == null)
                return;

            Job job = JobMaker.MakeJob(Defs.JobDefOf.RR_ConstrictorLatchOn, prey);
            job.expiryInterval = 600;
            job.checkOverrideOnExpire = true;
            beast.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        public override string CompInspectStringExtra()
        {
            if (load < 0f)
                return null;
            if (Spent)
                return "Spent: it has nothing left to pump into anyone.";
            return $"Swollen with slurry: {load:0} kg";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref load, "load", -1f);
            Scribe_Values.Look(ref arrivalLoad, "arrivalLoad", -1f);
        }

        public static bool IsPrey(Pawn beast, Pawn p)
        {
            return p != beast && !p.Dead && p.RaceProps.Humanlike && p.HostileTo(beast)
                && !p.health.hediffSet.HasHediff(Defs.HediffDefOf.RR_Constricted);
        }
    }
}
