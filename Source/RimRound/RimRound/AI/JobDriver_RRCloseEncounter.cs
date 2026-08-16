using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>
    /// The physical layer for RimRound's touch interactions: walk up to the
    /// recipient, pin them in place for a few seconds, and hold each other's
    /// attention. The social/weight effects still fire from the InteractionWorker.
    /// </summary>
    public class JobDriver_RRCloseEncounter : JobDriver
    {
        const int DurationTicks = 360;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !(TargetThingA is Pawn p) || p.Dead || p.InAggroMentalState);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil press = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = DurationTicks,
                initAction = delegate
                {
                    var target = (Pawn)TargetThingA;
                    pawn.rotationTracker.Face(target.DrawPos);
                    target.stances?.stunner?.StunFor(DurationTicks, pawn, addBattleLog: false, showMote: true);
                    FleckMaker.ThrowSmoke(target.DrawPos, target.Map, 1.2f);
                },
                tickAction = delegate
                {
                    var target = TargetThingA as Pawn;
                    if (target != null && target.Spawned)
                        pawn.rotationTracker.Face(target.DrawPos);
                }
            };
            press.AddFailCondition(() => !(((Pawn)TargetThingA)?.Spawned ?? false));
            yield return press;
        }
    }
}
