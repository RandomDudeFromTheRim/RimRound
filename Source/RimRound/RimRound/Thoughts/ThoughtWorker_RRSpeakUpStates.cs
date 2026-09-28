using RimRound.Comps;
using RimRound.Incidents;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Thoughts
{
    /// <summary>
    /// A wild gorge constrictor still holds enough to burst them, and they are nearly
    /// there. Dread, for most; for those who love growing, a terrible thrill.
    /// Stage 0 dread, 1 thrill (Love and up).
    /// </summary>
    public class ThoughtWorker_RRConstrictedNearBurst : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            var grip = p.health.hediffSet.GetFirstHediff<Hediffs.Hediff_RRConstricted>();
            if (grip == null || !grip.WillBurstSoon)
                return ThoughtState.Inactive;
            WeightOpinion o = p.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.Neutral;
            return ThoughtState.ActiveAtStage(o >= WeightOpinion.Love ? 1 : 0);
        }
    }

    /// <summary>
    /// A guest here on a fattening commission. Stage 0 still working toward the size
    /// their people asked for, 1 already there.
    /// </summary>
    public class ThoughtWorker_RRCommissionGuest : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!p.IsQuestLodger())
                return ThoughtState.Inactive;
            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.State != QuestState.Ongoing)
                    continue;
                foreach (QuestPart part in q.PartsListForReading)
                {
                    if (part is QuestPart_RRWeightGoalProgress goal && goal.pawn == p)
                    {
                        BodyTypeDef past = DefDatabase<BodyTypeDef>.GetNamedSilentFail(goal.pastBodyTypeDefName);
                        bool reached = past != null && BodyTypeUtility.PawnIsOverWeightThreshold(p, past);
                        return ThoughtState.ActiveAtStage(reached ? 1 : 0);
                    }
                }
            }
            return ThoughtState.Inactive;
        }
    }
}
