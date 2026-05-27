using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRoundExtraEvents.ThingComps
{
    public class CompProperties_ExplosiveAddRRGas : CompProperties_Explosive
    {
        public RRGasType gasType = RRGasType.enbiggenerGas;
        public int gasAmount = 255;

        public CompProperties_ExplosiveAddRRGas()
        {
            compClass = typeof(CompExplosive_AddRRGas);
        }
    }

    public class CompExplosive_AddRRGas : CompExplosive
    {
        protected new CompProperties_ExplosiveAddRRGas Props =>
            (CompProperties_ExplosiveAddRRGas)props;

        private bool exploded;

        public override void CompTick()
        {
            bool wasWickStarted = wickStarted;

            base.CompTick();

            // Detect detonation
            if (wasWickStarted && !wickStarted && !exploded)
            {
                exploded = true;
                ReleaseGas();
            }
        }

        private void ReleaseGas()
        {
            Map map = parent.MapHeld;
            if (map == null)
                return;

            MapComp_RRGasGrid grid = map.GetComponent<MapComp_RRGasGrid>();
            if (grid == null)
                return;

            IntVec3 pos = parent.PositionHeld;
            float radius = Props.explosiveRadius;

            int numCells = GenRadial.NumCellsInRadius(radius);

            for (int i = 0; i < numCells; i++)
            {
                IntVec3 cell = pos + GenRadial.RadialPattern[i];

                if (cell.InBounds(map))
                {
                    grid.AddGas(cell, Props.gasType, Props.gasAmount, true);
                }
            }
        }
    }
}