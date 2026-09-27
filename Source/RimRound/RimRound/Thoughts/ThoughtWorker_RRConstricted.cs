using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Thoughts
{
    /// <summary>
    /// A pawn with a gorge constrictor wrapped around them: horrified, unless they
    /// love growing - then it's the best thing that has ever happened to them.
    /// Stage 0 horrified, 1 uneasy but curious (neutral-plus), 2 thrilled (Like and up).
    /// </summary>
    public class ThoughtWorker_RRConstricted : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            var grip = p.health.hediffSet.GetFirstHediff<Hediffs.Hediff_RRConstricted>();
            if (grip == null)
                return ThoughtState.Inactive;

            // a bound constrictor grown with euphoric mucus leaves no room for anything but bliss
            if (grip.bound != null && grip.bound.Euphoric)
                return ThoughtState.ActiveAtStage(2);

            WeightOpinion opinion = p.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.Neutral;
            if (opinion >= WeightOpinion.Like)
                return ThoughtState.ActiveAtStage(2);
            if (opinion >= WeightOpinion.NeutralPlus)
                return ThoughtState.ActiveAtStage(1);
            return ThoughtState.ActiveAtStage(0);
        }
    }
}
