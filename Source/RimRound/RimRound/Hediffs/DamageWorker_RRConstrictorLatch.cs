using Verse;

namespace RimRound.Hediffs
{
    /// <summary>
    /// Rides on a constrictor's melee attack (gorge or brood) as extra damage: when it lands
    /// on a humanlike, the constrictor wraps itself around them.
    /// </summary>
    public class DamageWorker_RRConstrictorLatch : DamageWorker
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            if (victim is Pawn p && dinfo.Instigator is Pawn beast && beast.TryGetComp<Comps.Comp_RRConstrictorHunt>() != null)
            {
                // already taken: coil in with the one on her instead of fighting over her
                if (p.health.hediffSet.GetFirstHediff<Hediff_RRConstricted>() is Hediff_RRConstricted grip)
                    grip.TryJoin(beast);
                else
                    Hediff_RRConstricted.Latch(p, beast);
            }
            return new DamageResult();
        }
    }
}
