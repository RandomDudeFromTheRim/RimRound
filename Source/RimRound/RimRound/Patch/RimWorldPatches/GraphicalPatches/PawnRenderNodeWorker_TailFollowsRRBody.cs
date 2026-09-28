using System.Collections.Concurrent;
using AlienRace;
using HarmonyLib;
using RimRound.Rendering;
using RimRound.Utilities;
using UnityEngine;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// Gene tails (Biotech's, Erin's Experiments' and the like) are placed at fixed
    /// offsets tuned for a vanilla body, so on a big RimRound body they end up buried
    /// inside it - and a tail drawn on the body mesh (no overrideMeshSize) also grows
    /// one-for-one with the body, as HAR scales that mesh. On a RimRound body the tail
    /// keeps its height against the body's centre of mass, as on a vanilla body, and
    /// is pushed straight out to this body's outline at that height: out of the rump
    /// from the side, peeking out beside the hips from the front, and over the rump
    /// from behind. It grows only with the square root of the body's size.
    /// </summary>
    static class TailOnRRBody
    {
        static readonly ConcurrentDictionary<GeneDef, bool> tailGenes = new ConcurrentDictionary<GeneDef, bool>();

        public static bool Applies(PawnRenderNode node, PawnDrawParms parms, out float meshSize)
        {
            meshSize = 1f;
            GeneDef gene = node.gene?.def;
            if (gene == null || !tailGenes.GetOrAdd(gene, g => g.exclusionTags != null && g.exclusionTags.Contains("Tail")))
                return false;
            Pawn pawn = parms.pawn;
            if (pawn?.story == null || !BodyTypeUtility.HasCustomBody(pawn))
                return false;
            meshSize = pawn.GetComp<AlienPartGenerator.AlienComp>()?.customDrawSize.x ?? 1f;
            return meshSize > 1.01f;
        }

        /// <summary>How much bigger than on a vanilla body the tail is drawn.</summary>
        public static float Growth(float meshSize) => Mathf.Sqrt(meshSize);
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.OffsetFor))]
    static class PawnRenderNodeWorker_OffsetFor_TailFollowsRRBody
    {
        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            if (!TailOnRRBody.Applies(node, parms, out float mesh))
                return;
            Pawn pawn = parms.pawn;
            string vanillaPath = (pawn.gender == Gender.Female ? RimWorld.BodyTypeDefOf.Female : RimWorld.BodyTypeDefOf.Male).bodyNakedGraphicPath;
            string bodyPath = pawn.Drawer?.renderer?.BodyGraphic?.path;
            Rot4 rot = parms.facing;
            if (!BodySilhouettes.TryGet(vanillaPath, rot, out var vanilla)
                | !BodySilhouettes.TryGet(bodyPath, rot, out var body))
                return;

            const float VanillaCanvas = 1.5f;
            float canvas = VanillaCanvas * mesh;
            Vector2 o = new Vector2(__result.x, __result.z);

            // the same height against the body's middle as on a vanilla body - except
            // from the front, where it peeks out beside the hips rather than from under them
            float dz = rot == Rot4.South ? 0f : o.y - vanilla.CentroidZ(VanillaCanvas);
            float z = body.CentroidZ(canvas) + dz;
            if (rot == Rot4.North)
            {
                // facing away: it lies over the rump
                __result.z = z;
                return;
            }

            // straight out sideways to the outline at that height; whatever part of the
            // offset reached past the vanilla outline still sticks out past this one
            float side = o.x < 0f ? -1f : 1f;
            float vanillaReach = vanilla.Reach(rot, new Vector2(0f, o.y), new Vector2(side, 0f), VanillaCanvas);
            float bodyReach = body.Reach(rot, new Vector2(0f, z), new Vector2(side, 0f), canvas);
            __result.x = side * (bodyReach + Mathf.Max(0f, Mathf.Abs(o.x) - vanillaReach));
            __result.z = z;
        }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.ScaleFor))]
    static class PawnRenderNodeWorker_ScaleFor_TailFollowsRRBody
    {
        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            if (!TailOnRRBody.Applies(node, parms, out float mesh))
                return;
            float k = TailOnRRBody.Growth(mesh);
            // a tail on the body mesh is already scaled by the whole body size
            __result *= node.Props.overrideMeshSize.HasValue ? k : k / mesh;
        }
    }
}
