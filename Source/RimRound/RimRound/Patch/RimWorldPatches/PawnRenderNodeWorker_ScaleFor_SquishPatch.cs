using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using RimRound.Rendering;

namespace RimRound.Patch.RimWorldPatches
{
    /// <summary>
    /// Shaves the body node's scale when the pawn is pressed against something. This runs inside
    /// PawnRenderTree.ParallelPreDraw, so it must be pure: read a float, scale a Vector3, return.
    /// Anything that mutates graphics or a collection happens in SquishUtility.Tick instead.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.ScaleFor))]
    public static class PawnRenderNodeWorker_ScaleFor_SquishPatch
    {
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            if (parms.Portrait || parms.Statue || parms.Cache)
                return;

            Pawn pawn = parms.pawn;
            if (pawn == null || !IsBodyNode(node))
                return;

            if (!SquishUtility.TryRead(pawn, out float compression, out int mask))
                return;

            float factor = (mask == 3) ? 1.5f : 1f;      // wedged gets more
            __result.x *= 1f - Mathf.Clamp(SquishUtility.MaxCompression * compression * factor, 0f, 0.55f);
            __result.z *= 1f + SquishUtility.MaxBulge * compression * ((mask == 3) ? 1.4f : 1f);
        }

        internal static bool IsBodyNode(PawnRenderNode node)
        {
            string tag = node?.Props?.tagDef?.defName;
            if (tag != null)
                return tag == "Body";
            return node?.Props?.debugLabel == "Body";
        }
    }

    /// <summary>
    /// Keeps the blocked edge of the body where it is. A pure scale shrinks toward the node centre,
    /// so a pawn against a wall looked like it was being squeezed from both sides at once.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.OffsetFor))]
    public static class PawnRenderNodeWorker_OffsetFor_SquishAnchorPatch
    {
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            if (parms.Portrait || parms.Statue || parms.Cache)
                return;
            if (!PawnRenderNodeWorker_ScaleFor_SquishPatch.IsBodyNode(node))
                return;

            float shift = SquishUtility.AnchorShift(parms.pawn, node?.Props?.drawSize.x ?? 1f);
            if (shift != 0f)
                __result.x += shift;
        }
    }

    /// <summary>Drives the spring on the main thread. This is the only place that dirties graphics.</summary>
    [HarmonyPatch(typeof(Pawn))]
    public static class Pawn_Tick_DriveSquish
    {
        [HarmonyPatch("Tick")]
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            SquishUtility.Tick(__instance);
        }
    }

    public static class DebugActions_Squish
    {
        [DebugAction("RimRound", "Toggle squish on this pawn", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap,
            displayPriority = 100)]
        public static void ToggleSquish(Pawn p)
        {
            SquishUtility.ToggleForced(p);
            Messages.Message(
                $"Squish debug toggled for {p?.LabelShort ?? "pawn"}.",
                MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
