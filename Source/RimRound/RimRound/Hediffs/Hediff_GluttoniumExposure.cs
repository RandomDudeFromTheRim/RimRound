using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Hediffs
{
    public class Hediff_GluttoniumExposure : Hediff
    {
        int ticksUntilNextWG = 0;

        public override void Tick()
        {
            base.Tick();

            if (pawn is null || pawn.Dead)
                return;

            ticksUntilNextWG--;
            if (ticksUntilNextWG > 0)
                return;
            ticksUntilNextWG = 250;

            if (this.Severity <= 0)
            {
                pawn.health.RemoveHediff(this);
                return;
            }

            var fndComp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fndComp is null)
                return;

            float kilosToAdd = GetWeightGainPerTick() * this.Severity;
            if (kilosToAdd <= 0)
                return;

            fndComp.activeWeightGainRequests.Enqueue(
                new WeightGainRequest(kilosToAdd, Find.TickManager.TicksGame + 10, 18000, false));

            this.Severity -= 0.001f;
        }

        float GetWeightGainPerTick()
        {
            switch (CurStageIndex)
            {
                case 0: return 0.001f;
                case 1: return 0.005f;
                case 2: return 0.015f;
                case 3: return 0.04f;
                case 4: return 0.1f;
                default: return 0.001f;
            }
        }

        public override string TipStringExtra
        {
            get
            {
                return "Gluttonium exposure building up in the body.\n" +
                       $"Current exposure: {Severity:p1}";
            }
        }
    }
}
