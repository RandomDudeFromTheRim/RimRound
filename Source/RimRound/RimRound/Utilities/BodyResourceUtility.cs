using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    public static class BodyResourceUtility
    {
        /// <summary>
        /// How much more milk a body this heavy makes and holds: a step per weight stage,
        /// growing a little slower than her mass (about mass^0.38) all the way up to
        /// Gelatinous Ω, scaled by the milk-by-weight setting and capped by the max setting.
        /// A pawn without RimRound weight (a Nephila caste, with a mass of its own) is x1.
        /// </summary>
        public static float GetMilkMultiplierByWeight(Pawn p)
        {
            if (p?.RaceProps == null || !p.RaceProps.Humanlike || Utilities.HediffUtility.WeightHediff(p) == null)
                return 1f;

            float weightSeverity = Utilities.HediffUtility.KilosToSeverityWithBaseWeight(p.Weight());
            float stage = milkMultiplier[0].Second;
            foreach (Pair<float, float> step in milkMultiplier)
            {
                if (weightSeverity < step.First)
                    break;
                stage = step.Second;
            }

            float multiplier = 1 + (stage - 1) * GlobalSettings.milkMultiplierForWeight.threshold;
            return Mathf.Clamp(multiplier, 0, GlobalSettings.maxMilkMultiplier.threshold);
        }

        static List<Pair<float, float>> milkMultiplier = new List<Pair<float, float>>()
        {
            new Pair<float, float>( 0.000f, 0.90f  ),
            new Pair<float, float>( 0.005f, 0.95f  ),
            new Pair<float, float>( 0.015f, 1.00f  ),
            new Pair<float, float>( 0.035f, 1.05f  ),
            new Pair<float, float>( 0.050f, 1.05f  ),
            new Pair<float, float>( 0.065f, 1.10f  ),
            new Pair<float, float>( 0.090f, 1.15f  ),
            new Pair<float, float>( 0.120f, 1.20f  ),
            new Pair<float, float>( 0.155f, 1.25f  ),
            new Pair<float, float>( 0.200f, 1.35f  ),
            new Pair<float, float>( 0.230f, 1.40f  ),
            new Pair<float, float>( 0.280f, 1.45f  ),
            new Pair<float, float>( 0.350f, 1.60f  ),
            new Pair<float, float>( 0.430f, 1.75f  ),
            new Pair<float, float>( 0.535f, 1.90f  ),
            new Pair<float, float>( 0.660f, 2.15f  ),
            new Pair<float, float>( 0.800f, 2.40f  ),
            new Pair<float, float>( 0.965f, 2.70f  ),
            new Pair<float, float>( 1.160f, 3.05f  ),
            new Pair<float, float>( 1.410f, 3.50f  ),

            new Pair<float, float>( 1.860f, 4.00f  ),
            new Pair<float, float>( 2.460f, 4.50f  ),
            new Pair<float, float>( 2.960f, 5.00f  ),
            new Pair<float, float>( 3.960f, 5.50f  ),
            new Pair<float, float>( 4.960f, 6.00f  ),
            new Pair<float, float>( 6.460f, 6.75f  ),
            new Pair<float, float>( 7.960f, 7.50f  ),
            new Pair<float, float>( 9.960f, 8.50f  ),
            new Pair<float, float>( 14.46f, 10.0f  ),
            // past Gelatinous IX, still about mass^0.38
            new Pair<float, float>( 21.85f, 11.7f  ),
            new Pair<float, float>( 42.40f, 15.0f  ),
            new Pair<float, float>( 70.50f, 18.25f ),
            new Pair<float, float>( 116.6f, 22.1f  ),
            new Pair<float, float>( 164.5f, 25.2f  ),
            new Pair<float, float>( 286.2f, 31.1f  ),
            new Pair<float, float>( 411.0f, 35.7f  ),
            new Pair<float, float>( 576.7f, 40.5f  ),
            new Pair<float, float>( 773.2f, 45.3f  ),
            new Pair<float, float>( 999.999f, 50.0f ),
        };
    }
}
