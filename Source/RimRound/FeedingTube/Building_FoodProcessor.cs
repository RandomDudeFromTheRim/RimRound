using RimRound.Comps;
using RimRound.FeedingTube.Comps;
using RimRound.FeedingTube.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.FeedingTube
{
    /// <summary>
    /// Converts solid feed stock from an adjacent hopper into liquid food,
    /// preserving the food's nutrition density. Fixed up from the original:
    ///  - the "Build Hopper" gizmo now places the hopper on an adjacent (not a
    ///    floating 2-cell-away) cell so the processor can actually reach it;
    ///  - hopper feedstock is scanned the same way vanilla hoppers are used;
    ///  - throughput (nutrition per processing pass / frequency) is higher so a
    ///    single processor can keep up with colony feeding;
    ///  - the pointless always-true feedstock gate now requires a real minimum.
    /// </summary>
    public class Building_FoodProcessor : Building
    {
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            this.trader = base.GetComp<FoodNetTrader_ThingComp>();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;

            yield return new Command_Action
            {
                Disabled = TryFindHopperSpot(out _) ? false : true,
                defaultLabel = "Build Hopper",
                defaultDesc = "Places a XL hopper on an adjacent empty cell so the processor can accept feed stock.",
                icon = Widgets.GetIconFor(Defs.ThingDefOf.RR_Hopper),
                action = delegate ()
                {
                    if (TryFindHopperSpot(out IntVec3 spot))
                        GenConstruct.PlaceBlueprintForBuild(Defs.ThingDefOf.RR_Hopper, spot, this.Map, Rot4.West, Faction, null);
                }
            };
        }

        private bool TryFindHopperSpot(out IntVec3 spot)
        {
            foreach (IntVec3 c in GenAdj.CellsAdjacentCardinal(this))
            {
                if (!c.InBounds(base.Map))
                    continue;
                if (c.GetEdifice(base.Map) != null)
                    continue;
                if (!c.Walkable(base.Map))
                    continue;
                // Don't sit on top of an existing hopper or food
                if (c.GetThingList(base.Map).Any(t => t.def == ThingDefOf.Hopper || t.def == Defs.ThingDefOf.RR_Hopper || this.IsAcceptableFeedstock(t.def)))
                    continue;
                spot = c;
                return true;
            }
            spot = IntVec3.Invalid;
            return false;
        }

        protected override void Tick()
        {
            base.Tick();
            ProcessFood();
        }

        private bool CanProcessNow
        {
            get
            {
                return trader.IsOn && trader.CanBeOn && trader.TransmitsFoodNow && this.HasEnoughFeedstockInHoppers();
            }
        }

        private bool ShouldProcessFood
        {
            get
            {
                if (FoodTransmitter_NetManager.For(this.Map)?.FoodNetAt(this.Position) is FoodNet f &&
                    f.Stored < f.StorageCapacity &&
                    CanProcessNow)
                {
                    return true;
                }

                return false;
            }
        }

        private void ProcessFood()
        {
            if (!FeedingTubeUtility.IsHashIntervalTick((int)processingFrequency) || !ShouldProcessFood)
                return;

            float remainingStorageSpace = trader.FoodNet.StorageCapacity - trader.FoodNet.Stored;
            if (remainingStorageSpace <= FeedingTubeUtility.MinRQ)
                return;

            Thing foodInHopper = TryGetFoodInHopper();
            if (foodInHopper is null)
                return;

            float nutritionForOneUnitOfFoodInHopper = foodInHopper.GetStatValue(StatDefOf.Nutrition, true);
            if (nutritionForOneUnitOfFoodInHopper <= 0f)
                return;

            float ftnRatio = GetNutritionDensityOfFoodInHopper(foodInHopper);

            // Convert up to maxNutritionToProcessPerTurn worth of nutrition, but
            // never more than we have room for or than the hopper holds.
            float volumePerItem = ftnRatio * nutritionForOneUnitOfFoodInHopper;
            int numberOfFoodItemsPerProcess = Mathf.Min(
                foodInHopper.stackCount,
                Mathf.CeilToInt(remainingStorageSpace / Mathf.Max(volumePerItem, 0.0001f)),
                Mathf.CeilToInt(maxNutritionToProcessPerTurn / Mathf.Max(nutritionForOneUnitOfFoodInHopper, 0.0001f)));

            if (numberOfFoodItemsPerProcess <= 0)
                return;

            float amountOfFoodVolumeToAdd = numberOfFoodItemsPerProcess * volumePerItem;

            foodInHopper.SplitOff(numberOfFoodItemsPerProcess);
            trader.FoodNet.Fill(amountOfFoodVolumeToAdd, ftnRatio);
        }

        private float GetNutritionDensityOfFoodInHopper(Thing foodInHopper)
        {
            float ftnRatio = 1;

            ThingComp_FoodItems_NutritionDensity NDComp = foodInHopper.TryGetComp<ThingComp_FoodItems_NutritionDensity>();
            if (NDComp != null)
            {
                ftnRatio = NDComp.Props.fullnessToNutritionRatio;
            }

            return ftnRatio;
        }

        private bool HasEnoughFeedstockInHoppers()
        {
            float totalNutritionInHoppers = 0;

            foreach (var c in AdjCellsCardinalInBounds)
            {
                Thing hopper = null;
                Thing foodOnHopper = null;

                List<Thing> thingsInCell = c.GetThingList(base.Map);
                foreach (Thing thing in thingsInCell)
                {
                    if (thing.def == ThingDefOf.Hopper || thing.def == Defs.ThingDefOf.RR_Hopper)
                        hopper = thing;

                    if (this.IsAcceptableFeedstock(thing.def))
                        foodOnHopper = thing;
                }

                if (foodOnHopper != null && hopper != null)
                    totalNutritionInHoppers += (float)foodOnHopper.stackCount * foodOnHopper.GetStatValue(StatDefOf.Nutrition, true);

                if (totalNutritionInHoppers >= minNutritionPerUse)
                    return true;
            }

            return false;
        }

        private List<IntVec3> AdjCellsCardinalInBounds
        {
            get
            {
                if (cachedAdjCells == null)
                {
                    cachedAdjCells = (from c in GenAdj.CellsAdjacentCardinal(this)
                                      where c.InBounds(base.Map)
                                      select c).ToList<IntVec3>();
                }

                return cachedAdjCells;
            }
        }

        private bool IsAcceptableFeedstock(ThingDef def)
        {
            return def.IsNutritionGivingIngestible &&
                def.ingestible.preferability != FoodPreferability.Undefined &&
                (def.ingestible.foodType & FoodTypeFlags.Plant) != FoodTypeFlags.Plant &&
                (def.ingestible.foodType & FoodTypeFlags.Tree) != FoodTypeFlags.Tree;
        }

        private Thing TryGetFoodInHopper()
        {
            for (int i = 0; i < this.AdjCellsCardinalInBounds.Count; i++)
            {
                Thing foodItem = null;
                Thing hopper = null;
                List<Thing> thingList = this.AdjCellsCardinalInBounds[i].GetThingList(base.Map);
                for (int j = 0; j < thingList.Count; j++)
                {
                    Thing thingInCell = thingList[j];
                    if (this.IsAcceptableFeedstock(thingInCell.def))
                        foodItem = thingInCell;
                    if (thingInCell.def == ThingDefOf.Hopper || thingInCell.def == Defs.ThingDefOf.RR_Hopper)
                        hopper = thingInCell;
                }
                if (foodItem != null && hopper != null)
                    return foodItem;
            }
            return null;
        }

        FoodNetTrader_ThingComp trader;

        // Amount of ticks between processing passes.
        float processingFrequency = 45;

        // Required nutrition in an adjacent hopper before the processor will run.
        float minNutritionPerUse = 0.9f;

        // Max nutrition converted per processing pass.
        float maxNutritionToProcessPerTurn = 0.5f;

        List<IntVec3> cachedAdjCells;
    }
}
