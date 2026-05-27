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
            DamageResult result = new DamageResult();
            if (victim is Pawn pawn && pawn.RaceProps.Humanlike && !pawn.Dead)
            {
                var existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldAerosol);
                if (existing != null)
                {
                    existing.Severity += 0.15f;
                }
                else
                {
                    var hediff = HediffMaker.MakeHediff(Defs.HediffDefOf.RR_MeldAerosol, pawn);
                    hediff.Severity = 0.15f;
                    pawn.health.AddHediff(hediff);
                }
            }
            return result;
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
