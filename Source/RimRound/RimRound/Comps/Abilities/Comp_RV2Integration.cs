using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Reflection;
using Verse;

namespace RimRound.Comps
{
    public static class Comp_RV2Integration
    {
        static Harmony harmonyInstance;

        static Type voreTrackerRecordType;
        static Type struggleManagerType;
        static Type rollType;
        static Type digestionUtilityType;
        static Type voreValidatorType;
        static Type statPartType;

        static FieldInfo recordPreyFI, recordPredFI;
        static FieldInfo struggleRecordFI, struggleRequiredFI;

        public static void PatchAll(Harmony harmony)
        {
            harmonyInstance = harmony;
            voreTrackerRecordType = AccessTools.TypeByName("RimVore2.VoreTrackerRecord");
            struggleManagerType = AccessTools.TypeByName("RimVore2.StruggleManager");
            rollType = AccessTools.TypeByName("RimVore2.Roll");
            digestionUtilityType = AccessTools.TypeByName("RimVore2.DigestionUtility");
            voreValidatorType = AccessTools.TypeByName("RimVore2.VoreValidator");
            statPartType = AccessTools.TypeByName("RimVore2.StatPart_VoreCapacityMultiplier");

            if (voreTrackerRecordType == null)
            {
                Log.Warning("[RimRound] RimVore2 not detected - skipping RV2 integration");
                return;
            }

            recordPreyFI = voreTrackerRecordType.GetField("Prey", BindingFlags.Public | BindingFlags.Instance);
            recordPredFI = voreTrackerRecordType.GetField("Predator", BindingFlags.Public | BindingFlags.Instance);
            struggleRecordFI = struggleManagerType?.GetField("record", BindingFlags.NonPublic | BindingFlags.Instance);
            struggleRequiredFI = struggleManagerType?.GetField("requiredStruggles", BindingFlags.NonPublic | BindingFlags.Instance);

            // 1
            TryPatchMethod(rollType, "GetRollStrength", "Postfix_VoreRoll");
            // 2
            TryPatchMethod(statPartType, "TransformValue", "Postfix_Capacity");
            // 3
            TryPatchMethod(digestionUtilityType, "FinishDigestion", "Postfix_Digestion");
            // 4
            TryPatchMethod(voreValidatorType, "WantsToProposeTo", "Postfix_ProposalPref");
            // 5
            TryPatchMethod(struggleManagerType, "CalculateRequiredStruggles", "Postfix_StruggleReq");
            // 6
            TryPatchMethod(voreTrackerRecordType, "Initialize", "Postfix_VoreStart");
        }

        static void TryPatchMethod(Type type, string method, string postfixName)
        {
            if (type == null) return;
            var mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (mi == null)
            {
                Log.Warning($"[RimRound] RV2 method {method} not found on {type}");
                return;
            }
            var postfix = typeof(Comp_RV2Integration).GetMethod(postfixName, BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null) return;
            harmonyInstance.Patch(mi, postfix: new HarmonyMethod(postfix));
        }

        static Pawn GetPrey(object record) => recordPreyFI?.GetValue(record) as Pawn;
        static Pawn GetPred(object record) => recordPredFI?.GetValue(record) as Pawn;

        // 1
        // Roll.GetRollStrength runs once per roll, after all of the roll's modifiers.
        // (Patching RollModifier.ModifyValue instead compounded this multiplier once
        // per modifier in the chain, whether or not the modifier applied.)
        static void Postfix_VoreRoll(ref float __result, object record)
        {
            if (record == null || recordPredFI == null || recordPreyFI == null) return;
            var pred = GetPred(record);
            var prey = GetPrey(record);
            if (pred == null || prey == null) return;

            float pSev = GetWeightSev(prey);
            float prSev = GetWeightSev(pred);
            float ratio = (pSev + 0.035f) / (prSev + 0.035f);

            var att = pred.TryGetComp<ThingComp_PawnAttitude>();
            float mult = att?.weightOpinion switch
            {
                WeightOpinion.Hate => 0.3f, WeightOpinion.Dislike => 0.5f,
                WeightOpinion.NeutralMinus => 0.7f, WeightOpinion.Neutral => 0.9f,
                WeightOpinion.NeutralPlus => 1.2f, WeightOpinion.Like => 1.5f,
                WeightOpinion.Love => 2f, WeightOpinion.Fanatical => 3f,
                _ => 1f,
            };
            __result *= mult * Math.Max(0.2f, 1f - ratio * 0.3f);
        }

        // 2
        static void Postfix_Capacity(StatRequest req, ref float val)
        {
            if (req.Thing is Pawn p)
            {
                float s = GetWeightSev(p);
                if (s >= 0.035f) val *= 1f + s * 0.5f;
            }
        }

        // 3
        static void Postfix_Digestion(object record, DamageDef appliedDamageDef)
        {
            var pred = GetPred(record);
            var prey = GetPrey(record);
            if (pred == null || prey == null) return;

            float nutrition = GetPreyNutrition(pred, prey);
            float kilos = Math.Min(nutrition * 0.15f, 20f);
            Utilities.HediffUtility.QueueWeightGain(pred, kilos);
        }

        // 4
        static void Postfix_ProposalPref(Pawn pawn, Thing target, ref bool __result)
        {
            if (!__result || target is not Pawn tp || pawn == null) return;
            float tw = GetWeightSev(tp);
            var att = pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (att == null) return;
            switch (att.weightOpinion)
            {
                case WeightOpinion.Hate when tw > 0.09f: __result = false; break;
                case WeightOpinion.Dislike when tw > 0.2f: __result = false; break;
                case WeightOpinion.Like when tw > 0 && tw < 0.035f: __result = false; break;
                case WeightOpinion.Love when tw > 0 && tw < 0.05f: __result = false; break;
                case WeightOpinion.Fanatical when tw > 0 && tw < 0.05f: __result = false; break;
            }
        }

        // 5
        // Heavier prey are sluggish and wedged in: scale RV2's own required
        // struggles (default 60) up. Overwriting it with (int)(severity * 2) made
        // fat prey need 0-4 struggles, so they escaped almost instantly.
        static void Postfix_StruggleReq(object __instance)
        {
            if (__instance == null || struggleRecordFI == null || struggleRequiredFI == null) return;
            var rec = struggleRecordFI.GetValue(__instance);
            var prey = GetPrey(rec);
            if (prey == null) return;
            float s = GetWeightSev(prey);
            if (s > 0.05f)
            {
                int required = (int)struggleRequiredFI.GetValue(__instance);
                struggleRequiredFI.SetValue(__instance, Math.Max(1, (int)Math.Round(required * (1f + s * 0.5f))));
            }
        }

        // 6
        static void Postfix_VoreStart(object __instance)
        {
            var prey = GetPrey(__instance);
            var pred = GetPred(__instance);
            if (prey == null || pred == null || !prey.RaceProps.Humanlike) return;

            float ps = GetWeightSev(prey);
            if (ps > 0.035f)
            {
                var sud = HediffMaker.MakeHediff(Defs.HediffDefOf.RimRound_SuddenWeightGain, pred);
                sud.Severity = ps * 0.3f;
                pred.health.AddHediff(sud);
            }

            var pa = pred.TryGetComp<ThingComp_PawnAttitude>();
            var pt = prey.needs?.mood?.thoughts?.memories;
            if (pt != null && pa != null && ps > 0.09f && pa.weightOpinion >= WeightOpinion.Like)
                pt.TryGainMemory(ThoughtDef.Named("RR_VoredFatPredator"), prey);
            else if (pt != null && pa != null && ps < 0.035f && pa.weightOpinion <= WeightOpinion.Dislike)
                pt.TryGainMemory(ThoughtDef.Named("RR_VoredThinPredator"), prey);
        }

        static float GetWeightSev(Pawn p) =>
            p.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight)?.Severity ?? 0;

        static float GetPreyNutrition(Pawn pred, Pawn prey)
        {
            if (prey == null) return 0;
            ThingDef fd = FoodUtility.GetFinalIngestibleDef(prey);
            if (fd != null) return FoodUtility.GetNutrition(pred, prey, fd);
            float meat = prey.GetStatValue(StatDefOf.MeatAmount, true);
            var md = prey.def?.race?.meatDef;
            return (meat > 0 && md != null) ? meat * md.GetStatValueAbstract(StatDefOf.Nutrition) : 10f;
        }
    }
}
