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
    /// RimRound's density-aware liquid food network. It is a member of BOTH the
    /// VNPE paste net (via PipeSystem.CompResource) and the food conduit network
    /// (via FoodNetTrader_ThingComp), and moves resource between the two. The
    /// food side goes through FoodNetworkAccess, so it works with the legacy
    /// FoodNet and with Food Network v2 alike.
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
            if (!FeedingTubeUtility.IsHashIntervalTick(CheckIntervalTicks))
                return;
            if (powerComp != null && !powerComp.PowerOn)
                return;
            ProcessConversion();
        }

        private void ProcessConversion()
        {
            PipeSystem.PipeNet pasteNet = vnpeComp?.PipeNet;
            if (pasteNet == null || foodTrader == null)
                return;

            if (exportingToLegacy)
                PasteToLiquid(pasteNet, FoodNetworkAccess.Current);
            else
                LiquidToPaste(pasteNet, FoodNetworkAccess.Current);
        }

        /// <summary>VNPE paste -> liquid food at plain-food density (1).</summary>
        private void PasteToLiquid(PipeSystem.PipeNet pasteNet, IFoodNetworkAccess foodNet)
        {
            // check for room first, so no paste is drawn that can't be stored
            float fullnessNeeded = mealsPerConversion * pasteNutrition * pasteDensity;
            if (pasteNet.Stored < mealsPerConversion || foodNet.FreeCapacity(this) < fullnessNeeded)
                return;

            pasteNet.DrawAmongStorage(mealsPerConversion, out float drawnMeals, null, drawFromOverflow: true);
            if (drawnMeals <= 0f)
                return;

            if (!foodNet.TryStore(this, drawnMeals * pasteNutrition, pasteDensity))
                pasteNet.DistributeAmongStorage(drawnMeals, out _); // put it back rather than lose it
        }

        /// <summary>Liquid food -> VNPE paste.</summary>
        private void LiquidToPaste(PipeSystem.PipeNet pasteNet, IFoodNetworkAccess foodNet)
        {
            if (pasteNet.AvailableCapacity < mealsPerConversion)
                return;

            float nutrition = foodNet.DrawNutrition(this, mealsPerConversion * pasteNutrition);
            if (nutrition <= 0f)
                return;

            pasteNet.DistributeAmongStorage(nutrition / pasteNutrition, out _);
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

        PipeSystem.CompResource vnpeComp;
        FoodNetTrader_ThingComp foodTrader;
        CompPowerTrader powerComp;

        bool exportingToLegacy = true;

        const int CheckIntervalTicks = 30;
        const float pasteDensity = 1f;
        const float mealsPerConversion = 0.5f;
        const float pasteNutrition = 0.9f;
    }
}
