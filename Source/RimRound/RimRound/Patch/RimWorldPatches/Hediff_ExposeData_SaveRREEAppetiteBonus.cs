using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// RimRound Extra Events' appetite stimulant keeps the digestion bonus it has
    /// applied to the pawn in a field it never saves. The pawn's bonus is saved, so after
    /// a load the hediff thought it had applied nothing and stacked the bonus again on
    /// its next stage change. This saves the field alongside the hediff. RREE is a
    /// separate assembly, so it is reached by name; skipped if it isn't loaded.
    /// </summary>
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.ExposeData))]
    public static class Hediff_ExposeData_SaveRREEAppetiteBonus
    {
        static readonly Type stimulant = AccessTools.TypeByName("RimRoundExtraEvents.Hediffs.Hediff_AppetiteStimulant");
        static readonly FieldInfo bonus = stimulant == null ? null : AccessTools.Field(stimulant, "currentDigestionBonus");

        static bool Prepare() => bonus != null;

        public static void Postfix(Hediff __instance)
        {
            if (bonus == null || !stimulant.IsInstanceOfType(__instance))
                return;
            float value = (float)bonus.GetValue(__instance);
            Scribe_Values.Look(ref value, "rreeDigestionBonus", 0f);
            bonus.SetValue(__instance, value);
        }
    }
}
