using HarmonyLib;
using RimRound.Comps;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// Let RR gas spread through vents.
    /// </summary>
    [HarmonyPatch(typeof(Building_Vent))]
    [HarmonyPatch(nameof(Building_Vent.TickRare))]
    public class Building_Vent_TickRare_DiffuseRRGasses
    {
        public static void Postfix(Building_Vent __instance) 
        {
            if (FlickUtility.WantsToBeOn(__instance))
            {
                // the vent's own map, not whatever the camera is on: RR gas belongs to the
                // map the vent is standing on, and a second colony or a pocket map ticks too
                var mapComp = __instance.Map?.GetComponent<MapComp_RRGasGrid>();

                if (mapComp is null)
                    return;

                mapComp.EqualizeGasThroughBuilding(__instance, true);
            }
        }
    }
}
