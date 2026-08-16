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
            // Only swallow the known PipeSystem postfix bug (a PipeNetDef whose
            // designator lacks designationCategoryDef); let any other NRE through.
            if (__exception is NullReferenceException &&
                __exception.StackTrace.Contains("PipeSystem.ResolvedAllowedDesignators_Patch"))
            {
                if (!loggedOnce)
                {
                    loggedOnce = true;
                    Log.Warning($"[RimRound] Suppressed NRE in ResolvedAllowedDesignators caused by a PipeSystem pipe net whose <designator> is missing <designationCategoryDef>. Further occurrences will be silent. Full exception:\n{__exception}");
                }
                return null;
            }
            return __exception;
        }
    }
}
