using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using RRHediffs = RimRound.Utilities.HediffUtility;

namespace RimRound.Rituals
{
    /// <summary>
    /// Lipophagy: philophagy's gluttonous cousin. The invoker psychically draws the fat out of
    /// the target's body and into their own - a share of everything the target carries above
    /// their base weight, more with a better ritual. The target falls into dark psychic shock,
    /// and remembers who did it.
    /// </summary>
    public class PsychicRitualDef_RRLipophagy : PsychicRitualDef_InvocationCircle
    {
        /// <summary>Under this many kilos of fat there's nothing worth drawing.</summary>
        public const float MinKilos = 5f;

        public SimpleCurve fractionFromQualityCurve;

        public override List<PsychicRitualToil> CreateToils(PsychicRitual psychicRitual, PsychicRitualGraph parent)
        {
            List<PsychicRitualToil> list = base.CreateToils(psychicRitual, parent);
            list.Add(new PsychicRitualToil_RRLipophagy(InvokerRole, TargetRole));
            list.Add(new PsychicRitualToil_TargetCleanup(InvokerRole, TargetRole));
            return list;
        }

        /// <summary>Kilos above base weight - the fat there is to draw.</summary>
        public static float Excess(Pawn p) =>
            RRHediffs.WeightHediff(p) is Hediff w ? RRHediffs.SeverityToKilosWithoutBaseWeight(w.Severity) : 0f;

        public override TaggedString OutcomeDescription(FloatRange qualityRange, string qualityNumber, PsychicRitualRoleAssignments assignments)
        {
            float fraction = fractionFromQualityCurve.Evaluate(qualityRange.min);
            TaggedString result = outcomeDescription.Formatted(fraction.ToStringPercent());
            Pawn invoker = assignments.FirstAssignedPawn(InvokerRole);
            Pawn target = assignments.FirstAssignedPawn(TargetRole);
            if (invoker != null && target != null)
                result += $"\n\n{invoker.LabelShort} would draw about {Excess(target) * fraction:F0} kg out of {target.LabelShort}.";
            return result;
        }

        public override IEnumerable<string> BlockingIssues(PsychicRitualRoleAssignments assignments, Map map)
        {
            foreach (string issue in base.BlockingIssues(assignments, map))
                yield return issue;
            Pawn target = assignments.FirstAssignedPawn(TargetRole);
            if (target != null && Excess(target) < MinKilos)
                yield return $"{target.LabelShort} has no fat to draw.";
            Pawn invoker = assignments.FirstAssignedPawn(InvokerRole);
            if (invoker != null && RRHediffs.WeightHediff(invoker) == null)
                yield return $"{invoker.LabelShort} can't hold any of it.";
        }

        public override IEnumerable<string> GetPawnTooltipExtras(Pawn pawn)
        {
            if (RRHediffs.WeightHediff(pawn) != null)
                yield return $"Weight: {RRHediffs.Weight(pawn):F0} kg ({Excess(pawn):F0} kg to draw)";
        }
    }

    public class PsychicRitualToil_RRLipophagy : PsychicRitualToil
    {
        PsychicRitualRoleDef invokerRole;
        PsychicRitualRoleDef targetRole;

        protected PsychicRitualToil_RRLipophagy()
        {
        }

        public PsychicRitualToil_RRLipophagy(PsychicRitualRoleDef invokerRole, PsychicRitualRoleDef targetRole)
        {
            this.invokerRole = invokerRole;
            this.targetRole = targetRole;
        }

        public override void Start(PsychicRitual psychicRitual, PsychicRitualGraph parent)
        {
            base.Start(psychicRitual, parent);
            Pawn invoker = psychicRitual.assignments.FirstAssignedPawn(invokerRole);
            Pawn target = psychicRitual.assignments.FirstAssignedPawn(targetRole);
            if (invoker != null && target != null)
                ApplyOutcome(psychicRitual, invoker, target);
        }

        void ApplyOutcome(PsychicRitual psychicRitual, Pawn invoker, Pawn target)
        {
            var def = (PsychicRitualDef_RRLipophagy)psychicRitual.def;
            float fraction = def.fractionFromQualityCurve.Evaluate(psychicRitual.PowerPercent);
            float targetBefore = RRHediffs.Weight(target);
            float invokerBefore = RRHediffs.Weight(invoker);
            float kilos = PsychicRitualDef_RRLipophagy.Excess(target) * fraction;

            // queued, so both bodies redraw (and bwomf) as it lands
            RRHediffs.QueueExactWeightChange(target, -kilos);
            RRHediffs.QueueExactWeightChange(invoker, kilos);
            if (target.needs?.food is Need_Food food)
                food.CurLevel = 0f;

            target.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.PsychicRitualVictim);
            ThoughtDef drained = DefDatabase<ThoughtDef>.GetNamedSilentFail("RR_DrainedMyFat");
            if (drained != null)
                target.needs?.mood?.thoughts?.memories?.TryGainMemory(drained, invoker);
            foreach (Pawn p in psychicRitual.assignments.AllAssignedPawns.Except(target))
                target.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.UsedMeForPsychicRitual, p);
            target.health.AddHediff(HediffDefOf.DarkPsychicShock);
            PsychicRitualUtility.AddPsychicRitualGuiltToPawns(psychicRitual.def, psychicRitual.Map.mapPawns.FreeColonistsSpawned.Where(p => p != target));

            FilthMaker.TryMakeFilth(target.PositionHeld, target.MapHeld, ThingDefOf.Filth_Blood, 2);
            string text = $"{invoker.LabelShort} has drawn the fat out of {target.LabelShort}'s body and into {invoker.Possessive()} own.\n\n"
                + $"{invoker.LabelShort}: {invokerBefore:F0} kg -> {invokerBefore + kilos:F0} kg\n"
                + $"{target.LabelShort}: {targetBefore:F0} kg -> {targetBefore - kilos:F0} kg\n\n"
                + $"{target.LabelShort} has fallen into dark psychic shock, hollow and starving.";
            Find.LetterStack.ReceiveLetter("PsychicRitualCompleteLabel".Translate(psychicRitual.def.label), text, LetterDefOf.NeutralEvent, new LookTargets(invoker, target));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref invokerRole, "invokerRole");
            Scribe_Defs.Look(ref targetRole, "targetRole");
        }
    }
}
