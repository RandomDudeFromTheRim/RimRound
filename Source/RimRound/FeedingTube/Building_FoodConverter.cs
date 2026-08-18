using RimRound.FeedingTube.Comps;
using RimRound.FeedingTube.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace RimRound.FeedingTube
{
    /// <summary>
    /// Bridges the VNPE (Vanilla Nutrient Paste Expanded) VEF pipe network with
    /// RimRound's density-aware liter FoodNet. It registers as a member of BOTH
    /// the VNPE paste net (via PipeSystem.CompResource) and a RimRound legacy
    /// FoodNet (via FoodNetTrader_ThingComp), and moves resource between the two.
    ///
    /// Paste is treated like RimRound's FoodProcessor does for plain food (density
    /// 1), so it can be further condensed by a Nutrient Distillery if desired.
    /// Direction is toggled with a gizmo.
    /// </summary>
    public class Building_FoodConverter : Building
    {
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            vnpeComp = this.GetComp<PipeSystem.CompResource>();
            foodTrader = this.GetComp<FoodNetTrader_ThingComp>();
            powerComp = this.GetComp<CompPowerTrader>();
        }

        protected override void Tick()
        {
            base.Tick();
            if (!IsHashIntervalTick(30))
                return;
            if (powerComp != null && !powerComp.PowerOn)
                return;
            ProcessConversion();
        }

        private void ProcessConversion()
        {
            PipeSystem.PipeNet vnpeNet = vnpeComp?.PipeNet;
            FoodNet foodNet = foodTrader?.FoodNet;

            if (vnpeNet == null || foodNet == null)
                return;

            if (exportingToLegacy)
            {
                // VNPE paste -> RimRound liters at plain-food density (1).
                if (vnpeNet.Stored < mealsPerConversion || foodNet.StorageCapacity - foodNet.Stored <= FeedingTubeUtility.MinRQ)
                    return;

                vnpeNet.DrawAmongStorage(mealsPerConversion, out float drawnMeals, null, drawFromOverflow: true);
                if (drawnMeals <= 0f)
                    return;

                float nutrition = drawnMeals * pasteNutrition;
                foodNet.Fill(nutrition, pasteDensity);
            }
            else
            {
                // RimRound liters -> VNPE paste.
                float ftnRatio = Mathf.Max(foodNet.FullnessToNutritionRatio, 0.0001f);
                if (foodNet.Stored <= FeedingTubeUtility.MinRQ || vnpeNet.AvailableCapacity <= FeedingTubeUtility.MinRQ)
                    return;

                float litersToDrain = Mathf.Min(mealsPerConversion * pasteNutrition / ftnRatio, foodNet.Stored);
                float amountLeft = foodNet.Drain(litersToDrain);
                float litersDrained = litersToDrain - amountLeft;
                if (litersDrained <= 0f)
                    return;

                // nutrition = liters * NutritionToFullnessRatio = liters / ftnRatio; then back to paste meals
                float nutrition = litersDrained / ftnRatio;
                vnpeNet.DistributeAmongStorage(nutrition / pasteNutrition, out _);
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;

            yield return new Command_Action
            {
                defaultLabel = exportingToLegacy ? "Export: Paste → Liquid food" : "Export: Liquid food → Paste",
                defaultDesc = "Toggle which direction liquid food is transferred between the paste and food-tube networks.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", true),
                action = delegate
                {
                    exportingToLegacy = !exportingToLegacy;
                }
            };
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(base.GetInspectString());
            sb.Append(exportingToLegacy ? "\nConverting: VNPE paste → liquid food" : "\nConverting: liquid food → VNPE paste");
            return sb.ToString().TrimEndNewlines();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref exportingToLegacy, "exportingToLegacy", true);
        }

        private bool IsHashIntervalTick(int interval)
        {
            return Find.TickManager.TicksGame % interval == 0;
        }

        PipeSystem.CompResource vnpeComp;
        FoodNetTrader_ThingComp foodTrader;
        CompPowerTrader powerComp;

        bool exportingToLegacy = true;

        const float pasteDensity = 1f;
        const float mealsPerConversion = 0.5f;
        const float pasteNutrition = 0.9f;
    }
}
