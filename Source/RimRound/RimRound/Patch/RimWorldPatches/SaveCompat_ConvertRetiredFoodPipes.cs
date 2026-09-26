using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimRound.Patch.RimWorldPatches
{
    /// <summary>
    /// One-time save-repair for saves that still contain the old VPE-style
    /// food pipes (RR_FoodPipe / RR_UndergroundFoodPipe / RR_FoodValve). Those
    /// defs were retired during the network consolidation; their defs are still
    /// provided (hidden, non-buildable) purely so old saves deserialize. After a
    /// map finishes loading, any leftover instances are converted back into the
    /// materials they cost to build, so the player can continue the save cleanly.
    /// </summary>
    [HarmonyPatch(typeof(Map), "FinalizeLoading")]
    internal static class Map_FinalizeLoading_ConvertRetiredFoodPipes
    {
        private static readonly HashSet<string> retiredPipeDefs =
            new HashSet<string>
            {
                "RR_FoodPipe",
                "RR_UndergroundFoodPipe",
                "RR_FoodValve",
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
                RefundAndRemove(map, pipe);
            }

            Log.Message(
                "[RimRound] Converted " + candidates.Count +
                " retired food pipe(s) back into their build materials for save compatibility.");
        }

        /// <summary>Removes the pipe and drops its full build cost (e.g. 15 steel + 1 component for the valve).</summary>
        private static void RefundAndRemove(Map map, Thing pipe)
        {
            if (!pipe.Spawned)
            {
                return;
            }

            IntVec3 pos = pipe.Position;
            pipe.Destroy(DestroyMode.Vanish);

            List<ThingDefCountClass> cost = pipe.def.costList;
            if (cost.NullOrEmpty())
            {
                GenPlace.TryPlaceThing(ThingMaker.MakeThing(ThingDefOf.Steel), pos, map, ThingPlaceMode.Near);
                return;
            }

            foreach (ThingDefCountClass entry in cost)
            {
                if (entry?.thingDef == null || entry.count <= 0)
                {
                    continue;
                }

                Thing refund = ThingMaker.MakeThing(entry.thingDef);
                refund.stackCount = entry.count;
                GenPlace.TryPlaceThing(refund, pos, map, ThingPlaceMode.Near);
            }
        }
    }
}
