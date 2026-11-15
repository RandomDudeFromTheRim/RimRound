using HarmonyLib;
using RimRound.Hediffs;
using RimWorld;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage), typeof(DamageInfo))]
    public class MeldOvergrowth_DamagePatch
    {
        [HarmonyPriority(Priority.Last)]
        static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (!(__instance is Pawn pawn) || pawn.Destroyed || pawn.Dead || dinfo.Amount <= 0)
                return;

            var overgrowth = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldOvergrowth) as Hediff_MeldOvergrowth;
            if (overgrowth == null || overgrowth.Severity <= 0)
                return;

            float absorbed = overgrowth.AbsorbDamage(dinfo.Amount, dinfo.Def);
            if (absorbed > 0)
            {
                // Reduce the damage amount by what the meld absorbed
                float newAmount = System.Math.Max(0, dinfo.Amount - absorbed);
                dinfo.SetAmount(newAmount);

                if (pawn.IsHashIntervalTick(250))
                {
                    Messages.Message(
                        $"{pawn.LabelShort}'s melded flesh absorbs {(int)absorbed} damage!",
                        new LookTargets(pawn),
                        MessageTypeDefOf.NeutralEvent);
                }
            }
        }
    }
}
