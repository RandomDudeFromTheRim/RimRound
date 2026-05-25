using System.Collections.Generic;
using Verse;

namespace RimRound.Comps
{
    public class CompProperties_GluttoniumRadiation : CompProperties
    {
        public float radius = 4f;
        public float exposurePerTick = 0.005f;
        public int tickInterval = 250;

        public CompProperties_GluttoniumRadiation()
        {
            compClass = typeof(Comp_GluttoniumRadiation);
        }
    }
}
