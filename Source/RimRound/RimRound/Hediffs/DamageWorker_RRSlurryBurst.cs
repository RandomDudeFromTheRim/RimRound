using System.Collections.Generic;
using RimRound.Comps;
using RimRound.Utilities;
using Verse;

namespace RimRound.Hediffs
{
    /// <summary>
    /// A burst of slurry: harmless in itself, but every cell it reaches fills with fattening gas
    /// (RimRound's gas grid) and gets splashed. The feeding reactor goes up like this.
    /// </summary>
    public class DamageWorker_RRSlurryBurst : DamageWorker
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim) => new DamageResult();

        public override void ExplosionAffectCell(Explosion explosion, IntVec3 cell, List<Thing> damagedThings, List<Thing> ignoredThings, bool canAffectCeller)
        {
            base.ExplosionAffectCell(explosion, cell, damagedThings, ignoredThings, canAffectCeller);
            Map map = explosion.Map;
            if (map == null)
                return;
            map.GetComponent<MapComp_RRGasGrid>()?.AddGas(cell, RRGasType.fatteningGas, 40 + (int)(explosion.radius * 12f));
            if (Rand.Chance(0.3f) && cell.Standable(map))
                RimWorld.FilthMaker.TryMakeFilth(cell, map, RimWorld.ThingDefOf.Filth_Vomit);
        }
    }
}
