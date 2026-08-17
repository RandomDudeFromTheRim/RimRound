using RimRound.Comps;
using RimRound.Utilities;
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
            var fnd = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fnd != null && !fnd.Disabled)
                fnd.activeWeightGainRequests.Enqueue(
                    new WeightGainRequest(4f, Find.TickManager.TicksGame + 5, 45000, false));
        }
    }
}
