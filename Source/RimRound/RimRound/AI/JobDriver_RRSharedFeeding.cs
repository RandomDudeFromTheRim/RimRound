using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>
    /// A shared feeding session: the initiator walks to a feeding machine with a
    /// partner, both settle in side by side for a while, and both leave heavier,
    /// happier, and considerably closer.
    /// </summary>
    public class JobDriver_RRSharedFeeding : JobDriver
    {
        const int SettleTicks = 120;
        const int DurationTicks = 2400;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 2, -1, null, errorOnFailed)
                && pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !(TargetThingB is Pawn partner) || partner.Dead || partner.InAggroMentalState);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);

            Toil settle = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = SettleTicks,
                initAction = delegate
                {
                    var partner = (Pawn)TargetThingB;
                    if (partner.CurJobDef != Defs.JobDefOf.RR_SharedFeeding &&
                        partner.jobs != null && !partner.Drafted &&
                        partner.CanReserve(job.targetA, 2) && partner.CanReserve(pawn))
                    {
                        Job partnerJob = JobMaker.MakeJob(Defs.JobDefOf.RR_SharedFeeding, job.targetA, pawn);
                        partner.jobs.StartJob(partnerJob, JobCondition.InterruptForced);
                    }
                }
            };
            yield return settle;

            Toil feed = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = DurationTicks,
                handlingFacing = true,
                initAction = delegate
                {
                    pawn.rotationTracker.FaceTarget(TargetThingA);
                },
                tickAction = delegate
                {
                    // eating at the machine, turning every so often to the partner beside them
                    int t = DurationTicks - ticksLeftThisToil;
                    bool glancing = t % 400 >= 340 && TargetThingB.Spawned;
                    if (glancing)
                        pawn.rotationTracker.Face(TargetThingB.DrawPos);
                    else
                        pawn.rotationTracker.FaceTarget(TargetThingA);
                    if (t % 400 == 360)
                        FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.Heart);
                }
            };
            feed.WithEffect(EffecterDefOf.EatMeat, TargetIndex.A);
            feed.PlaySustainerOrSound(() => DefDatabase<SoundDef>.GetNamedSilentFail("Meal_Eat"));
            yield return feed;

            yield return Toils_General.Do(() => Reward(pawn, TargetThingB as Pawn));
        }

        static void Reward(Pawn self, Pawn other)
        {
            if (self == null) return;

            var intimacy = self.IntimacyNeed();
            if (intimacy != null)
                intimacy.CurLevelPercentage = Mathf.Clamp01(intimacy.CurLevelPercentage - 0.3f);

            Utilities.HediffUtility.QueueWeightGain(self, Rand.Range(3f, 6f));

            if (other != null)
                self.needs?.mood?.thoughts.memories.TryGainMemory(Defs.ThoughtDefOf.RR_SharedFeedingThought, other);

            if (other == null || self.thingIDNumber >= other.thingIDNumber)
                return; // one message per session, from whichever pawn is first by ID

            if (PawnUtility.ShouldSendNotificationAbout(self))
                Messages.Message(
                    $"{self.LabelShort} and {other.LabelShort} share a long, lazy feeding session at the machine.",
                    new LookTargets(new Pawn[] { self, other }),
                    MessageTypeDefOf.PositiveEvent);
        }
    }
}
