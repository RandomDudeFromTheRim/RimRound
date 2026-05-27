using RimRound.Comps;
using RimRound.Utilities;
using Verse;

namespace RimRoundExtraEvents.Projectiles
{
    public class Projectile_Explosive_AddRRGas : Projectile_Explosive
    {
        public RRGasType gasType = RRGasType.enbiggenerGas;
        public int gasAmount = 255;

        protected override void Explode()
        {
            base.Explode();

            Map map = this.Map;
            IntVec3 pos = this.Position;
            if (map == null || !pos.InBounds(map))
                return;

            MapComp_RRGasGrid grid = map.GetComponent<MapComp_RRGasGrid>();
            if (grid == null)
                return;

            float radius = this.def.projectile?.explosionRadius ?? 7.2f;
            int numCells = GenRadial.NumCellsInRadius(radius);
            for (int i = 0; i < numCells; i++)
            {
                IntVec3 cell = pos + GenRadial.RadialPattern[i];
                if (cell.InBounds(map))
                    grid.AddGas(cell, gasType, gasAmount, true);
            }
        }
    }
}
