using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.AI
{
    /// <summary>WeightOpinion.Extreme: a mime that has stopped hiding (a concealed one feels Fanatical's thought).</summary>
    public class ThoughtWorker_WeightOpinion_Animalistic : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!WeightOpinionUtility.ShouldHaveThisKindOfThought(this, p, WeightOpinion.Extreme))
                return false;

            int index = WeightOpinionUtility.GetThoughtIndex(p);
            return ThoughtState.ActiveAtStage(index);
        }
    }
}
