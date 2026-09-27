using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>
    /// Right-click a downed pawn inside the void maze: "Carry X home through the
    /// void portal". The only way to bring a void echo out to a holding platform,
    /// and a way to rescue a colonist who went down in there.
    /// </summary>
    public class FloatMenuOptionProvider_RRCarryThroughVoidPortal : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool RequiresManipulation => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return VoidMazeUtility.ReturnPortalOn(context.FirstSelectedPawn?.Map) != null;
        }

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn carrier = context.FirstSelectedPawn;
            if (clickedPawn == carrier || !clickedPawn.Spawned || !clickedPawn.Downed)
                return null;

            Building portal = VoidMazeUtility.ReturnPortalOn(carrier.Map);
            string label = $"Carry {clickedPawn.LabelShort} home through the void portal";

            if (!carrier.CanReserveAndReach(clickedPawn, PathEndMode.OnCell, Danger.Deadly, 1, -1, null, ignoreOtherReservations: true)
                || !carrier.CanReach(portal, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption(label + ": " + "NoPath".Translate().CapitalizeFirst(), null);

            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, delegate
            {
                Job job = JobMaker.MakeJob(Defs.JobDefOf.RR_CarryThroughVoidPortal, clickedPawn, portal);
                job.count = 1;
                carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }), carrier, clickedPawn);
        }
    }
}
