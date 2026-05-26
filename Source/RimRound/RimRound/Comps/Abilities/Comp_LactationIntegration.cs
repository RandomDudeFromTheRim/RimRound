using HarmonyLib;
using RimRound.Hediffs;
using RimRound.Utilities;
using RimWorld;
using System;
using Verse.AI;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
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

        // #2: Weight opinion affects milking mood + WeirdMilk (warden milking prisoner)
        static IEnumerable<Toil> Postfix_MilkPrisonerToils(IEnumerable<Toil> __result, Pawn ___pawn, JobDriver __instance)
        {
            var att = ___pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (att != null)
            {
                var thoughts = ___pawn.needs?.mood?.thoughts;
                if (thoughts != null)
                {
                    if (att.weightOpinion <= WeightOpinion.Dislike)
                        thoughts.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_MilkedHate);
                    else if (att.weightOpinion >= WeightOpinion.Love)
                        thoughts.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_MilkedLove);
                }
            }

            foreach (var toil in __result)
                yield return toil;

            yield return new Toil
            {
                initAction = () =>
                {
                    Pawn prisoner = __instance?.job?.GetTarget(TargetIndex.A).Thing as Pawn;
                    if (prisoner != null)
                        TryReplaceMilk(___pawn, prisoner);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        // #2b: Weight opinion affects milking mood + WeirdMilk (prisoner getting milked)
        static IEnumerable<Toil> Postfix_GetMilkedToils(IEnumerable<Toil> __result, Pawn ___pawn, JobDriver __instance)
        {
            var att = ___pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (att != null)
            {
                var thoughts = ___pawn.needs?.mood?.thoughts;
                if (thoughts != null)
                {
                    if (att.weightOpinion >= WeightOpinion.Fanatical)
                        thoughts.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_MilkedOthersFanatical);
                }
            }

            foreach (var toil in __result)
                yield return toil;

            yield return new Toil
            {
                initAction = () =>
                {
                    Pawn prisoner = __instance?.job?.GetTarget(TargetIndex.A).Thing as Pawn;
                    if (prisoner != null)
                        TryReplaceMilk(___pawn, prisoner);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        // #3: WeirdMilk spawn for gluttonium/meld-exposed pawns (self-milking)
        static IEnumerable<Toil> Postfix_MilkSelfToils(IEnumerable<Toil> __result, Pawn ___pawn)
        {
            foreach (var toil in __result)
                yield return toil;

            yield return new Toil
            {
                initAction = () => TryReplaceMilk(___pawn, ___pawn),
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        static bool HasContaminationCondition(Pawn pawn)
        {
            if (pawn?.health?.hediffSet is null)
                return false;

            bool hasGlut = pawn.health.hediffSet.GetFirstHediffOfDef(Defs.HediffDefOf.RR_GluttoniumExposure)?.Severity > 0.01f;
            bool hasMeld = pawn.health.hediffSet.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldOvergrowth) != null;
            return hasGlut || hasMeld;
        }

        static void TryReplaceMilk(Pawn recipient, Pawn source)
        {
            if (recipient?.inventory?.innerContainer is null || source?.health?.hediffSet is null)
                return;

            if (!HasContaminationCondition(source))
                return;

            Thing milk = recipient.inventory.innerContainer.FirstOrDefault(t => t.def.defName == "SEX_BreastMilk");
            if (milk is null)
                return;

            int count = milk.stackCount;
            milk.Destroy();

            Thing weirdMilk = ThingMaker.MakeThing(Defs.ThingDefOf.RR_WeirdMilk);
            weirdMilk.stackCount = count;
            recipient.inventory.innerContainer.TryAdd(weirdMilk);
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
