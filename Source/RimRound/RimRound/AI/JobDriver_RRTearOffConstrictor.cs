using RimRound.Hediffs;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>
    /// Wrestles a gorge constrictor off another pawn. TargetA is the victim. The
    /// fuller the constrictor still is, the longer it takes; melee skill helps.
    /// </summary>
    public class JobDriver_RRTearOffConstrictor : JobDriver
    {
        const int BaseTicks = 300;

        Pawn Victim => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        Hediff_RRConstricted Grip => Victim?.health?.hediffSet?.GetFirstHediff<Hediff_RRConstricted>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        /// <summary>Ticks to tear it off: longer the fuller it still is, shorter for a good brawler.</summary>
        public static int TicksToTearOff(Pawn rescuer, Hediff_RRConstricted grip)
        {
            float melee = rescuer.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 5;
            float skill = Mathf.Lerp(1.4f, 0.6f, melee / 20f);
            return Mathf.RoundToInt(BaseTicks * (1f + 2.5f * (grip?.LoadFraction ?? 0f)) * skill);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Grip == null);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil wrestle = Toils_General.Wait(TicksToTearOff(pawn, Grip), TargetIndex.A)
                .WithProgressBarToilDelay(TargetIndex.A)
                .FailOn(() => Grip == null);
            wrestle.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Victim);
                if (pawn.IsHashIntervalTick(90))
                    FilthMaker.TryMakeFilth(Victim.Position, Victim.Map, ThingDefOf.Filth_Vomit, 1);
            };
            yield return wrestle;

            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = delegate
                {
                    Grip?.Release(pawn);
                    Messages.Message($"{pawn.LabelShort} tears the gorge constrictor off {Victim.LabelShort}!", new LookTargets(Victim), MessageTypeDefOf.PositiveEvent);
                    pawn.skills?.Learn(SkillDefOf.Melee, 200f);
                }
            };
        }
    }
}
