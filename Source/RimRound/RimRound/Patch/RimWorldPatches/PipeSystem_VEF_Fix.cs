using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(DesignationCategoryDef))]
    [HarmonyPatch("ResolvedAllowedDesignators", MethodType.Getter)]
    public class DesignationCategoryDef_ResolvedAllowedDesignators_Fix
    {
        [HarmonyFinalizer]
        static Exception Finalizer(Exception __exception)
        {
            if (__exception is NullReferenceException)
            {
                Log.Warning("[RimRound] Suppressed NRE in ResolvedAllowedDesignators (PipeSystem/VEF postfix conflict)");
                return null;
            }
            return __exception;
        }
    }
}
