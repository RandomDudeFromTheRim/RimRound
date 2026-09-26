using RimRound.FeedingTube.Comps;
using RimRound.FeedingTube.Utilities;
using UnityEngine;
using Verse;

namespace RimRound.FeedingTube
{
    /// <summary>
    /// How a machine stores food into, and draws food out of, the liquid food
    /// network it is connected to. The default goes through RimRound's own
    /// FoodNet. RimRoundFeedOther swaps in Food Network v2, which takes over from
    /// FoodNet while it is enabled, so machines written against this keep working
    /// with either.
    /// </summary>
    public interface IFoodNetworkAccess
    {
        /// <summary>Free space on the machine's network, in fullness units (0 when not connected).</summary>
        float FreeCapacity(Building machine);

        /// <summary>Stores nutrition at the given fullness-per-nutrition density. Stores nothing and returns false if it doesn't all fit.</summary>
        bool TryStore(Building machine, float nutrition, float fullnessPerNutrition);

        /// <summary>Draws up to maxNutrition from the machine's network and returns the nutrition actually drawn.</summary>
        float DrawNutrition(Building machine, float maxNutrition);
    }

    public static class FoodNetworkAccess
    {
        public static IFoodNetworkAccess Current = new LegacyFoodNetAccess();
    }

    /// <summary>RimRound's original FoodNet, reached through the machine's FoodNetTrader comp.</summary>
    internal sealed class LegacyFoodNetAccess : IFoodNetworkAccess
    {
        static FoodNet NetOf(Building machine) => machine.GetComp<FoodNetTrader_ThingComp>()?.FoodNet;

        public float FreeCapacity(Building machine)
        {
            FoodNet net = NetOf(machine);
            return net == null ? 0f : Mathf.Max(0f, net.StorageCapacity - net.Stored);
        }

        public bool TryStore(Building machine, float nutrition, float fullnessPerNutrition)
        {
            FoodNet net = NetOf(machine);
            float fullness = nutrition * fullnessPerNutrition;
            if (net == null || fullness <= 0f || FreeCapacity(machine) < fullness)
                return false;

            net.Fill(fullness, fullnessPerNutrition);
            return true;
        }

        public float DrawNutrition(Building machine, float maxNutrition)
        {
            FoodNet net = NetOf(machine);
            if (net == null || net.Stored <= FeedingTubeUtility.MinRQ)
                return 0f;

            float fullnessPerNutrition = Mathf.Max(net.FullnessToNutritionRatio, 0.0001f);
            float fullnessToDrain = Mathf.Min(maxNutrition * fullnessPerNutrition, net.Stored);
            float drained = fullnessToDrain - net.Drain(fullnessToDrain);
            return drained / fullnessPerNutrition;
        }
    }
}
