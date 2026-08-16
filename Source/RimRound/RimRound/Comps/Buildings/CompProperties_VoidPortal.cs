using RimWorld;
using Verse;

namespace RimRound.Comps
{
    public class CompProperties_VoidPortal : CompProperties_Interactable
    {
        public float minWeightToEnter = 0.09f;
        public int mazeDurationTicks = 120000;
        public int respawnIntervalTicks = 60000;
        public int maxMeldBeasts = 4;
        public bool exitPortal = false;

        public CompProperties_VoidPortal()
        {
            compClass = typeof(Comp_VoidPortal);
        }
    }
}
