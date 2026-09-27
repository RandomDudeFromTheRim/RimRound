using Verse;

namespace RimRound.Hediffs
{
    /// <summary>
    /// Rides on the gorge constrictor's melee attack as extra damage: when it lands
    /// on a humanlike, the constrictor wraps itself around them.
    /// </summary>
    public class DamageWorker_RRConstrictorLatch : DamageWorker
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            if (victim is Pawn p && dinfo.Instigator is Pawn beast && beast.def.defName == "RR_GorgeConstrictor")
                Hediff_RRConstricted.Latch(p, beast);
            return new DamageResult();
        }
    }
}
