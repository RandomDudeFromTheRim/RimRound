using RimRound.Comps;
using RimRound.Hediffs;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>A gorge constrictor crawls onto its prey (TargetA) and wraps itself around them - downed or not.</summary>
    public class JobDriver_RRConstrictorLatchOn : JobDriver
    {
        Pawn Prey => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !Comp_RRConstrictorHunt.IsPrey(pawn, Prey));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = delegate
                {
                    Hediff_RRConstricted.Latch(Prey, pawn);
                }
            };
        }
    }
}
