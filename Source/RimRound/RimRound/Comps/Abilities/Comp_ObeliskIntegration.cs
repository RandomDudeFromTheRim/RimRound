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
            // The mutator obelisk is Anomaly content but its class ships with the base
            // game, so patch it directly. (ModCompatibilityUtility.TryPatch matches mods
            // by display name, which "Ludeon.RimWorld" never was.)
            if (!ModsConfig.AnomalyActive)
                return;

            MethodInfo doMutation = AccessTools.Method(typeof(CompObelisk_Mutator), "DoMutation");
            if (doMutation is null)
            {
                Log.Warning("[RimRound] CompObelisk_Mutator.DoMutation not found; mutator obelisk weight gain is disabled.");
                return;
            }

            harmony.Patch(doMutation, postfix: new HarmonyMethod(typeof(Comp_ObeliskIntegration), nameof(Postfix_DoMutation)));
        }

        /// <summary>The mutator obelisk's fleshmass lung/stomach mutations also pile on flesh.</summary>
        static void Postfix_DoMutation(Pawn pawn, HediffDef mutation)
        {
            // DoMutation also runs when the mutation fails; only react to a real one
            if (pawn is null || mutation is null || !pawn.RaceProps.Humanlike ||
                pawn.health?.hediffSet?.HasHediff(mutation) != true)
                return;

            Pawn target = pawn;

            var weight = target.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
            if (weight is null)
                return;

            float gain = Rand.Range(0.02f, 0.08f);
            weight.Severity += gain;

            Utilities.HediffUtility.QueueWeightGain(target, gain * 10f);

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
