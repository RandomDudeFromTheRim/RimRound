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

    public class Hediff_RRBorrowedStomach : HediffWithComps
    {
        const float CapacityPerDose = 0.6f;
        const float DigestionPenaltyPerDose = 0.15f;

        public int Doses => (int)Severity;

        public void AddDose()
        {
            Severity++;
            ChangeBonuses(1);
            GetComp<HediffComp_Disappears>().SetDuration(Doses == 1 ? GenDate.TicksPerDay : 6 * GenDate.TicksPerHour);
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            ChangeBonuses(-Doses);
            if (!pawn.Dead && pawn.TryGetComp<FullnessAndDietStats_ThingComp>() is FullnessAndDietStats_ThingComp stomach
                && !stomach.Disabled && stomach.CurrentFullness > stomach.HardLimit)
            {
                Messages.Message($"{pawn.LabelShort}'s borrowed stomach has collapsed while overfull.", pawn,
                    MessageTypeDefOf.NegativeHealthEvent);
            }
        }

        void ChangeBonuses(int doses) =>
            StatChangeUtility.ChangeRimRoundStats(pawn, new RimRoundStatBonuses
            {
                softLimitMultiplier = CapacityPerDose * doses,
                digestionRateMultiplier = -DigestionPenaltyPerDose * doses,
            });

        public override string LabelInBrackets => $"{Doses} dose(s)";

        public override string TipStringExtra =>
            $"{base.TipStringExtra}\nStomach capacity +{CapacityPerDose * Doses:P0}; digestion −{DigestionPenaltyPerDose * Doses:P0}.\n" +
            "When the effect ends, everything still in the stomach stays there. Stop feeding early enough to digest the excess.";
    }

    public class CompUseEffect_RRBorrowedStomach : CompUseEffect
    {
        static HediffDef Def => DefDatabase<HediffDef>.GetNamed("RR_BorrowedStomach");

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            var stomach = p.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (stomach == null || stomach.Disabled)
                return $"{p.LabelShort} has no stomach for it to alter.";
            if (p.health.hediffSet.HasHediff(HediffDefOf.DarkPsychicShock))
                return $"{p.LabelShort} is in dark psychic shock.";
            return base.CanBeUsedBy(p);
        }

        public override void DoEffect(Pawn usedBy)
        {
            var report = CanBeUsedBy(usedBy);
            if (!report.Accepted)
                return;

            var health = usedBy.health;
            var borrowed = health.hediffSet.GetFirstHediffOfDef(Def) as Hediff_RRBorrowedStomach;
            if (borrowed?.Doses >= 2)
            {
                health.RemoveHediff(borrowed);
                health.AddHediff(HediffDefOf.DarkPsychicShock);
                Messages.Message($"A third dose snapped {usedBy.LabelShort}'s borrowed stomach shut and sent {usedBy.Possessive()} mind into shock.",
                    usedBy, MessageTypeDefOf.NegativeHealthEvent);
                return;
            }

            if (borrowed == null)
            {
                borrowed = (Hediff_RRBorrowedStomach)HediffMaker.MakeHediff(Def, usedBy);
                health.AddHediff(borrowed);
            }

            borrowed.AddDose();
            if (borrowed.Doses == 2)
                Messages.Message($"{usedBy.LabelShort} has six hours before the borrowed stomach collapses. A third dose would end it immediately.",
                    usedBy, MessageTypeDefOf.CautionInput);
        }
    }
}
