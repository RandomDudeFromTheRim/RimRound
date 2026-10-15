using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimRound.Comps;

namespace RimRound.Patch
{
    /// <summary>
    /// VNPE's nutrient paste dripper feeds a bed's occupant once their food level drops under a
    /// threshold. RimRound pawns go by fullness and diet mode instead, so the dripper's one
    /// read of the food level is swapped for DripperFoodLevel: well under any threshold when
    /// RimRound says they should eat, well over it when not, and the real level for pawns
    /// RimRound doesn't feed. Same stack shape as the call it replaces (Need in, float out),
    /// so the IL stays valid on Mono and CoreCLR alike.
    /// </summary>
    public static class VENutrientPaste_Building_Dripper_FixForRR
    {
        private static readonly MethodInfo methodInfo_GetCurLevelPercentage = typeof(Need).GetProperty(nameof(Need.CurLevelPercentage)).GetGetMethod();
        private static readonly MethodInfo methodInfo_DripperFoodLevel = typeof(VENutrientPaste_Building_Dripper_FixForRR).GetMethod(nameof(DripperFoodLevel), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly AccessTools.FieldRef<Need, Pawn> needPawn = AccessTools.FieldRefAccess<Need, Pawn>("pawn");

        private static float DripperFoodLevel(Need need)
        {
            Pawn pawn = needPawn(need);
            if (!(pawn?.TryGetComp<FullnessAndDietStats_ThingComp>() is FullnessAndDietStats_ThingComp fullnessComp) || fullnessComp.DietMode == DietMode.Disabled)
                return need.CurLevelPercentage;
            return ShouldEat(pawn, fullnessComp) ? -1f : 2f;
        }

        private static bool ShouldEat(Pawn pawn, FullnessAndDietStats_ThingComp fullnessComp)
        {
            switch (fullnessComp.DietMode)
            {
                case DietMode.Nutrition:
                case DietMode.Hybrid:
                    return pawn.needs.food.CurLevel + fullnessComp.CurrentFullness / fullnessComp.CurrentFullnessToNutritionRatio <= fullnessComp.GetRanges().First;
                case DietMode.Fullness:
                    return fullnessComp.CurrentFullness <= fullnessComp.GetRanges().First;
                default:
                    return false;
            }
        }

        [HarmonyPriority(Priority.VeryHigh)]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool found = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!found && (instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) && instruction.operand as MethodInfo == methodInfo_GetCurLevelPercentage)
                {
                    // replaced in place, so any labels on it stay put
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = methodInfo_DripperFoodLevel;
                    found = true;
                }
                yield return instruction;
            }
            if (!found)
                Log.Warning("[RimRound] Couldn't find the nutrient paste dripper's food check; it will use VNPE's own threshold.");
        }


        public static PatchCollection GetPatchCollection()
        {
            return new PatchCollection
            {
                transpiler = typeof(VENutrientPaste_Building_Dripper_FixForRR).GetMethod(
                    nameof(VENutrientPaste_Building_Dripper_FixForRR.Transpiler), ModCompatibilityUtility.majorFlags)
            };
        }
    }
}
