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
        static bool loggedOnce = false;

        [HarmonyFinalizer]
        static Exception Finalizer(Exception __exception)
        {
            if (__exception is NullReferenceException)
            {
                if (!loggedOnce)
                {
                    loggedOnce = true;
                    Log.Warning($"[RimRound] Suppressed NRE in ResolvedAllowedDesignators (PipeSystem/VEF postfix conflict). Further occurrences will be silent. Full exception:\n{__exception}");
                }
                return null;
            }
            return __exception;
        }
    }
}
