using HarmonyLib;
using RimRound.Hediffs;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// Zoomed out, humanlike pawns are drawn from a cached snapshot that is only
    /// re-baked when something changes (like turning), which froze the gorge
    /// constrictor's pumping between turns. Its victims skip the cache, so the coils
    /// clench on every pump at any zoom. Runs on the parallel pre-render: read-only.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderer), "ParallelGetPreRenderResults")]
    public static class PawnRenderer_ParallelGetPreRenderResults_ConstrictorNoCache
    {
        public static void Prefix(Pawn ___pawn, ref bool disableCache)
        {
            if (!disableCache && Hediff_RRConstricted.Victims.Count > 0 && Hediff_RRConstricted.Victims.Contains(___pawn))
                disableCache = true;
        }
    }
}
