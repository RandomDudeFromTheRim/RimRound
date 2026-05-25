using HarmonyLib;
using RimRound.Hediffs;
using RimRound.Utilities;
using RimWorld;
using System;
using Verse.AI;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimRound.Comps
{
    public static class Comp_LactationIntegration
    {
        public static void PatchAll(HarmonyLib.Harmony harmony)
        {
            // #1: Milk yield scales with weight stage
            ModCompatibilityUtility.TryPatch(
                harmony,
                new ModPatchInfo("Lactation Expansion", "EuterpeMilkyTitfuck.WorkGiver_MilkSelf", "JobOnThing", MethodType.Normal),
                new PatchCollection
                {
                    postfix = typeof(Comp_LactationIntegration).GetMethod("Postfix_MilkJob", BindingFlags.Static | BindingFlags.NonPublic)
                });

            ModCompatibilityUtility.TryPatch(
                harmony,
                new ModPatchInfo("Lactation Expansion", "EuterpeMilkyTitfuck.WorkGiver_WardenMilkPrisoner", "JobOnThing", MethodType.Normal),
                new PatchCollection
                {
                    postfix = typeof(Comp_LactationIntegration).GetMethod("Postfix_MilkJob", BindingFlags.Static | BindingFlags.NonPublic)
                });

            ModCompatibilityUtility.TryPatch(
                harmony,
                new ModPatchInfo("Lactation Expansion", "EuterpeMilkyTitfuck.WorkGiver_DoctorMilkColonist", "JobOnThing", MethodType.Normal),
                new PatchCollection
                {
                    postfix = typeof(Comp_LactationIntegration).GetMethod("Postfix_MilkJob", BindingFlags.Static | BindingFlags.NonPublic)
                });

            // #2: Weight opinion affects milking mood
            ModCompatibilityUtility.TryPatch(
                harmony,
                new ModPatchInfo("Lactation Expansion", "EuterpeMilkyTitfuck.JobDriver_MilkPrisoner", "MakeNewToils", MethodType.Normal),
                new PatchCollection
                {
                    postfix = typeof(Comp_LactationIntegration).GetMethod("Postfix_MilkPrisonerToils", BindingFlags.Static | BindingFlags.NonPublic)
                });

            ModCompatibilityUtility.TryPatch(
                harmony,
                new ModPatchInfo("Lactation Expansion", "EuterpeMilkyTitfuck.JobDriver_GetMilkedPrisoner", "MakeNewToils", MethodType.Normal),
                new PatchCollection
                {
                    postfix = typeof(Comp_LactationIntegration).GetMethod("Postfix_GetMilkedToils", BindingFlags.Static | BindingFlags.NonPublic)
                });

            // #3: Gluttonium-spiked milk
            ModCompatibilityUtility.TryPatch(
                harmony,
                new ModPatchInfo("Lactation Expansion", "EuterpeMilkyTitfuck.JobDriver_MilkSelf", "MakeNewToils", MethodType.Normal),
                new PatchCollection
                {
                    postfix = typeof(Comp_LactationIntegration).GetMethod("Postfix_MilkSelfToils", BindingFlags.Static | BindingFlags.NonPublic)
                });

            // #5: Fullness affects lactation rate
            ModCompatibilityUtility.TryPatch(
                harmony,
                new ModPatchInfo("Lactation Expansion", "EuterpeMilkyTitfuck.HediffComp_LactationDecay", "SeverityChangePerDay", MethodType.Normal),
                new PatchCollection
                {
                    postfix = typeof(Comp_LactationIntegration).GetMethod("Postfix_LactationRate", BindingFlags.Static | BindingFlags.NonPublic)
                });
        }

        // #1: Multiply milk yield by weight stage
        static void Postfix_MilkJob(ref Job __result, Pawn pawn)
        {
            if (__result is null || pawn is null)
                return;

            float mult = GetMilkYieldMultiplier(pawn);
            if (mult <= 1f)
                return;

            FieldInfo countFI = typeof(Job).GetField("count", BindingFlags.Instance | BindingFlags.Public);
            if (countFI is null)
                return;

            int originalCount = (int)countFI.GetValue(__result);
            countFI.SetValue(__result, (int)(originalCount * mult));
        }

        static float GetMilkYieldMultiplier(Pawn pawn)
        {
            var weight = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
            if (weight is null || weight.Severity < 0.035f)
                return 1;

            if (weight.Severity < 0.09f) return 1.5f;
            if (weight.Severity < 0.2f) return 2f;
            if (weight.Severity < 0.43f) return 3f;
            if (weight.Severity < 1f) return 5f;
            if (weight.Severity < 3f) return 8f;
            if (weight.Severity < 10f) return 12f;
            return 15f;
        }

        // #2: Weight opinion affects milking mood (prisoner being milked)
        static void Postfix_MilkPrisonerToils(Pawn ___pawn)
        {
            var att = ___pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (att is null)
                return;

            var thoughts = ___pawn.needs?.mood?.thoughts;
            if (thoughts is null)
                return;

            if (att.weightOpinion <= WeightOpinion.Dislike)
            {
                thoughts.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_MilkedHate);
            }
            else if (att.weightOpinion >= WeightOpinion.Love)
            {
                thoughts.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_MilkedLove);
            }
        }

        // #2: Weight opinion affects milking mood (prisoner doing the milking)
        static void Postfix_GetMilkedToils(Pawn ___pawn)
        {
            var att = ___pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (att is null)
                return;

            var thoughts = ___pawn.needs?.mood?.thoughts;
            if (thoughts is null)
                return;

            if (att.weightOpinion >= WeightOpinion.Fanatical)
            {
                thoughts.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_MilkedOthersFanatical);
            }
        }

        // #3: Milk from gluttonium-exposed pawns carries exposure
        static void Postfix_MilkSelfToils(Pawn ___pawn)
        {
            var exposure = ___pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_GluttoniumExposure);
            if (exposure is null || exposure.Severity < 0.01f)
                return;

            Hediff milkSpiked = HediffMaker.MakeHediff(Defs.HediffDefOf.RR_GluttoniumExposure, ___pawn);
            milkSpiked.Severity = exposure.Severity * 0.1f;
            ___pawn.health.AddHediff(milkSpiked);
        }

        // #5: Fullness multiplier on lactation rate
        static void Postfix_LactationRate(ref float __result, HediffComp __instance)
        {
            Pawn pawn = __instance?.Pawn;
            if (pawn is null)
                return;

            var fnd = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fnd is null || fnd.Disabled)
                return;

            float fullnessRatio = fnd.CurrentFullness / Math.Max(fnd.SoftLimit, 0.1f);
            float mult = 1f + (fullnessRatio * 0.5f);
            __result *= mult;
        }
    }
}
