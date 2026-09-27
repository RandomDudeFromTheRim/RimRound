using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using static UnityEngine.Mathf;

namespace RimRound.Hediffs
{
    public class Hediff_MeldAerosol : Hediff
    {
        // Past the "growing" stage the meld feeds itself: no more decay, and it
        // climbs to detonation in about 2 in-game hours (~80 s at normal speed).
        // A light dose below that still fades away.
        const float SelfSustainingSeverity = 0.34f;
        const float SelfGrowthPerInterval = 0.008f;

        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead)
                return;

            if (!pawn.IsHashIntervalTick(60))
                return;

            if (Severity >= 1f)
            {
                TriggerMeldDetonation();
                return;
            }

            if (Severity >= SelfSustainingSeverity)
                Severity += SelfGrowthPerInterval;
            else
                DecaySeverity();

            if (Severity > 0f)
            {
                AddMeldGrowth();
                AddDirectWeightGain();
            }
        }

        void DecaySeverity()
        {
            Severity -= 0.0005f;
            if (Severity < 0f)
                Severity = 0f;
        }

        void AddMeldGrowth()
        {
            float meldAmount = 0.02f + Severity * 0.05f;
            Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth);
            if (existing is Hediff_MeldGrowth meldGrowth)
            {
                meldGrowth.AddContribution(meldAmount, 1.0f);
            }
            else
            {
                Hediff_MeldGrowth meld = (Hediff_MeldGrowth)HediffMaker.MakeHediff(Defs.HediffDefOf.RR_MeldGrowth, pawn);
                meld.AddContribution(meldAmount, 1.0f);
                pawn.health.AddHediff(meld);
            }
        }

        void AddDirectWeightGain()
        {
            var fnd = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fnd == null || fnd.Disabled)
                return;

            float kilos = 0.02f + Severity * 0.1f;
            fnd.activeWeightGainRequests.Enqueue(
                new WeightGainRequest(kilos, Find.TickManager.TicksGame + 5, 0, false));
        }

        void TriggerMeldDetonation()
        {
            Utilities.MeldBurstUtility.BeginBurst(pawn);
        }

        public override string TipStringExtra
        {
            get
            {
                float weightSev = pawn?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight)?.Severity ?? 0.035f;
                float meldSev = pawn?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth)?.Severity ?? 0f;
                float ek = (weightSev / 0.001f) + (meldSev * 10f);
                int blobEstimate = 1 + (int)(ek / 20f);
                blobEstimate = Mathf.Clamp(blobEstimate, 3, 80);

                return $"Meld aerosol infection: {Severity:P1}\nDetonation: ~{blobEstimate} blob walls\nHeavier victims produce more walls";
            }
        }
    }
}
