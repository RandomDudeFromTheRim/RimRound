using HarmonyLib;
using RimRound.Comps;
using UnityEngine;
using RimWorld;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// A gorge constrictor is drawn as big as the load it carries - about as wide as
    /// a pawn carrying that much would be - so it arrives huge and shrinks as it is
    /// wounded and spills (Comp_RRConstrictorHunt.DrawWidth). Its body node and the
    /// overlays under it are scaled (the Animal render tree draws them through
    /// workers that don't override ScaleFor).
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.ScaleFor))]
    static class PawnRenderNodeWorker_ScaleFor_ConstrictorSwell
    {
        // the sac fills about 74% of its 1.5-cell canvas (drawSize 1.5)
        const float SacWidthAtScale1 = 1.5f * 0.74f;

        // render workers run on several threads at once (parallel pre-render): needs a concurrent map
        static readonly System.Collections.Concurrent.ConcurrentDictionary<ThingDef, bool> constrictorRaces = new System.Collections.Concurrent.ConcurrentDictionary<ThingDef, bool>();

        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            Pawn pawn = parms.pawn;
            if (pawn == null)
                return;
            // any constrictor kind (gorge, brood): races with the hunt comp
            if (!constrictorRaces.GetOrAdd(pawn.def, d => d.HasComp(typeof(Comp_RRConstrictorHunt))))
                return;
            PawnRenderNodeTagDef body = PawnRenderNodeTagDefOf.Body;
            if (node.Props.tagDef != body && node.parent?.Props.tagDef != body)
                return;

            var hunt = pawn.TryGetComp<Comp_RRConstrictorHunt>();
            if (hunt == null)
                return;
            __result *= hunt.DrawWidth / SacWidthAtScale1 * hunt.Props.drawScale;
        }
    }
}
