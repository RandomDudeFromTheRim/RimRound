using RimRound.Utilities;
using UnityEngine;
using Verse;

namespace RimRound.Comps
{
    public class CompProperties_GasOnDestroy : CompProperties
    {
        public RRGasType gasType = RRGasType.fatteningGas;
        public float radius = 1.9f;
        public int gasPerCell = 160;

        public CompProperties_GasOnDestroy()
        {
            compClass = typeof(CompGasOnDestroy);
        }
    }

    /// <summary>
    /// Releases a puff of RimRound gas where the thing stood when it's destroyed
    /// or mined - e.g. flesh walls in the void maze bleed bloatgas when cut open.
    /// </summary>
    public class CompGasOnDestroy : ThingComp
    {
        public CompProperties_GasOnDestroy Props => (CompProperties_GasOnDestroy)props;

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            // Vanish covers map teardown and code removal, not a wall being cut open
            if (previousMap == null || mode == DestroyMode.Vanish)
                return;

            var gasGrid = previousMap.GetComponent<MapComp_RRGasGrid>();
            if (gasGrid == null)
                return;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.radius, useCenter: true))
            {
                if (cell.InBounds(previousMap))
                    gasGrid.AddGas(cell, Props.gasType, Mathf.RoundToInt(Props.gasPerCell * (1f - 0.5f * cell.DistanceTo(parent.Position) / Props.radius)));
            }
        }
    }
}
