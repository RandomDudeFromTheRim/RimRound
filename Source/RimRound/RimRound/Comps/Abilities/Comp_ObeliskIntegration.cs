using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Reflection;
using Verse;

namespace RimRound.Comps
{
    public static class Comp_ObeliskIntegration
    {
        public static void PatchAll(HarmonyLib.Harmony harmony)
        {
            // Patch the mutator obelisk to add weight gain on mutation
            ModCompatibilityUtility.TryPatch(
                harmony,
                new ModPatchInfo("Ludeon.RimWorld", "RimWorld.CompObelisk_Mutator", "TryMutate", MethodType.Normal),
                new PatchCollection
                {
                    postfix = typeof(Comp_ObeliskIntegration).GetMethod("Postfix_MutatorMutate", BindingFlags.Static | BindingFlags.NonPublic)
                });
        }

        static void Postfix_MutatorMutate(Pawn target)
        {
            if (target is null || !target.RaceProps.Humanlike)
                return;

            var weight = target.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
            if (weight is null)
                return;

            float gain = Rand.Range(0.02f, 0.08f);
            weight.Severity += gain;

            var fnd = target.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fnd != null && !fnd.Disabled)
            {
                fnd.activeWeightGainRequests.Enqueue(
                    new WeightGainRequest(gain * 10f, Find.TickManager.TicksGame + 5, 30000, false));
            }

            var att = target.TryGetComp<ThingComp_PawnAttitude>();
            if (att?.weightOpinion >= WeightOpinion.Like)
            {
                var thought = target.needs?.mood?.thoughts;
                thought?.memories.TryGainMemory(ThoughtDef.Named("RR_MeldGrowthThought"));
            }

            var exposure = target.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_GluttoniumExposure);
            if (exposure is null)
            {
                var exp = HediffMaker.MakeHediff(Defs.HediffDefOf.RR_GluttoniumExposure, target);
                exp.Severity = 0.1f;
                target.health.AddHediff(exp);
            }
            else
            {
                exposure.Severity += 0.05f;
            }
        }
    }
}
