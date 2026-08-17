using RimRound.Hediffs;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>
    /// A colonist climbs onto a holding platform and lets the void echo swallow
    /// them whole. The pawn is contained inside the echo (recoverable), the echo
    /// grows, and voidmilk flows while it digests.
    /// </summary>
    public class JobDriver_RRFeedToEcho : JobDriver
    {
        const int ClimbTicks = 600;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);

            Toil offer = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = ClimbTicks,
                initAction = delegate
                {
                    pawn.rotationTracker.Face(((Building)TargetThingA).DrawPos);
                },
                tickAction = delegate
                {
                    if (pawn.IsHashIntervalTick(150))
                        FleckMaker.ThrowSmoke(pawn.DrawPos, pawn.Map, 0.6f);
                }
            };
            yield return offer;

            Toil swallowed = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = delegate
                {
                    var platform = TargetThingA as Building_HoldingPlatform;
                    var echo = platform?.HeldPawn;
                    var vigor = echo?.health?.hediffSet?.GetFirstHediffOfDef(HediffDef.Named("RR_VoidEchoVigor")) as Hediff_VoidEchoVigor;
                    if (vigor == null)
                        return;

                    vigor.Contain(pawn);

                    var weight = echo.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
                    if (weight != null)
                        weight.Severity = Mathf.Min(weight.Severity + 0.06f, 1.6f);

                    Messages.Message(
                        $"{pawn.LabelShort} steps into the echo's waiting embrace — swallowed whole.",
                        new LookTargets(echo),
                        MessageTypeDefOf.NeutralEvent);
                }
            };
            yield return swallowed;
        }
    }
}
