using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimRound.AI
{
    public class InteractionWorker_Fondle : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (initiator == recipient || !initiator.RaceProps.Humanlike || !recipient.RaceProps.Humanlike)
                return 0;

            if (initiator.Inhumanized() || recipient.Inhumanized())
                return 0;

            if (!CloseContactUtility.InTouchRange(initiator, recipient))
                return 0;

            if (!initiator.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return 0;

            var initAtt = initiator.TryGetComp<ThingComp_PawnAttitude>();
            if (initAtt is null || initAtt.weightOpinion < WeightOpinion.Neutral)
                return 0;

            if (initiator.relations.OpinionOf(recipient) < 20)
                return 0;

            var recWeight = recipient.health.hediffSet.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
            if (recWeight is null || recWeight.Severity < 0.035f)
                return 0;

            float weightBonus = recWeight.Severity * 2f;

            switch (initAtt.weightOpinion)
            {
                case WeightOpinion.Neutral:
                case WeightOpinion.NeutralPlus:
                    return 1.5f + weightBonus;
                case WeightOpinion.Like:
                    return 3f + weightBonus;
                case WeightOpinion.Love:
                    return 5f + weightBonus;
                case WeightOpinion.Fanatical:
                    return 8f + weightBonus;
                default:
                    return 0;
            }
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = new LookTargets(initiator, recipient);

            var recAtt = recipient.TryGetComp<ThingComp_PawnAttitude>();
            var initAtt = initiator.TryGetComp<ThingComp_PawnAttitude>();
            var recThought = recipient.needs?.mood?.thoughts;
            var initThought = initiator.needs?.mood?.thoughts;

            int stage = GetFondleStage(recipient);

            switch (recAtt?.weightOpinion)
            {
                case WeightOpinion.Hate:
                    recThought?.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_FondledHate, initiator);
                    break;
                case WeightOpinion.Dislike:
                    recThought?.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_FondledDislike, initiator);
                    break;
                case WeightOpinion.NeutralMinus:
                case WeightOpinion.Neutral:
                    recThought?.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_FondledNeutral, initiator);
                    break;
                case WeightOpinion.NeutralPlus:
                case WeightOpinion.Like:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_FondledGood, stage), initiator);
                    break;
                case WeightOpinion.Love:
                case WeightOpinion.Fanatical:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_FondledGreat, stage), initiator);
                    break;
            }

            initThought?.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_FondledInitiator, recipient);

            if (initAtt?.weightOpinion >= WeightOpinion.Fanatical)

            AdjustIntimacyNeed(initiator, recipient, stage);
        }

        static int GetFondleStage(Pawn recipient)
        {
            var weight = recipient.health.hediffSet.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
            if (weight is null)
                return 0;

            float sev = weight.Severity;
            if (sev < 0.05f) return 0;
            if (sev < 0.12f) return 1;
            if (sev < 0.28f) return 2;
            if (sev < 0.66f) return 3;
            if (sev < 1.86f) return 4;
            return 5;
        }

        static void AdjustIntimacyNeed(Pawn initiator, Pawn recipient, int stage)
        {
            float relief = 0.05f + (stage * 0.03f);

            if (recipient.needs?.AllNeeds != null)
            {
                var intimacy = recipient.needs.AllNeeds.Find(n => n.def.defName == "SEX_Intimacy");
                if (intimacy != null)
                {
                    var att = recipient.TryGetComp<ThingComp_PawnAttitude>();
                    float multiplier = att?.weightOpinion switch
                    {
                        WeightOpinion.Hate or WeightOpinion.Dislike => -0.5f,
                        WeightOpinion.NeutralMinus or WeightOpinion.Neutral => 0f,
                        _ => 1f,
                    };
                    if (att?.weightOpinion >= WeightOpinion.NeutralPlus)
                        intimacy.CurLevelPercentage += relief * multiplier;
                    else if (att?.weightOpinion <= WeightOpinion.Dislike)
                        intimacy.CurLevelPercentage += relief * multiplier;
                }
            }

            if (initiator.needs?.AllNeeds != null)
            {
                var initIntimacy = initiator.needs.AllNeeds.Find(n => n.def.defName == "SEX_Intimacy");
                if (initIntimacy != null)
                {
                    initIntimacy.CurLevelPercentage -= relief * 0.5f;
                }
            }
        }
    }
}
