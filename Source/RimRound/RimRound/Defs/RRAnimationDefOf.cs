using RimWorld;
using Verse;

namespace RimRound.Defs
{
    [DefOf]
    public static class RRAnimationDefOf
    {
        public static AnimationDef RR_EchoGorged;
        public static AnimationDef RR_BurstSwell;

        static RRAnimationDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RRAnimationDefOf));
        }
    }
}
