using RimRound.Hediffs;
using Verse;

namespace RimRound.Utilities
{
    public static class VoidMazeUtility
    {
        /// <summary>
        /// RR_VoidWarmth is applied on entering the void maze and stripped on the way
        /// out, so it doubles as the "currently inside the maze" marker.
        /// </summary>
        public static bool IsInVoidMaze(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_VoidWarmth) != null;
        }

        /// <summary>The void vigor hediff if this pawn is a void echo, otherwise null.</summary>
        public static Hediff_VoidEchoVigor VoidEchoVigor(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_VoidEchoVigor) as Hediff_VoidEchoVigor;
        }
    }
}
