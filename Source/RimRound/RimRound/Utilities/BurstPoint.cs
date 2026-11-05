using RimRound.Comps;
using RimRound.Hediffs;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    /// <summary>
    /// How much weight a pawn can take before they burst, and what has to happen to get
    /// them there.
    ///
    /// One owner, two consumers: the gorge constrictor pumps kilos into a victim and
    /// bursts them on passing the top of <see cref="BodyType"/>; vore counts the mass a
    /// predator is carrying against the same limit, so a small pawn that swallows a big
    /// one is in exactly the trouble a constrictor would be.
    ///
    /// A pawn bursts on outgrowing the highest body type they can still reach. The base is
    /// the last body type below Gelatinous I; each doubling of the stomach's hard limit
    /// over the default (perks like Black Hole and Limit Break) moves it one Gelatinous
    /// tier on, and a body that has survived being pumped past its limit keeps the extra
    /// reach it gained (<see cref="Hediff_RROverfilled.StretchedLevel"/>).
    /// </summary>
    public static class BurstPoint
    {
        static readonly string[] Tiers =
        {
            "F_090_Titanic", "F_100_Gelatinous", "F_150_Gelatinous", "F_200_Gelatinous", "F_250_Gelatinous",
            "F_300_Gelatinous", "F_350_Gelatinous", "F_400_Gelatinous", "F_450_Gelatinous", "F_500_Gelatinous",
        };

        /// <summary>
        /// How many extra Gelatinous tiers this pawn gets before bursting. A fluffy body
        /// stretches: Expies reach one tier further than anyone else.
        /// </summary>
        public static int ExtraTiers(Pawn p)
        {
            if (!ModsConfig.BiotechActive || p?.genes == null)
                return 0;

            // Erin's Experiment: the xenotype, not the race - they are humanlike with genes
            return p.genes.Xenotype?.defName == "ERN_Expie" ? 1 : 0;
        }

        /// <summary>The body type this pawn bursts on outgrowing: Titanic, or later for a pawn whose perks let them take more.</summary>
        public static BodyTypeDef BodyType(Pawn p, int extraTiers = 0)
        {
            int tier = extraTiers + ExtraTiers(p) + Hediff_RROverfilled.StretchedLevel(p);
            var fnd = p.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fnd != null && !fnd.Disabled)
            {
                float baseline = FullnessAndDietStats_ThingComp.defaultSoftLimit * 1.3f;
                tier += Mathf.FloorToInt(Mathf.Log(Mathf.Max(1f, fnd.HardLimit / baseline), 2f));
            }
            tier = Mathf.Clamp(tier, 0, Tiers.Length - 1);
            for (; tier > 0; tier--)
            {
                BodyTypeDef def = DefDatabase<BodyTypeDef>.GetNamedSilentFail(Tiers[tier]);
                if (def != null && RacialBodyTypeInfoUtility.defaultFemaleSet.ContainsKey(def))
                    return def;
            }
            return Defs.BodyTypeDefOf.F_090_Titanic;
        }

        /// <summary>Weight severity at which this pawn bursts, for its race.</summary>
        public static float Severity(Pawn p, int extraTiers = 0)
        {
            float max = RacialBodyTypeInfoUtility.defaultFemaleSet[BodyType(p, extraTiers)].maxSeverity;
            return max * RacialBodyTypeInfoUtility.GetBodyTypeWeightRequirementMultiplier(p);
        }

        public static float WeightSeverity(Pawn p) => HediffUtility.WeightHediff(p)?.Severity ?? 0f;

        /// <summary>Kilos still to go before this pawn reaches their burst point.</summary>
        public static float KilosRemaining(Pawn p, int extraTiers = 0) =>
            Mathf.Max(0f, HediffUtility.SeverityToKilosWithoutBaseWeight(Severity(p, extraTiers) - WeightSeverity(p)));

        public static bool IsPast(Pawn p, int extraTiers = 0) => BodyTypeUtility.PawnIsOverWeightThreshold(p, BodyType(p, extraTiers));

        /// <summary>
        /// Ghouls and the void-touched knit back together faster than anything can split
        /// them: nothing bursts them, it just keeps going until it runs dry.
        /// </summary>
        public static bool CannotBurst(Pawn p) =>
            ModsConfig.AnomalyActive && p != null
            && (p.IsGhoul || (RimWorld.HediffDefOf.VoidTouched != null && p.health.hediffSet.HasHediff(RimWorld.HediffDefOf.VoidTouched)));
    }
}
