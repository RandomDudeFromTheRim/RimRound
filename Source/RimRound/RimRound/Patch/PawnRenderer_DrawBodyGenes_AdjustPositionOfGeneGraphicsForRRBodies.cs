using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using UnityEngine;
using RimRound.Utilities;
using RimWorld;

using BodyAlignmentOffsets = System.Tuple<Verse.Pair<float, float>, Verse.Pair<float, float>, Verse.Pair<float, float>>;

namespace RimRound.Patch
{
    //[HarmonyPatch(typeof(PawnRenderer))]
    //[HarmonyPatch("DrawBodyGenes")]
    //internal class PawnRenderer_DrawBodyGenes_AdjustPositionOfGeneGraphicsForRRBodies
    //{
    //    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    //    {
    //        List<CodeInstruction> codeInstructions = new List<CodeInstruction>(instructions);
    //        List<CodeInstruction> newInstructions = new List<CodeInstruction>();

    //        bool attemptedPatch = false;

    //        MethodInfo drawOffsetAtMI = typeof(GeneGraphicData).GetMethod(nameof(GeneGraphicData.DrawOffsetAt), BindingFlags.Instance | BindingFlags.Public);
    //        MethodInfo replacementMI = typeof(PawnRenderer_DrawBodyGenes_AdjustPositionOfGeneGraphicsForRRBodies).GetMethod(nameof(ReplacementMethod), BindingFlags.Static | BindingFlags.NonPublic);

    //        if (drawOffsetAtMI is null)
    //        {
    //            Log.Error($"drawOffsetAtMI was null in {nameof(PawnRenderer_DrawBodyGenes_AdjustPositionOfGeneGraphicsForRRBodies.Transpiler)}");
    //            return codeInstructions;
    //        }

    //        for (int i = 0; i < codeInstructions.Count; ++i)
    //        {
    //            if (!(codeInstructions[i].operand is MethodInfo mi) || mi != drawOffsetAtMI)
    //                continue;

    //            codeInstructions[i] = new CodeInstruction(OpCodes.Call, replacementMI);
    //            codeInstructions.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));

    //            attemptedPatch = true;
    //            break;
    //        }

    //        if (!attemptedPatch)
    //            Log.Error($"Failed to find suitable patch location in {nameof(PawnRenderer_DrawBodyGenes_AdjustPositionOfGeneGraphicsForRRBodies.Transpiler)}");

    //        return codeInstructions;
    //    }

    //    private static Vector3 ReplacementMethod(GeneGraphicData geneGraphic, Rot4 rot4, PawnRenderer pawnrenderer) 
    //    {
    //        Vector3 offset = geneGraphic.DrawOffsetAt(rot4);

    //        Pawn pawn = (Pawn)pawnRendererPawnFI.GetValue(pawnrenderer);
    //        if (pawn is null || pawn.story is null || pawn.story.bodyType is null || !BodyTypeUtility.HasCustomBody(pawn))
    //            return offset;

    //        if (bodyTypeToTailOffset.TryGetValue(pawn.story.bodyType, out BodyAlignmentOffsets offsets))
    //        {
    //            switch (rot4.AsInt) 
    //            {
    //                case 0:
    //                    offset += new Vector3(offsets.Item1.First, 0, offsets.Item1.Second);
    //                    break;
    //                case 1:
    //                    offset += new Vector3(offsets.Item2.First, 0, offsets.Item2.Second);
    //                    break;
    //                case 2:
    //                    offset += new Vector3(offsets.Item3.First, 0, offsets.Item3.Second);
    //                    break;
    //                case 3:
    //                    offset += new Vector3(-offsets.Item2.First, 0, offsets.Item2.Second);
    //                    break;
    //            }
    //        }

    //        return offset;
    //    }

    //    static FieldInfo pawnRendererPawnFI = typeof(PawnRenderer).GetField("pawn", BindingFlags.NonPublic | BindingFlags.Instance);

    //    static Dictionary<BodyTypeDef, BodyAlignmentOffsets> bodyTypeToTailOffset = new Dictionary<BodyTypeDef, BodyAlignmentOffsets>();
    //}
}
