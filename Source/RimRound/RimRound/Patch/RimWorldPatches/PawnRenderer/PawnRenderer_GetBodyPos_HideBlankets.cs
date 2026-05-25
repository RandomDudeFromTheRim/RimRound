using HarmonyLib;
using RimRound.Comps;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;
using System.Reflection;
using RimRound.Utilities;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(PawnRenderer))]
    [HarmonyPatch("GetBodyPos")]
    public class PawnRenderer_GetBodyPos_HideBlankets
    {
        static FieldInfo pawnFieldInfo = typeof(PawnRenderer).GetField("pawn", BindingFlags.NonPublic | BindingFlags.Instance);
        static ConcurrentDictionary<string, HideCovers_ThingComp> pawnIdToComp = new ConcurrentDictionary<string, HideCovers_ThingComp>();

        public static void InvalidateCaches() 
        {
            pawnIdToComp.Clear();
        }

        public static void Postfix(PawnRenderer __instance, ref bool __2, Pawn ___pawn) 
        {
            Pawn pawn = ___pawn;

            if (pawn is null || !(pawn?.RaceProps?.Humanlike is bool b && b))
                return;

            HideCovers_ThingComp comp = pawnIdToComp.GetOrAdd(pawn.ThingID, (id) => pawn.TryGetComp<HideCovers_ThingComp>());

            if (comp is null)
                return;


            if (pawn.CurrentBed() is Building_Bed bed) 
            {
                if (bed.IsBlobBed())
                {
                    __2 = true;
                    return;
                }
                __2 = comp.HideCovers;
            }
        }
    }
}
