using RimRound.Utilities;
using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using Verse;

namespace RimRound.Comps
{
    public static class Comp_GluttoniumArousal
    {
        public static void PatchIntimacyNeed(HarmonyLib.Harmony harmony)
        {
            ModCompatibilityUtility.TryPatch(
                harmony,
                new ModPatchInfo("Intimacy - A Lovin' Expansion", "LoveyDoveySexWithEuterpe.Need_Intimacy", "NeedInterval", MethodType.Normal),
                new PatchCollection
                {
                    postfix = typeof(Comp_GluttoniumArousal).GetMethod(nameof(Postfix_NeedInterval), BindingFlags.Static | BindingFlags.NonPublic)
                });
        }

        static void Postfix_NeedInterval(Pawn ___pawn)
        {
            if (___pawn is null || !___pawn.RaceProps.Humanlike)
                return;

            var exposure = ___pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_GluttoniumExposure);
            if (exposure is null || exposure.Severity < 0.01f)
                return;

            var attitude = ___pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (attitude is null)
                return;

            float multiplier = GetArousalMultiplier(attitude.weightOpinion, exposure.Severity);
            if (multiplier == 0)
                return;

            Type needType = ___pawn.needs?.AllNeeds?.Find(n => n.def.defName == "SEX_Intimacy")?.GetType();
            if (needType is null)
                return;

            FieldInfo curLevelIntFI = needType.GetField("curLevelInt", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo defFI = needType.GetField("def", BindingFlags.Instance | BindingFlags.Public);
            if (curLevelIntFI is null || defFI is null)
                return;

            float cur = (float)curLevelIntFI.GetValue(___pawn.needs.AllNeeds.Find(n => n.def.defName == "SEX_Intimacy"));
            NeedDef def = (NeedDef)defFI.GetValue(___pawn.needs.AllNeeds.Find(n => n.def.defName == "SEX_Intimacy"));

            float change = multiplier * exposure.Severity * 0.01f;
            float newVal = Math.Max(0, Math.Min(1, cur + change));
            curLevelIntFI.SetValue(___pawn.needs.AllNeeds.Find(n => n.def.defName == "SEX_Intimacy"), newVal);
        }

        static float GetArousalMultiplier(WeightOpinion opinion, float severity)
        {
            switch (opinion)
            {
                case WeightOpinion.Hate: return -0.5f;
                case WeightOpinion.Dislike: return -0.3f;
                case WeightOpinion.NeutralMinus: return -0.1f;
                case WeightOpinion.Neutral: return 0;
                case WeightOpinion.NeutralPlus: return 0.1f;
                case WeightOpinion.Like: return 0.3f;
                case WeightOpinion.Love: return 0.5f;
                case WeightOpinion.Fanatical: return 0.8f;
                default: return 0;
            }
        }
    }
}
