using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimRound.Patch.RimWorldPatches
{
    /// <summary>
    /// One-time save-repair for saves that still contain the old VPE-style
    /// food pipes (RR_FoodPipe / RR_UndergroundFoodPipe). Those defs were
    /// retired during the network consolidation; their defs are still provided
    /// (hidden, non-buildable) purely so old saves deserialize. After a map
    /// finishes loading, any leftover instances are converted into the steel
    /// they cost to build, so the player can continue the save cleanly.
    /// </summary>
    [HarmonyPatch(typeof(Map), "FinalizeLoading")]
    internal static class Map_FinalizeLoading_ConvertRetiredFoodPipes
    {
        private static readonly HashSet<string> retiredPipeDefs =
            new HashSet<string>
            {
                "RR_FoodPipe",
                "RR_UndergroundFoodPipe",
            };

        private static void Postfix(Map __instance)
        {
            Map map = __instance;
            if (map == null || map.listerThings == null)
            {
                return;
            }

            var candidates = new List<Thing>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing != null && thing.def != null &&
                    retiredPipeDefs.Contains(thing.def.defName))
                {
                    candidates.Add(thing);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            foreach (Thing pipe in candidates)
            {
                ConvertToSteel(map, pipe);
            }

            Log.Message(
                "[RimRound] Converted " + candidates.Count +
                " retired food pipe(s) into steel for save compatibility.");
        }

        private static void ConvertToSteel(Map map, Thing pipe)
        {
            if (map == null || pipe == null || !pipe.Spawned)
            {
                return;
            }

            // Refund a fair amount of steel as loose items based on the pipe's
            // build cost (item form, not mineable): the retired pipes cost
            // 5 / 10 / 15 steel depending on type.
            int steelAmount = GetSteelRefund(pipe.def);
            Map mapHeld = pipe.MapHeld ?? map;
            IntVec3 pos = pipe.Position;
            if (mapHeld != null && pos.InBounds(mapHeld) && steelAmount > 0)
            {
                Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                steel.stackCount = steelAmount;
                GenSpawn.Spawn(steel, pos, mapHeld);
            }

            pipe.DeSpawn(DestroyMode.Vanish);
            pipe.Destroy(DestroyMode.Vanish);
        }

        private static int GetSteelRefund(ThingDef def)
        {
            if (def == null || def.costList == null)
            {
                return 1;
            }

            int refund = 1;
            ThingDefCountClass steelCost =
                def.costList.FirstOrDefault((ThingDefCountClass c) =>
                    c != null && c.thingDef == ThingDefOf.Steel);
            if (steelCost != null)
            {
                refund = Mathf.Max(1, steelCost.count);
            }

            return refund;
        }
    }
}
