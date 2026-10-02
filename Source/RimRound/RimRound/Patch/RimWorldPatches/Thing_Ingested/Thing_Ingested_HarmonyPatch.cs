using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("Ingested", new Type[] { typeof(Pawn), typeof(float) })]
    public class Thing_Ingested_HarmonyPatch
    {
        public static void Postfix(Thing __instance, Pawn __0,
            ref float __result)
        {
            FullnessAndDietStats_ThingComp comp = __0?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (GeneralUtility.IsNotNull(comp)) 
            {
                PlaySloshIfBeer(__instance, comp);
                Thing_Ingested_AddFullness.Postfix(__instance, __0, ref __result, comp);
            }

        }

        private static void PlaySloshIfBeer(Thing ingestedItem, FullnessAndDietStats_ThingComp comp) 
        {
            if (ingestedItem.def.defName == ThingDefOf.Beer.defName)
            {
                const float SECONDS_OF_SLOSHING_PER_BEER = 15;
                comp.SloshDurationSeconds += SECONDS_OF_SLOSHING_PER_BEER;
                if (comp.SloshStartTick == 0) 
                {
                    comp.SloshStartTick = Find.TickManager.TicksAbs;
                }
            }
        }
    }
}
