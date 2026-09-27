using RimRound.Hediffs;
using System.Linq;
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

        /// <summary>The way home out of a void maze map, or null on any other map.</summary>
        public static Building ReturnPortalOn(Map map)
        {
            return map?.listerThings.ThingsOfDef(Defs.ThingDefOf.RR_VoidPortalReturn).FirstOrDefault() as Building;
        }

        /// <summary>The void vigor hediff if this pawn is a void echo, otherwise null.</summary>
        public static Hediff_VoidEchoVigor VoidEchoVigor(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_VoidEchoVigor) as Hediff_VoidEchoVigor;
        }
    }
}
