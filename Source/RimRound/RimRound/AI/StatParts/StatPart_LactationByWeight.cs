using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.AI
{
    /// <summary>
    /// Lactation Expansion's milk grows with weight: how much she holds (SEX_LactationCapacity)
    /// and how much she makes a day (SEX_LactationPerDay), by the same factor - so she still
    /// fills up in about a day, and every milking yields more the bigger she is. (Lactation
    /// Expansion replaces vanilla lactation, so RimRound's vanilla milk patch never reaches it.)
    /// </summary>
    public class StatPart_LactationByWeight : StatPart
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
