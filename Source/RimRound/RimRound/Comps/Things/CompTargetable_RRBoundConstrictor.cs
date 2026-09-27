using RimRound.Hediffs;
using RimWorld;
using Verse;

namespace RimRound.Comps.Things
{
    /// <summary>
    /// Picks who a bound gorge constrictor goes on: any person it could still
    /// grow. The use job (JobDriver_RRApplyBoundConstrictor) carries it over.
    /// </summary>
    public class CompTargetable_RRBoundConstrictor : CompTargetable_SinglePawn
    {
        protected override TargetingParameters GetTargetingParameters()
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetAnimals = false,
                canTargetMechs = false,
                canTargetSelf = true,
            };
        }

        public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if (!base.ValidateTarget(target, showMessages) || !(target.Thing is Pawn p))
                return false;
            string reason = Hediff_RRConstricted.CannotLatchLeashedReason(p, parent.TryGetComp<RimRound.Things.CompRRBoundConstrictor>()?.data);
            if (reason != null)
            {
                if (showMessages)
                    Messages.Message(reason, p, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            return true;
        }
    }
}
