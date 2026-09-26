using Verse;

namespace RimRound.Hediffs
{
    /// <summary>
    /// The warm afterglow of voidmilk: a contented mood and a slow, gentle
    /// thickening while it works through the drinker.
    /// </summary>
    public class Hediff_VoidMilkBuzz : Hediff
    {
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            Utilities.HediffUtility.QueueWeightGain(pawn, 4f, 45000);
        }
    }
}
