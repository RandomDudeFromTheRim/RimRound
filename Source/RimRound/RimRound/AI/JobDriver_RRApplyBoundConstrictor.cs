using System.Collections.Generic;
using RimRound.Hediffs;
using RimRound.Things;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>
    /// Carries a bound gorge constrictor to someone and sets it on them. TargetA is
    /// the constrictor item, or the constrictor vat holding it; TargetB is the pawn
    /// (invalid when the user targets themself).
    /// </summary>
    public class JobDriver_RRApplyBoundConstrictor : JobDriver
    {
        const int ApplyTicks = 120;

        Pawn Target => job.targetB.IsValid ? (Pawn)job.GetTarget(TargetIndex.B).Thing : pawn;

        Building_RRConstrictorVat Vat => job.GetTarget(TargetIndex.A).Thing as Building_RRConstrictorVat;

        /// <summary>The constrictor's state, wherever it is right now: in the vat, on the floor or in hand.</summary>
        BoundConstrictorData Data
        {
            get
            {
                Thing item = pawn.carryTracker.CarriedThing;
                if (item?.def.defName != "RR_BoundConstrictor")
                    item = Vat != null ? Vat.Constrictor : job.GetTarget(TargetIndex.A).Thing;
                return item?.TryGetComp<CompRRBoundConstrictor>()?.data;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
                return false;
            return !job.targetB.IsValid || pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnIncapable(PawnCapacityDefOf.Manipulation);
            this.FailOn(() => Target == null || Target.Dead || Hediff_RRConstricted.CannotLatchLeashedReason(Target, Data) != null);
            if (job.targetB.IsValid)
                this.FailOnDespawnedOrNull(TargetIndex.B);

            if (Vat != null)
            {
                // lift it out of the vat
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell)
                    .FailOnDespawnedOrNull(TargetIndex.A)
                    .FailOn(() => Vat.Constrictor == null);
                yield return Toils_General.Wait(60, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
                yield return new Toil
                {
                    defaultCompleteMode = ToilCompleteMode.Instant,
                    initAction = delegate
                    {
                        if (!Vat.TryTakeConstrictor(pawn))
                            EndJobWith(JobCondition.Incompletable);
                    }
                };
            }
            else
            {
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                    .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
                yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            }

            if (job.targetB.IsValid)
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

            Toil apply = Toils_General.Wait(ApplyTicks);
            if (job.targetB.IsValid)
                apply.WithProgressBarToilDelay(TargetIndex.B);
            apply.handlingFacing = true;
            apply.tickAction = delegate
            {
                if (Target != pawn)
                    pawn.rotationTracker.FaceTarget(Target);
            };
            yield return apply;

            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = delegate
                {
                    Thing carried = pawn.carryTracker.CarriedThing;
                    if (carried == null || carried.def.defName != "RR_BoundConstrictor")
                        return;
                    BoundConstrictorData data = carried.TryGetComp<CompRRBoundConstrictor>()?.data;
                    carried.SplitOff(1).Destroy();
                    Hediff_RRConstricted.LatchLeashed(Target, pawn, data);
                }
            };
        }
    }
}
