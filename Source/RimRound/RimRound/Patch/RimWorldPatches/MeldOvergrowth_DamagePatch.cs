using HarmonyLib;
using RimRound.Hediffs;
using RimWorld;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Pawn))]
    [HarmonyPatch(nameof(Pawn.TakeDamage))]
    public class MeldOvergrowth_DamagePatch
    {
        static void Prefix(Pawn __instance, ref DamageInfo dinfo)
        {
            if (__instance == null || __instance.Dead || dinfo.Amount <= 0)
                return;

            var overgrowth = __instance.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldOvergrowth) as Hediff_MeldOvergrowth;
            if (overgrowth == null || overgrowth.Severity <= 0)
                return;

            float absorbed = overgrowth.AbsorbDamage(dinfo.Amount, dinfo.Def);
            if (absorbed > 0)
            {
                // Reduce the damage amount by what the meld absorbed
                float newAmount = System.Math.Max(0, dinfo.Amount - absorbed);
                dinfo.SetAmount(newAmount);

                if (__instance.IsHashIntervalTick(250))
                {
                    Messages.Message(
                        $"{__instance.LabelShort}'s melded flesh absorbs {(int)absorbed} damage!",
                        new LookTargets(__instance),
                        MessageTypeDefOf.NeutralEvent);
                }
            }
        }
    }
}
