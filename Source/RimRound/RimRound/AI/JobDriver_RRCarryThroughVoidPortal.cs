using RimRound.Comps;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>
    /// Picks up a downed pawn in the void maze (a void echo, or a fallen colonist),
    /// carries it to the return portal, and brings it home. TargetA is the downed
    /// pawn, TargetB the return portal.
    /// </summary>
    public class JobDriver_RRCarryThroughVoidPortal : JobDriver
    {
        Pawn Carried => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDespawnedOrNull(TargetIndex.B);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOn(() => !Carried.Downed)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = delegate
                {
                    job.GetTarget(TargetIndex.B).Thing.TryGetComp<Comp_VoidPortal>()?.CarryHome(pawn, Carried);
                }
            };
        }
    }
}
