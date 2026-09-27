using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// A holding platform picks its held entity's animation (a wiggle, or a lunge
    /// toward colonists) every time it updates. A void echo with swallowed
    /// colonists inside plays its gorged belly heave instead.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Building_HoldingPlatform), "UpdateAnimation")]
    public static class BuildingHoldingPlatform_UpdateAnimation_EchoGorged
    {
        static void Postfix(Building_HoldingPlatform __instance)
        {
            var vigor = VoidMazeUtility.VoidEchoVigor(__instance.HeldPawn);
            if (vigor != null && vigor.ContainedCount > 0)
                vigor.UpdateGorgedAnimation();
        }
    }
}
