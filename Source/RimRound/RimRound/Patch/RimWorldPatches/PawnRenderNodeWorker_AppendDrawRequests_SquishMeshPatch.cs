using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using RimRound.Rendering;

namespace RimRound.Patch.RimWorldPatches
{
    /// <summary>
    /// Milestone 1 of the mesh work: swap the body node's pooled quad for a subdivided grid mesh
    /// with identical geometry, so the sprite must look exactly the same. Pure plumbing test.
    ///
    /// This runs inside PawnRenderTree.ParallelPreDraw, a parallel job. It therefore performs only
    /// a read-only lookup in a prebuilt cache and rewrites one struct in the caller's own list.
    /// Nothing shared is mutated here - doing so is what faulted every render job in the first
    /// squish build.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.AppendDrawRequests))]
    public static class PawnRenderNodeWorker_AppendDrawRequests_SquishMeshPatch
    {
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms,
                                   List<PawnGraphicDrawRequest> requests)
        {
            if (!SquishMeshCache.MeshEnabled)
                return;

            // the same guards the vanilla Spastic node uses
            if (parms.Portrait || parms.Statue || parms.Cache || parms.pawn == null || parms.pawn.Dead)
                return;

            if (!PawnRenderNodeWorker_ScaleFor_SquishPatch.IsBodyNode(node))
                return;

            Vector2 size = node.Props.overrideMeshSize ?? node.Props.drawSize;
            if (!SquishMeshCache.TryGet(size, out Mesh mesh) || mesh == null)
                return;

            for (int i = requests.Count - 1; i >= 0; i--)
            {
                if (requests[i].node != node)
                    continue;
                // mesh is readonly on the struct, so rebuild the request; the matrix is filled in
                // by ParallelPreDraw immediately after this hook returns
                requests[i] = new PawnGraphicDrawRequest(node, mesh, requests[i].material);
                return;
            }
        }
    }

    /// <summary>
    /// Main-thread half: builds the mesh for whatever size the body node actually uses.
    /// Called from SquishUtility.Tick, which already runs on the main thread - never from the
    /// render path, where creating a Mesh would be both unsafe and a per-frame cost.
    /// </summary>
    public static class SquishMeshBootstrap
    {
        static bool primed;

        /// <summary>
        /// One-shot, main thread. Builds a mesh for every body mesh size RimRound can assign
        /// (Values.validBodyMeshSizes), which is a bounded list rather than per-pawn work.
        /// </summary>
        public static void Note(Pawn pawn)
        {
            if (primed || !SquishMeshCache.MeshEnabled)
                return;
            primed = true;

            foreach (float s in RimRound.Utilities.Values.validBodyMeshSizes)
                SquishMeshCache.EnsureBuilt(new Vector2(s, s));
            SquishMeshCache.EnsureBuilt(new Vector2(1f, 1f));

            Log.Message($"[RimRound] squish meshes primed: {SquishMeshCache.Count} sizes " +
                        $"(grid {SquishMeshCache.Nx}x{SquishMeshCache.Ny})");
        }
    }
}
