using RimRound.Hediffs;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>Right-click a pawn a gorge constrictor has wrapped itself around: "Tear the gorge constrictor off X".</summary>
    public class FloatMenuOptionProvider_RRTearOffConstrictor : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool RequiresManipulation => true;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn rescuer = context.FirstSelectedPawn;
            var grip = clickedPawn.health?.hediffSet?.GetFirstHediff<Hediff_RRConstricted>();
            if (grip == null || clickedPawn == rescuer)
                return null;

            string label = $"Tear the gorge constrictor off {clickedPawn.LabelShort} ({JobDriver_RRTearOffConstrictor.TicksToTearOff(rescuer, grip).ToStringSecondsFromTicks()})";
            if (!rescuer.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption(label + ": " + "NoPath".Translate().CapitalizeFirst(), null);

            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, delegate
            {
                Job job = JobMaker.MakeJob(Defs.JobDefOf.RR_TearOffConstrictor, clickedPawn);
                rescuer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }, MenuOptionPriority.RescueOrCapture), rescuer, clickedPawn);
        }
    }
}
