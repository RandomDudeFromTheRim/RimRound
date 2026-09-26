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

            // copy first: converting despawns things out of the lister
            List<Thing> candidates = map.listerThings.AllThings
                .Where(thing => thing?.def != null && retiredPipeDefs.Contains(thing.def.defName))
                .ToList();

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
            if (!pipe.Spawned)
            {
                return;
            }

            // Refund the steel the pipe cost to build as loose items (the retired
            // pipes cost 5 / 10 / 15 steel depending on type).
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            steel.stackCount = GetSteelRefund(pipe.def);
            IntVec3 pos = pipe.Position;

            pipe.Destroy(DestroyMode.Vanish);
            GenSpawn.Spawn(steel, pos, map);
        }

        private static int GetSteelRefund(ThingDef def)
        {
            int steelCost = def.costList?
                .FirstOrDefault(c => c?.thingDef == ThingDefOf.Steel)?
                .count ?? 0;
            return Mathf.Max(1, steelCost);
        }
    }
}
