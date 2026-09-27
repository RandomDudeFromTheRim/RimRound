using HarmonyLib;
using RimRound.Comps;
using UnityEngine;
using RimWorld;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// A gorge constrictor is drawn as big as the load it carries: it arrives
    /// swollen and shrinks as it is wounded and spills. Its body node and the
    /// overlays under it are scaled (the Animal render tree draws them through
    /// workers that don't override ScaleFor).
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.ScaleFor))]
    static class PawnRenderNodeWorker_ScaleFor_ConstrictorSwell
    {
        static ThingDef constrictorRace;
        static bool looked;

        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            Pawn pawn = parms.pawn;
            if (pawn == null)
                return;
            if (!looked)
            {
                constrictorRace = DefDatabase<ThingDef>.GetNamedSilentFail("RR_GorgeConstrictor");
                looked = true;
            }
            if (constrictorRace == null || pawn.def != constrictorRace)
                return;
            PawnRenderNodeTagDef body = PawnRenderNodeTagDefOf.Body;
            if (node.Props.tagDef != body && node.parent?.Props.tagDef != body)
                return;

            var hunt = pawn.TryGetComp<Comp_RRConstrictorHunt>();
            if (hunt == null)
                return;
            __result *= Mathf.Lerp(0.8f, 2.1f, hunt.Swell);
        }
    }
}
