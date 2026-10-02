using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.AI
{
    /// <summary>
    /// Lactation Expansion's milk capacity (SEX_LactationCapacity) grows with weight: every
    /// way of getting milk out of her - milking, suckling, a constrictor's pumping - draws on
    /// the one store, so a heavier pawn simply holds and yields more.
    /// </summary>
    public class StatPart_LactationCapacityByWeight : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.Thing is Pawn pawn && pawn.RaceProps.Humanlike)
                val *= RRLactationUtility.CapacityFactor(pawn);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (req.Thing is Pawn pawn && pawn.RaceProps.Humanlike)
            {
                float f = RRLactationUtility.CapacityFactor(pawn);
                if (f != 1f)
                    return $"Body weight: x{f:0.##}";
            }
            return null;
        }
    }
}
