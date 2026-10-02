using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Hediffs
{
    /// <summary>
    /// Stretched past what the body can hold: the strain of it (severity 0..1). Two things
    /// raise it. A stomach stuffed past its hard limit strains until it may rupture (see
    /// FullnessAndDietStats_ThingComp.StomachStrainCheckTick). And a constrictor that
    /// keeps pumping someone past their burst point doesn't burst them outright - each pump
    /// adds strain and may be the one that does it, likelier and likelier as the strain
    /// builds (certain at full strain, about one Gelatinous tier past the point). If it stops
    /// first - torn off, or run dry - the strain fades over a few days; they keep everything
    /// they were given, and their body is stretched for good (RR_StretchedBeyond), which moves
    /// their burst point on by a tier each time.
    /// </summary>
    public class Hediff_RROverfilled : HediffWithComps
    {
        /// <summary>Kilos past the burst point that bring a pawn of ordinary resilience to full strain.</summary>
        const float KilosToFullStrain = 600f;
        public const int MaxStretched = 3;

        float peak;
        /// <summary>Strained by a constrictor's pumping (not just a stuffed stomach): only that stretches the burst point.</summary>
        bool pumped;
        /// <summary>Ended by a rupture or a burst rather than easing off: no stretching for that.</summary>
        bool gaveWay;

        static Hediff_RROverfilled GetOrAdd(Pawn p)
        {
            var h = p.health.hediffSet.GetFirstHediffOfDef(Defs.HediffDefOf.RR_Overfilled) as Hediff_RROverfilled;
            if (h == null)
            {
                h = (Hediff_RROverfilled)HediffMaker.MakeHediff(Defs.HediffDefOf.RR_Overfilled, p);
                h.Severity = 0.001f;
                p.health.AddHediff(h);
            }
            return h;
        }

        /// <summary>Adds a pump's worth of strain; returns the strain after it.</summary>
        public static float Strain(Pawn p, float kilos, bool troika)
        {
            var h = GetOrAdd(p);
            h.pumped = true;
            h.Severity = Mathf.Min(1f, h.Severity + kilos / (KilosToFullStrain * Resilience(p, troika)));
            h.peak = Mathf.Max(h.peak, h.Severity);
            return h.Severity;
        }

        /// <summary>
        /// Ticks a stomach this far past its hard limit (as a fraction of it) takes to reach
        /// full strain, for a pawn of ordinary resilience: ten percent over takes about four
        /// hours, thirty percent over a little more than one.
        /// </summary>
        const float StomachTicksToFullStrainAtTenPercent = 10000f;

        /// <summary>Strain for a stomach <paramref name="over"/> past its hard limit, for <paramref name="ticks"/> ticks; returns the strain after it.</summary>
        public static float StrainStomach(Pawn p, float over, int ticks)
        {
            var h = GetOrAdd(p);
            h.Severity = Mathf.Min(1f, h.Severity + over * 10f * ticks / (StomachTicksToFullStrainAtTenPercent * Resilience(p, false)));
            h.peak = Mathf.Max(h.peak, h.Severity);
            return h.Severity;
        }

        /// <summary>Chance a straining stomach ruptures in a check covering <paramref name="ticks"/> ticks: rare at first, steeply likelier with strain.</summary>
        public static float RuptureChancePerCheck(float strain, int ticks) => Mathf.Clamp01(0.03f * strain * strain * strain * ticks / 150f);

        /// <summary>It gave way (a rupture, a burst): the strain is spent, with nothing stretched for it.</summary>
        public static void Release(Pawn p)
        {
            if (p?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_Overfilled) is Hediff_RROverfilled h)
            {
                h.gaveWay = true;
                p.health.RemoveHediff(h);
            }
        }

        /// <summary>
        /// A pump past the burst point may burst them: rare at first, steeply likelier with strain.
        /// Scaled by what the pump put in, so the risk per kilo is the same however it comes -
        /// a troika's big pumps are no safer than a brood's small ones.
        /// </summary>
        public static float BurstChancePerPump(float strain, float kilos) => Mathf.Clamp01(0.6f * strain * strain * strain * kilos / 22.5f);

        /// <summary>
        /// How much a body can take past its limit: those who love size give in to it, the
        /// tough hold together, a body already stretched once gives more easily; a troika's
        /// three proboscises fill faster than anyone can adjust.
        /// </summary>
        static readonly TraitDef tough = DefDatabase<TraitDef>.GetNamedSilentFail("Tough");

        public static float Resilience(Pawn p, bool troika)
        {
            float r = 1f;
            WeightOpinion o = p.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.Neutral;
            if (o >= WeightOpinion.Love)
                r *= 1.5f;
            else if (o > WeightOpinion.Neutral)
                r *= 1.2f;
            else if (o < WeightOpinion.Neutral)
                r *= 0.8f;
            if (tough != null && p.story?.traits?.HasTrait(tough) == true)
                r *= 1.3f;
            r *= 1f + 0.25f * StretchedLevel(p);
            r *= Mathf.Clamp(p.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness), 0.5f, 1.2f);
            if (troika)
                r *= 0.8f;
            return r;
        }

        /// <summary>How many times this pawn has survived being pumped past their burst point (0..3).</summary>
        public static int StretchedLevel(Pawn p)
        {
            Hediff h = p?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_StretchedBeyond);
            return h == null ? 0 : Mathf.Clamp(Mathf.RoundToInt(h.Severity), 0, MaxStretched);
        }

        /// <summary>Still being pumped: the strain doesn't ease until it stops.</summary>
        public override float Severity
        {
            get => base.Severity;
            set
            {
                if (value < base.Severity && pawn?.health?.hediffSet?.GetFirstHediff<Hediff_RRConstricted>() != null)
                    return;
                base.Severity = value;
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            // eased off without bursting: the body stays stretched for good
            if (pawn == null || pawn.Dead || !pumped || gaveWay || peak < 0.2f)
                return;
            Hediff s = pawn.health.hediffSet.GetFirstHediffOfDef(Defs.HediffDefOf.RR_StretchedBeyond);
            if (s == null)
            {
                s = HediffMaker.MakeHediff(Defs.HediffDefOf.RR_StretchedBeyond, pawn);
                s.Severity = 1f;
                pawn.health.AddHediff(s);
            }
            else if (s.Severity < MaxStretched)
            {
                s.Severity += 1f;
            }
            Find.LetterStack.ReceiveLetter("Stretched beyond",
                $"{pawn.LabelShort} was pumped past the point that should have burst {pawn.ProObj()} - and held. The strain has eased, and {pawn.Possessive()} body has stretched to hold everything it was given.\n\n{pawn.ProSubj().CapitalizeFirst()} could take even more before bursting now.",
                LetterDefOf.PositiveEvent, new LookTargets(pawn));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref peak, "peak");
            Scribe_Values.Look(ref pumped, "pumped");
        }
    }
}
