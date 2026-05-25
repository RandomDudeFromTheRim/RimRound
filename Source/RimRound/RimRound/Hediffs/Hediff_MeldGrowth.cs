using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Hediffs
{
    public class Hediff_MeldGrowth : Hediff
    {
        private int tickCounter = 0;
        public float totalMeldMass;
        public float weightedBodySizeSum;
        private float _displaySeverity;

        public override float Severity
        {
            get
            {
                if (_displaySeverity < totalMeldMass)
                    _displaySeverity = totalMeldMass;
                return _displaySeverity;
            }
            set
            {
                if (value > 0)
                {
                    totalMeldMass = value;
                    _displaySeverity = value;
                    if (weightedBodySizeSum == 0)
                        weightedBodySizeSum = value * 1.0f;
                }
            }
        }

        public void AddContribution(float amount, float bodySize)
        {
            totalMeldMass += amount;
            weightedBodySizeSum += amount * bodySize;
        }

        public override void Tick()
        {
            base.Tick();
            if (pawn is null || pawn.Dead)
            {
                if (pawn != null)
                    pawn.health.RemoveHediff(this);
                return;
            }

            tickCounter++;
            if (tickCounter < 60)
                return;
            tickCounter = 0;

            if (totalMeldMass <= 0.001f)
            {
                pawn.health.RemoveHediff(this);
                return;
            }

            float avgBodySize = weightedBodySizeSum / totalMeldMass;

            float conversionRate = 0.01f + totalMeldMass * 0.03f;
            float converted = Mathf.Min(conversionRate, totalMeldMass);

            if (totalMeldMass > 0)
            {
                float ratio = (totalMeldMass - converted) / totalMeldMass;
                weightedBodySizeSum *= ratio;
            }
            totalMeldMass -= converted;

            float kilos = converted * avgBodySize * 55f;

            var fnd = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fnd != null && !fnd.Disabled)
            {
                fnd.activeWeightGainRequests.Enqueue(
                    new WeightGainRequest(kilos, Find.TickManager.TicksGame + 5, 0, false));
            }

            if (_displaySeverity > totalMeldMass)
                _displaySeverity = Mathf.Max(_displaySeverity - 0.002f, totalMeldMass);

            if (converted > 0)
                SpawnMote();

            AutoClot();
            ApplyMeldThought();
        }

        void SpawnMote()
        {
            if (pawn?.Map != null && pawn.IsHashIntervalTick(120))
                FleckMaker.ThrowSmoke(pawn.DrawPos, pawn.Map, Mathf.Min(totalMeldMass * 2f, 3f));
        }

        void AutoClot()
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
                return;
            var hediffs = pawn.health.hediffSet.hediffs;
            float tendQuality = Mathf.Min(0.2f + totalMeldMass * 0.3f, 0.9f);
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (hediffs[i].Bleeding)
                    hediffs[i].Tended(tendQuality, tendQuality, 1);
            }
        }

        void ApplyMeldThought()
        {
            if (pawn == null || pawn.Dead || pawn.needs?.mood == null)
                return;

            var att = pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (att == null)
                return;

            int stage = att.weightOpinion switch
            {
                WeightOpinion.Hate or WeightOpinion.Dislike => 0,
                WeightOpinion.NeutralMinus or WeightOpinion.Neutral => 1,
                WeightOpinion.NeutralPlus or WeightOpinion.Like => 2,
                WeightOpinion.Love => 3,
                WeightOpinion.Fanatical => 4,
                _ => 1,
            };

            var thoughtDef = ThoughtDef.Named("RR_MeldGrowthThought");
            if (thoughtDef != null)
            {
                var thought = ThoughtMaker.MakeThought(thoughtDef, stage);
                pawn.needs.mood.thoughts.memories.TryGainMemory(thought);
            }
        }

        public override string TipStringExtra
        {
            get
            {
                var att = pawn?.TryGetComp<ThingComp_PawnAttitude>();
                float kgPerCycle = totalMeldMass > 0
                    ? (0.01f + totalMeldMass * 0.03f) * (weightedBodySizeSum / totalMeldMass) * 55f
                    : 0;
                return att?.weightOpinion switch
                {
                    WeightOpinion.Hate or WeightOpinion.Dislike =>
                        $"Meld mass: {totalMeldMass:F2} | +{kgPerCycle:F1} kg/cycle\nI can feel it GROWING inside me! Get it out!",
                    WeightOpinion.NeutralMinus or WeightOpinion.Neutral =>
                        $"Meld mass: {totalMeldMass:F2} | +{kgPerCycle:F1} kg/cycle\nSomething is making me expand from within...",
                    WeightOpinion.NeutralPlus or WeightOpinion.Like =>
                        $"Meld mass: {totalMeldMass:F2} | +{kgPerCycle:F1} kg/cycle\nThe warmth spreading through me feels... nice?",
                    WeightOpinion.Love or WeightOpinion.Fanatical =>
                        $"Meld mass: {totalMeldMass:F2} | +{kgPerCycle:F1} kg/cycle\nYES! Fill me! I want MORE of this void growth!",
                    _ => $"Meld mass: {totalMeldMass:F2} | +{kgPerCycle:F1} kg/cycle\nSomething is inside me.",
                };
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref totalMeldMass, "totalMeldMass");
            Scribe_Values.Look(ref weightedBodySizeSum, "weightedBodySizeSum");
            Scribe_Values.Look(ref _displaySeverity, "displaySeverity");
        }
    }
}
