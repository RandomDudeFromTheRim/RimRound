using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimRound.Projectiles
{
    /// <summary>
    /// A glob of gut-bile spat by a bloat spitter. It does no damage; it bursts
    /// into a thin cloud of bloatgas (fattening gas) where it lands.
    /// </summary>
    public class Projectile_RRBileGlob : Projectile
    {
        const float CloudRadius = 2.4f;
        const int GasAtCenter = 60;

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            IntVec3 center = Position;
            base.Impact(hitThing, blockedByShield); // destroys the glob

            var gasGrid = map.GetComponent<MapComp_RRGasGrid>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, CloudRadius, useCenter: true))
            {
                if (!cell.InBounds(map) || !cell.Walkable(map))
                    continue;
                float falloff = 1f - 0.5f * cell.DistanceTo(center) / CloudRadius;
                gasGrid?.AddGas(cell, RRGasType.fatteningGas, Mathf.RoundToInt(GasAtCenter * falloff));
            }

            FilthMaker.TryMakeFilth(center, map, ThingDefOf.Filth_Vomit, 2);
            FleckMaker.ThrowSmoke(center.ToVector3Shifted(), map, 1.5f);
            def.projectile.soundExplode?.PlayOneShot(new TargetInfo(center, map));
        }
    }
}
