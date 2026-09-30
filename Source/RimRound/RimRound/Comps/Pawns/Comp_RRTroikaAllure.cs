using System.Collections.Generic;
using System.Linq;
using RimRound.Hediffs;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.Comps
{
    public class CompProperties_RRTroikaAllure : CompProperties
    {
        public float radius = 18f;
        /// <summary>Chance per check that someone in range is drawn to it, before sensitivity and taste.</summary>
        public float chancePerCheck = 0.15f;

        public CompProperties_RRTroikaAllure() => compClass = typeof(Comp_RRTroikaAllure);
    }

    /// <summary>
    /// A constrictor troika's allure: it doesn't need to chase anyone. Every so often
    /// someone who can see it feels its warm, heavy pull and walks right up to it - more
    /// often the more psychically sensitive they are, and the more they like size.
    /// Drafting them snaps them out of it.
    /// </summary>
    public class Comp_RRTroikaAllure : ThingComp
    {
        const int CheckInterval = 250;

        CompProperties_RRTroikaAllure Props => (CompProperties_RRTroikaAllure)props;
        Pawn Troika => parent as Pawn;

        public override void CompTick()
        {
            base.CompTick();
            Pawn troika = Troika;
            if (troika == null || !troika.Spawned || !troika.IsHashIntervalTick(CheckInterval) || troika.Downed || troika.Dead)
                return;
            if (troika.TryGetComp<Comp_RRConstrictorHunt>()?.Spent == true)
                return;
            Map map = troika.Map;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (!CanBeDrawn(troika, p))
                    continue;
                float chance = Props.chancePerCheck * p.GetStatValue(StatDefOf.PsychicSensitivity) * TasteFactor(p);
                if (!Rand.Chance(chance))
                    continue;
                Job job = JobMaker.MakeJob(Defs.JobDefOf.RR_DrawnToTroika, troika);
                job.expiryInterval = 1200;
                p.jobs.StartJob(job, JobCondition.InterruptForced);
                FleckMaker.ThrowMetaIcon(p.Position, map, FleckDefOf.Heart);
                if (p.Faction == Faction.OfPlayer)
                    Messages.Message($"{p.LabelShort} feels the troika's warm, heavy pull and starts walking toward it. Draft {p.ProObj()} to snap {p.ProObj()} out of it.",
                        new LookTargets(p), MessageTypeDefOf.ThreatSmall);
            }
        }

        bool CanBeDrawn(Pawn troika, Pawn p)
        {
            if (p == troika || p.Dead || p.Downed || !p.RaceProps.Humanlike || !p.Awake() || p.InMentalState || p.jobs == null)
                return false;
            if (p.Drafted || !p.HostileTo(troika) || !p.DevelopmentalStage.Adult())
                return false;
            if (p.CurJobDef == Defs.JobDefOf.RR_DrawnToTroika || p.health.hediffSet.HasHediff(Defs.HediffDefOf.RR_Constricted))
                return false;
            if (!p.Position.InHorDistOf(troika.Position, Props.radius) || !GenSight.LineOfSight(p.Position, troika.Position, troika.Map))
                return false;
            return true;
        }

        /// <summary>Those who love size go gladly; those who hate it mostly resist.</summary>
        static float TasteFactor(Pawn p)
        {
            WeightOpinion o = p.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.Neutral;
            if (o >= WeightOpinion.Love)
                return 2.5f;
            if (o > WeightOpinion.Neutral)
                return 1.6f;
            if (o < WeightOpinion.Neutral)
                return 0.35f;
            return 1f;
        }
    }
}

namespace RimRound.AI
{
    /// <summary>Drawn in by a troika's allure: walk up to it and wait there, dazed, until it takes them.</summary>
    public class JobDriver_RRDrawnToTroika : JobDriver
    {
        Pawn Troika => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Troika.Dead || Troika.Downed || pawn.Drafted);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil wait = Toils_General.Wait(900, TargetIndex.A);
            wait.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Troika);
                if (pawn.IsHashIntervalTick(120))
                    FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.Heart);
            };
            yield return wait;
        }
    }
}
