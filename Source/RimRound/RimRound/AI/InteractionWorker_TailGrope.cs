using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.AI
{
    public class InteractionWorker_TailGrope : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (initiator == recipient || !initiator.RaceProps.Humanlike || !recipient.RaceProps.Humanlike)
                return 0;
            if (initiator.Inhumanized() || recipient.Inhumanized())
                return 0;

            // Check if recipient is from a modded race that might have a tail
            // (HAR races with custom bodies)
            if (recipient.def is AlienRace.ThingDef_AlienRace)
            {
                if (initiator.relations.OpinionOf(recipient) < 25)
                    return 0;

                var initAtt = initiator.TryGetComp<Comps.ThingComp_PawnAttitude>();
                if (initAtt is null || initAtt.weightOpinion < WeightOpinion.NeutralPlus)
                    return 0;

                var recWeight = Utilities.HediffUtility.WeightHediff(recipient);
                float sev = recWeight?.Severity ?? 0;
                if (sev < 0.035f)
                    return 0;

                float bonus = sev * 4f;
                return initAtt.weightOpinion switch
                {
                    WeightOpinion.NeutralPlus => 2f + bonus,
                    WeightOpinion.Like => 4f + bonus,
                    WeightOpinion.Love => 6f + bonus,
                    WeightOpinion.Fanatical => 10f + bonus,
                    _ => 0,
                };
            }

            return 0;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, System.Collections.Generic.List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = new LookTargets(initiator, recipient);

            var recThought = recipient.needs?.mood?.thoughts;
            var initThought = initiator.needs?.mood?.thoughts;
            var recAtt = recipient.TryGetComp<Comps.ThingComp_PawnAttitude>();

            initThought?.memories.TryGainMemory(RimRound.Defs.ThoughtDefOf.RR_TailGropeInitiator, recipient);

            switch (recAtt?.weightOpinion)
            {
                case WeightOpinion.Hate:
                    recThought?.memories.TryGainMemory(ThoughtDef.Named("RR_TailGropeHate"), initiator);
                    break;
                case WeightOpinion.Dislike:
                    recThought?.memories.TryGainMemory(ThoughtDef.Named("RR_TailGropeDislike"), initiator);
                    break;
                case WeightOpinion.Neutral:
                case WeightOpinion.NeutralMinus:
                    recThought?.memories.TryGainMemory(ThoughtDef.Named("RR_TailGropeNeutral"), initiator);
                    break;
                case WeightOpinion.NeutralPlus:
                case WeightOpinion.Like:
                    recThought?.memories.TryGainMemory(ThoughtDef.Named("RR_TailGropeGood"), initiator);
                    break;
                case WeightOpinion.Love:
                case WeightOpinion.Fanatical:
                    recThought?.memories.TryGainMemory(ThoughtDef.Named("RR_TailGropeGreat"), initiator);
                    break;
            }
        }
    }
}
