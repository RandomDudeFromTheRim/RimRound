using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using RimRound.Rendering;

namespace RimRound.Patch.RimWorldPatches
{
    /// <summary>
    /// Hooks the one place the engine asks a node how big to draw itself, and shaves the body in
    /// when it is standing against something. The head, hair and apparel are separate nodes and
    /// are left alone, which is what keeps a squished pawn looking like a squished person rather
    /// than a squashed image.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.ScaleFor))]
    public static class PawnRenderNodeWorker_ScaleFor_SquishPatch
    {
        static readonly HashSet<string> bodyTags = new HashSet<string> { "Body", "Shell", "Head" };

        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            // portraits, statues and cached draws must stay pristine
            if (parms.Portrait || parms.Statue || parms.Cache)
                return;

            Pawn pawn = parms.pawn;
            if (pawn == null)
                return;

            // only distort the body itself
            if (!IsBodyNode(node))
                return;

            if (!SquishUtility.TrySquish(pawn, ref __result, out int state))
                return;

            SquishUtility.NoteState(pawn, state);
        }

        static bool IsBodyNode(PawnRenderNode node)
        {
            string tag = node?.Props?.tagDef?.defName;
            if (tag != null && tag != "Body")
                return false;
            if (tag == "Body")
                return true;
            return node?.Props?.debugLabel == "Body";
        }
    }

    /// <summary>Dev-mode switch so the effect can be inspected without waiting for a heavy pawn.</summary>
    public static class DebugActions_Squish
    {
        [DebugAction("RimRound", "Toggle pawn squish (debug)", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap,
            displayPriority = 100)]
        public static void ToggleSquish(Pawn p)
        {
            SquishUtility.Forced = !SquishUtility.Forced;
            SquishUtility.ClearCaches();
            p?.Drawer?.renderer?.SetAllGraphicsDirty();
            Messages.Message(
                SquishUtility.Forced
                    ? "Squish forced on: every pawn now squishes when standing against something."
                    : "Squish forced off: back to normal severity gating.",
                MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
