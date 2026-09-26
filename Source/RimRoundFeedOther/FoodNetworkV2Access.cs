using RimRound.FeedingTube;
using Verse;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Food Network v2 behind RimRound's IFoodNetworkAccess, so RimRound machines
    /// that don't have a dedicated v2 patch (e.g. the food converter) still reach
    /// the network while v2 has replaced the legacy FoodNet. Falls back to the
    /// previous implementation whenever v2 is switched off.
    /// </summary>
    internal sealed class FoodNetworkV2Access : IFoodNetworkAccess
    {
        private readonly IFoodNetworkAccess fallback;

        public FoodNetworkV2Access(IFoodNetworkAccess fallback)
        {
            this.fallback = fallback;
        }

        private static FoodNetworkV2 NetworkOf(Building machine)
        {
            FoodNetworkV2MapComponent manager = FoodNetworkV2MapComponent.For(machine.Map);
            return manager == null ? null : manager.NetworkFor(machine);
        }

        public float FreeCapacity(Building machine)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return fallback.FreeCapacity(machine);
            }

            FoodNetworkV2 network = NetworkOf(machine);
            return network != null && network.HasStorage ? network.RemainingCapacity : 0f;
        }

        public bool TryStore(Building machine, float nutrition, float fullnessPerNutrition)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return fallback.TryStore(machine, nutrition, fullnessPerNutrition);
            }

            FoodNetworkV2 network = NetworkOf(machine);
            return network != null && network.TryStore(new FoodBatchV2(
                nutrition,
                nutrition * fullnessPerNutrition,
                null,
                Find.TickManager == null ? 0 : Find.TickManager.TicksGame));
        }

        public float DrawNutrition(Building machine, float maxNutrition)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return fallback.DrawNutrition(machine, maxNutrition);
            }

            FoodNetworkV2 network = NetworkOf(machine);
            FoodBatchV2 batch;
            if (network == null ||
                !network.TryDraw(maxNutrition, float.MaxValue, false, out batch))
            {
                return 0f;
            }
            return batch.nutrition;
        }
    }
}
