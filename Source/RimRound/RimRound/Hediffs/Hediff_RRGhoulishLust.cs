using System.Collections.Generic;
using RimRound.AI;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.Hediffs
{
    /// <summary>
    /// Ghoulish lust: a fattened ghoul hungers for more of itself. It grows with the
    /// ghoul's size (Titanic is the top of it) and makes it quicker but softer-hitting.
    /// Now and then a player's ghoul loses itself in a fit of groping at its own bulk,
    /// and a female one lets down voidmilk. Added and kept up by MapComponent_RRGhoulishLust.
    /// </summary>
    public class Hediff_RRGhoulishLust : HediffWithComps
    {
        const int UpdateInterval = 250;
        const int EventInterval = 2500;
        public const float MinLust = 0.12f;

        // mean days between fits and letdowns, per stage
        static readonly float[] FitMtbDays = { 3f, 1.5f, 0.7f };
        static readonly float[] MilkMtbDays = { 2f, 1f, 0.5f };
        static readonly int[] MilkCount = { 1, 2, 4 };

        /// <summary>How strong the lust is for a ghoul this size: 0 lean, 1 at Titanic's top. A gorge gland feeds it.</summary>
        public static float LustFor(Pawn p)
        {
            Hediff weight = Utilities.HediffUtility.WeightHediff(p);
            if (weight == null || !RacialBodyTypeInfoUtility.defaultFemaleSet.TryGetValue(Defs.BodyTypeDefOf.F_090_Titanic, out var titanic))
                return 0f;
            float top = titanic.maxSeverity * RacialBodyTypeInfoUtility.GetBodyTypeWeightRequirementMultiplier(p);
            float lust = top <= 0f ? 0f : weight.Severity / top;
            if (Defs.HediffDefOf.RR_GhoulGorgeGland != null && p.health.hediffSet.HasHediff(Defs.HediffDefOf.RR_GhoulGorgeGland))
                lust *= 1.5f;
            return Mathf.Clamp01(lust);
        }

        public override bool ShouldRemove => base.ShouldRemove || pawn == null || !pawn.IsGhoul;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || !pawn.IsHashIntervalTick(UpdateInterval, delta))
                return;
            Severity = LustFor(pawn);
            if (!pawn.Spawned || Severity < MinLust || !pawn.IsHashIntervalTick(EventInterval, delta))
                return;

            int stage = Mathf.Clamp(CurStageIndex - 1, 0, FitMtbDays.Length - 1);
            if (pawn.gender == Gender.Female && Rand.MTBEventOccurs(MilkMtbDays[stage], GenDate.TicksPerDay, EventInterval))
                LetDown(MilkCount[stage]);
            if (Rand.MTBEventOccurs(FitMtbDays[stage], GenDate.TicksPerDay, EventInterval))
                TryStartFit();
        }

        void LetDown(int count)
        {
            Thing milk = ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidMilk);
            milk.stackCount = count;
            GenPlace.TryPlaceThing(milk, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            for (int i = 0; i < 3; i++)
                CloseContactFlecks.Droplet(pawn.DrawPos + new Vector3(0f, 0f, 0.15f), pawn.Map, new Color(0.78f, 0.62f, 0.9f));
            if (pawn.Faction == Faction.OfPlayer)
                Messages.Message($"{pawn.LabelShort}'s swollen body lets down {count} voidmilk on its own.", new LookTargets(milk), MessageTypeDefOf.PositiveEvent, historical: false);
        }

        /// <summary>A player's ghoul, idle enough and with no threat about, loses itself groping at its own bulk.</summary>
        public bool TryStartFit()
        {
            if (pawn.Faction != Faction.OfPlayer || pawn.Downed || pawn.Drafted || pawn.InMentalState || pawn.jobs == null)
                return false;
            if (pawn.CurJob != null && (pawn.CurJob.playerForced || pawn.CurJobDef == Defs.JobDefOf.RR_GhoulSelfFondle))
                return false;
            if (pawn.health.hediffSet.HasHediff(Defs.HediffDefOf.RR_Constricted) || GenHostility.AnyHostileActiveThreatToPlayer(pawn.Map))
                return false;

            Job job = JobMaker.MakeJob(Defs.JobDefOf.RR_GhoulSelfFondle);
            job.expiryInterval = Mathf.RoundToInt(Rand.Range(900f, 1500f) * (0.7f + Severity));
            pawn.jobs.StartJob(job, JobCondition.InterruptForced, cancelBusyStances: true);
            Messages.Message($"{pawn.LabelShort} is overcome by ghoulish lust and starts groping at {pawn.Possessive()} own swollen body.", new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, historical: false);
            return true;
        }
    }

    /// <summary>Gives fattened ghouls on the map their ghoulish lust.</summary>
    public class MapComponent_RRGhoulishLust : MapComponent
    {
        public MapComponent_RRGhoulishLust(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (!ModsConfig.AnomalyActive || Defs.HediffDefOf.RR_GhoulishLust == null || Find.TickManager.TicksGame % 2500 != 77)
                return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (!p.IsGhoul || p.Dead || p.health.hediffSet.HasHediff(Defs.HediffDefOf.RR_GhoulishLust))
                    continue;
                float lust = Hediff_RRGhoulishLust.LustFor(p);
                if (lust >= Hediff_RRGhoulishLust.MinLust)
                    p.health.AddHediff(Defs.HediffDefOf.RR_GhoulishLust).Severity = lust;
            }
        }
    }

    /// <summary>A ghoul lost in its lust, groping at its own bulk until the fit passes.</summary>
    public class JobDriver_RRGhoulSelfFondle : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil grope = ToilMaker.MakeToil("GhoulSelfFondle");
            grope.defaultCompleteMode = ToilCompleteMode.Delay;
            grope.defaultDuration = job.expiryInterval > 0 ? job.expiryInterval : 1200;
            grope.handlingFacing = true;
            grope.tickIntervalAction = delta =>
            {
                if (!pawn.IsHashIntervalTick(80, delta))
                    return;
                // squirming about, turning this way and that
                if (Rand.Chance(0.35f))
                    pawn.Rotation = Rot4.Random;
                Map map = pawn.Map;
                float roll = Rand.Value;
                if (roll < 0.35f)
                    FleckMaker.ThrowMetaIcon(pawn.Position, map, FleckDefOf.Heart);
                else if (roll < 0.7f)
                    CloseContactFlecks.Droplet(pawn.DrawPos + new Vector3(Rand.Range(-0.25f, 0.25f), 0f, Rand.Range(-0.1f, 0.2f)), map, Color.white);
                else
                    FleckMaker.ThrowDustPuff(pawn.DrawPos, map, 0.5f);
            };
            yield return grope;
        }
    }
}
