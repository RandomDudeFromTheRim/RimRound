using HarmonyLib;
using RimRound.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(PawnRenderer))]
    [HarmonyPatch("ShellFullyCoversHead")]
    public class PawnRenderer_ShellFullyCoversHead_AlterShellClothingForRRBodies
    {
        static FieldInfo pawnFieldInfo = typeof(PawnRenderer).GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Postfix(ref bool __result, PawnRenderer __instance) 
        {
            Pawn pawn = (Pawn)pawnFieldInfo.GetValue(__instance);

            if (pawn is null || pawn.story is null || pawn.story.bodyType is null)
                return;

            if (BodyTypeUtility.HasCustomBody(pawn))
                __result = false;

            return;
        }
    }
}
