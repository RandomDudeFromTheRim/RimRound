using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Concurrent;
using UnityEngine;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// Fur genes only ship textures for the vanilla body types. On a RimRound body the
    /// fur is drawn from RimRound's own fur set instead (Source/TextureGen/furgen.py):
    /// each body texture redrawn as a coat, in the vanilla furskin style or, for Erin's
    /// Experiments' expies, in theirs. Only a body with no fur texture falls back to
    /// no fur at all.
    /// </summary>
    [HarmonyPatch(typeof(FurDef))]
    [HarmonyPatch(nameof(FurDef.GetFurBodyGraphicPath))]
    public class FurDef_GetFurBodyGraphicPath_ReturnTransparentForRR
    {
        const string BodiesPath = "Things/Pawn/Humanlike/Bodies/";
        const string FurPath = "Things/Pawn/Humanlike/Bodies/Fur/";

        static ModContentPack content;
        static readonly ConcurrentDictionary<string, bool> exists = new ConcurrentDictionary<string, bool>();

        public static void Postfix(FurDef __instance, ref string __result, Pawn pawn)
        {
            if (!BodyTypeUtility.HasCustomBody(pawn))
                return;

            string body = BodyTypeUtility.GetProperBodyGraphicPathFromPawn(pawn);
            if (body == null || !body.StartsWith(BodiesPath))
            {
                __result = "BlankTexture";
                return;
            }
            string rel = body.Substring(BodiesPath.Length);

            // a coat made for this very fur (e.g. Alpha Genes' bodies, in their load folder)
            string own = FurPath + __instance.defName + "/" + rel;
            if (HasTexture(own + "_south"))
            {
                __result = own;
                return;
            }
            string fur = FurPath + StyleFor(__instance) + "/" + rel;
            __result = HasTexture(fur + "_south") ? fur : "BlankTexture";
        }

        /// <summary>The generic coat otherwise: the expies' own look, or vanilla furskin for everything else.</summary>
        static string StyleFor(FurDef def) => def.defName.StartsWith("ERN_Expie") ? "Expie" : "Furskin";

        // A plain lookup in RimRound's own textures: this can run off the main thread,
        // where ContentFinder's fallback to Resources.Load isn't allowed.
        static bool HasTexture(string path) => exists.GetOrAdd(path, p =>
        {
            if (content == null)
                content = LoadedModManager.RunningModsListForReading.Find(m => m.assemblies.loadedAssemblies.Contains(typeof(FurDef_GetFurBodyGraphicPath_ReturnTransparentForRR).Assembly));
            return content?.GetContentHolder<Texture2D>().Get(p) != null;
        });
    }
}
