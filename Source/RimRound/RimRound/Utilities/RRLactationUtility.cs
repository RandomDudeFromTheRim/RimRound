using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    /// <summary>
    /// Milk, the RimRound way: Biotech's Lactating hediff, and Lactation Expansion's milk
    /// store on top of it when that mod is loaded (its comp replaces vanilla's). Lactation
    /// Expansion is only reached by reflection, so RimRound doesn't need it to load.
    /// </summary>
    public static class RRLactationUtility
    {
        static bool looked;
        static Type expansionComp;
        static FieldInfo storedField;
        static FieldInfo maxField;
        static MethodInfo recache;

        static void Look()
        {
            if (looked)
                return;
            looked = true;
            expansionComp = AccessTools.TypeByName("EuterpeMilkyTitfuck.HediffComp_Lactation");
            if (expansionComp == null)
                return;
            storedField = AccessTools.Field(expansionComp, "currentNutritionStored");
            maxField = AccessTools.Field(expansionComp, "maximumNutritionStored");
            recache = AccessTools.Method(expansionComp, "RecacheStats");
        }

        public static bool CanLactate(Pawn p) =>
            RimWorld.HediffDefOf.Lactating != null && p?.health != null && p.RaceProps.Humanlike
            && p.gender == Gender.Female && p.DevelopmentalStage.Adult();

        /// <summary>
        /// Starts her lactating if she isn't (and keeps it from fading if she is), and puts
        /// <paramref name="nutrition"/> of milk straight into her breasts, up to what they can hold.
        /// Returns the nutrition that didn't fit.
        /// </summary>
        public static float InduceAndFill(Pawn p, float nutrition)
        {
            if (!CanLactate(p))
                return nutrition;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(RimWorld.HediffDefOf.Lactating);
            if (h == null)
            {
                h = HediffMaker.MakeHediff(RimWorld.HediffDefOf.Lactating, p);
                h.Severity = 1f;
                p.health.AddHediff(h);
            }
            else
            {
                h.Severity = Mathf.Max(h.Severity, 1f);
            }
            if (nutrition <= 0f || !(h is HediffWithComps hc))
                return nutrition;

            Look();
            foreach (HediffComp comp in hc.comps)
            {
                if (expansionComp != null && expansionComp.IsInstanceOfType(comp) && storedField != null && maxField != null)
                {
                    recache?.Invoke(comp, null);
                    float stored = (float)storedField.GetValue(comp);
                    float room = Mathf.Max(0f, (float)maxField.GetValue(comp) - stored);
                    float added = Mathf.Min(room, nutrition);
                    storedField.SetValue(comp, stored + added);
                    return nutrition - added;
                }
                if (comp is HediffComp_Chargeable charge)
                {
                    float before = charge.Charge;
                    charge.TryCharge(nutrition);
                    return Mathf.Max(0f, nutrition - (charge.Charge - before));
                }
            }
            return nutrition;
        }

        /// <summary>
        /// How much more milk a body this heavy holds: RimRound's one milk-by-weight rule
        /// (BodyResourceUtility, tuned by the milk settings), shared with vanilla lactation
        /// and RJW milking.
        /// </summary>
        public static float CapacityFactor(Pawn p) => p?.health == null ? 1f : BodyResourceUtility.GetMilkMultiplierByWeight(p);
    }
}
