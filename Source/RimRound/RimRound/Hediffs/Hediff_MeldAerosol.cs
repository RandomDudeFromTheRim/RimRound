using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimRound.Hediffs
{
    public class Hediff_MeldAerosol : Hediff
    {
        int tickCounter = 0;

        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead)
                return;

            if (!pawn.IsHashIntervalTick(60))
                return;

            tickCounter++;

            AddSeverityProgression();

            AddMeldGrowth();

            AddDirectWeightGain();

            if (Severity >= 1f)
                TransformToFleshbeast();
        }

        void AddSeverityProgression()
        {
            Severity += 0.0005f;
            if (Severity > 1f)
                Severity = 1f;
        }

        void AddMeldGrowth()
        {
            float meldAmount = 0.05f + Severity * 0.15f;
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

            float kilos = (0.05f + Severity * 0.3f);
            fnd.activeWeightGainRequests.Enqueue(
                new WeightGainRequest(kilos, Find.TickManager.TicksGame + 5, 0, false));
        }

        void TransformToFleshbeast()
        {
            if (pawn == null || pawn.Dead || pawn.Map == null)
                return;

            if (!ModsConfig.AnomalyActive)
                return;

            var naturalWeight = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
            var meld = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth);

            float weightSev = naturalWeight?.Severity ?? 0.035f;
            float meldSev = meld?.Severity ?? 0f;
            float totalMass = weightSev + meldSev;

            PawnKindDef kind = ChooseFleshbeast(totalMass);
            if (kind == null)
            {
                Severity = 0.5f;
                return;
            }

            string label = kind.label;
            Messages.Message(
                $"{pawn.LabelShort}'s body contorts and rips apart as a {label} emerges from the writhing mass!",
                new LookTargets(pawn),
                MessageTypeDefOf.ThreatBig);

            Pawn beast = PawnGenerator.GeneratePawn(kind, Faction.OfEntities);
            GenSpawn.Spawn(beast, pawn.Position, pawn.Map);

            if (!pawn.Dead)
                pawn.Kill(null);
        }

        static PawnKindDef ChooseFleshbeast(float totalMass)
        {
            if (totalMass < 0.1f)
                return PawnKindDef.Named("Fingerspike");
            else if (totalMass < 0.5f)
                return Rand.Bool ? PawnKindDef.Named("Toughspike") : PawnKindDef.Named("Trispike");
            else if (totalMass < 2.0f)
                return PawnKindDef.Named("Bulbfreak");
            else
                return PawnKindDef.Named("Dreadmeld");
        }

        public override string TipStringExtra
        {
            get
            {
                var naturalWeight = pawn?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
                var meld = pawn?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth);
                float weightSev = naturalWeight?.Severity ?? 0.035f;
                float meldSev = meld?.Severity ?? 0f;
                float totalMass = weightSev + meldSev;

                string outcome = totalMass switch
                {
                    < 0.1f => "Will become: Fingerspike",
                    < 0.5f => "Will become: Toughspike or Trispike",
                    < 2.0f => "Will become: Bulbfreak",
                    _ => "WILL BECOME: DREADMELD!"
                };

                float kgPerCycle = 0.05f + Severity * 0.3f;
                return $"Meld aerosol progress: {Severity:P0}\nWeight gain: +{kgPerCycle:F1} kg/cycle\nPredicted outcome: {outcome}";
            }
        }
    }
}
