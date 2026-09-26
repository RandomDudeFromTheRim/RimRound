using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// Void echoes are humanlike pawns in the entity faction. Anomaly only offers
    /// "Capture" for humanlikes that are mutants allowed on platforms, so without
    /// this an echo could never be taken to a holding platform (and nothing could
    /// be fed to it or milked from it).
    /// </summary>
    [HarmonyPatch(typeof(CompHoldingPlatformTarget), nameof(CompHoldingPlatformTarget.StudiedAtHoldingPlatform), MethodType.Getter)]
    public static class CompHoldingPlatformTarget_StudiedAtHoldingPlatform_VoidEchoes
    {
        static void Postfix(CompHoldingPlatformTarget __instance, ref bool __result)
        {
            if (!__result && __instance.parent is Pawn pawn && VoidMazeUtility.VoidEchoVigor(pawn) != null)
                __result = true;
        }
    }

    /// <summary>
    /// Same rule as vanilla for a downed (or carried) target, minus the monolith
    /// level gate that the Human race's study comp would otherwise apply.
    /// </summary>
    [HarmonyPatch(typeof(CompHoldingPlatformTarget), nameof(CompHoldingPlatformTarget.CanBeCaptured), MethodType.Getter)]
    public static class CompHoldingPlatformTarget_CanBeCaptured_VoidEchoes
    {
        static void Postfix(CompHoldingPlatformTarget __instance, ref bool __result)
        {
            if (__result || !(__instance.parent is Pawn pawn) || VoidMazeUtility.VoidEchoVigor(pawn) == null)
                return;

            __result = pawn.Faction != Faction.OfPlayer &&
                (pawn.Downed || pawn.ParentHolder is Pawn_CarryTracker);
        }
    }
}
