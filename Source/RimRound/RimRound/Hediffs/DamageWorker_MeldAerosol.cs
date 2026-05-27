using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimRound.Hediffs
{
    public class DamageWorker_MeldAerosol : DamageWorker
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            return new DamageResult();
        }

        public override void ExplosionAffectCell(Explosion explosion, IntVec3 cell, List<Thing> damagedThings, List<Thing> ignoredThings, bool canAffectCeller)
        {
            base.ExplosionAffectCell(explosion, cell, damagedThings, ignoredThings, canAffectCeller);

            Map map = explosion.Map;
            if (map == null)
                return;

            int gasAmount = 30 + (int)(explosion.radius * 15f);
            map.GetComponent<MapComp_RRGasGrid>()?.AddGas(cell, RRGasType.meldGas, gasAmount);
        }

    }
}
