using System.Text;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Hediffs
{
    /// <summary>
    /// Stretch serum: each dose permanently stretches the stomach a little -
    /// more soft capacity, more give before it's too full. Stacks up to MaxDoses;
    /// the bonuses sit in the pawn's RimRound stat bonuses like an implant's and
    /// come back off if the hediff is ever removed.
    /// </summary>
    public class Hediff_RRStretchSerum : HediffWithComps
    {
        public const int MaxDoses = 5;
        const float SoftLimitPerDose = 0.12f;
        const float ElasticityPerDose = 0.1f;

        int doses;

        public int Doses => doses;

        public bool AddDose()
        {
            if (doses >= MaxDoses)
                return false;
            doses++;
            Severity = doses;
            StatChangeUtility.ChangeRimRoundStats(pawn, new RimRoundStatBonuses
            {
                softLimitMultiplier = SoftLimitPerDose,
                stomachElasticityMultiplier = ElasticityPerDose,
            });
            return true;
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            StatChangeUtility.ChangeRimRoundStats(pawn, new RimRoundStatBonuses
            {
                softLimitMultiplier = -SoftLimitPerDose * doses,
                stomachElasticityMultiplier = -ElasticityPerDose * doses,
            });
        }

        public override string LabelInBrackets => $"{doses}/{MaxDoses}";

        public override string TipStringExtra
        {
            get
            {
                var sb = new StringBuilder(base.TipStringExtra);
                sb.AppendLineIfNotEmpty().Append($"Stomach capacity +{SoftLimitPerDose * doses:P0}");
                sb.AppendLine().Append($"Stomach elasticity +{ElasticityPerDose * doses:P0}");
                return sb.ToString();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref doses, "doses");
        }
    }

    /// <summary>Injecting a stretch serum: one more dose, up to the limit.</summary>
    public class CompUseEffect_RRStretchSerum : CompUseEffect
    {
        static HediffDef Def => DefDatabase<HediffDef>.GetNamed("RR_StretchSerum");

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            var fnd = p.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fnd == null || fnd.Disabled)
                return $"{p.LabelShort} has no stomach for it to stretch.";
            if (p.health.hediffSet.GetFirstHediffOfDef(Def) is Hediff_RRStretchSerum h && h.Doses >= Hediff_RRStretchSerum.MaxDoses)
                return $"{p.LabelShort}'s stomach won't stretch any further.";
            return base.CanBeUsedBy(p);
        }

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            var h = usedBy.health.hediffSet.GetFirstHediffOfDef(Def) as Hediff_RRStretchSerum;
            if (h == null)
            {
                h = (Hediff_RRStretchSerum)HediffMaker.MakeHediff(Def, usedBy);
                usedBy.health.AddHediff(h);
            }
            if (h.AddDose())
                Messages.Message($"{usedBy.LabelShort}'s stomach stretches ({h.Doses}/{Hediff_RRStretchSerum.MaxDoses} doses).", usedBy, MessageTypeDefOf.PositiveEvent);
        }
    }
}
