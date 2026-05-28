using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimRound.AI
{
    // 1. Smothering: larger pawn (Chubby+, heavier) presses smaller pawn against a wall
    public class InteractionWorker_Smother : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (!CanSmother(initiator, recipient))
                return 0;

            var initAtt = initiator.TryGetComp<ThingComp_PawnAttitude>();
            if (initAtt is null)
                return 0;

            float weightBonus = GetWeightSeverity(initiator) * 3f;

            return initAtt.weightOpinion switch
            {
                WeightOpinion.NeutralPlus => 3f + weightBonus,
                WeightOpinion.Like => 6f + weightBonus,
                WeightOpinion.Love => 9f + weightBonus,
                WeightOpinion.Fanatical => 14f + weightBonus,
                _ => 0,
            };
        }

        static bool CanSmother(Pawn initiator, Pawn recipient)
        {
            if (initiator == recipient || !initiator.RaceProps.Humanlike || !recipient.RaceProps.Humanlike)
                return false;
            if (initiator.Inhumanized() || recipient.Inhumanized())
                return false;
            if (initiator.relations.OpinionOf(recipient) < 30)
                return false;

            float initSev = GetWeightSeverity(initiator);
            if (initSev < 0.09f)
                return false;

            float recSev = GetWeightSeverity(recipient);
            if (initSev <= recSev)
                return false;

            return initiator.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        }

        static float GetWeightSeverity(Pawn p)
        {
            return p.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight)?.Severity ?? 0;
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

            int stage = Mathf.Min(5, (int)((GetWeightSeverity(initiator) - 0.09f) * 5f));

            // Initiator always enjoys it
            initThought?.memories.TryGainMemory(
                ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_SmotheredInitiator, stage), recipient);

            // Recipient reaction
            switch (recAtt?.weightOpinion)
            {
                case WeightOpinion.Hate:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_SmotheredHate, stage), initiator);
                    break;
                case WeightOpinion.Dislike:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_SmotheredDislike, stage), initiator);
                    break;
                case WeightOpinion.NeutralMinus:
                case WeightOpinion.Neutral:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_SmotheredNeutral, stage), initiator);
                    break;
                case WeightOpinion.NeutralPlus:
                case WeightOpinion.Like:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_SmotheredGood, stage), initiator);
                    break;
                case WeightOpinion.Love:
                case WeightOpinion.Fanatical:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_SmotheredGreat, stage), initiator);
                    break;
            }

            AdjustIntimacy(initiator, recipient, stage);
        }

        static void AdjustIntimacy(Pawn initiator, Pawn recipient, int stage)
        {
            float relief = 0.08f + stage * 0.03f;
            var initIntimacy = initiator.needs?.AllNeeds?.FirstOrDefault(n => n.def.defName == "SEX_Intimacy");
            if (initIntimacy != null)
                initIntimacy.CurLevelPercentage -= relief * 0.5f;

            var recIntimacy = recipient.needs?.AllNeeds?.FirstOrDefault(n => n.def.defName == "SEX_Intimacy");
            if (recIntimacy != null)
            {
                var att = recipient.TryGetComp<ThingComp_PawnAttitude>();
                float mult = att?.weightOpinion switch
                {
                    WeightOpinion.Hate or WeightOpinion.Dislike => -0.4f,
                    WeightOpinion.NeutralMinus or WeightOpinion.Neutral => 0f,
                    _ => 1f,
                };
                recIntimacy.CurLevelPercentage += relief * mult;
            }
        }
    }

    // 2. Exploring: smaller pawn explores a larger pawn's curves
    public class InteractionWorker_Explore : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (!CanExplore(initiator, recipient))
                return 0;

            var initAtt = initiator.TryGetComp<ThingComp_PawnAttitude>();
            if (initAtt is null)
                return 0;

            float sizeDiff = GetWeightSeverity(recipient) - GetWeightSeverity(initiator);
            if (sizeDiff <= 0)
                return 0;

            return initAtt.weightOpinion switch
            {
                WeightOpinion.Like => 5f + sizeDiff * 4f,
                WeightOpinion.Love => 8f + sizeDiff * 6f,
                WeightOpinion.Fanatical => 12f + sizeDiff * 8f,
                _ => 0,
            };
        }

        static bool CanExplore(Pawn initiator, Pawn recipient)
        {
            if (initiator == recipient || !initiator.RaceProps.Humanlike || !recipient.RaceProps.Humanlike)
                return false;
            if (initiator.Inhumanized() || recipient.Inhumanized())
                return false;
            if (initiator.relations.OpinionOf(recipient) < 25)
                return false;

            float initSev = GetWeightSeverity(initiator);
            float recSev = GetWeightSeverity(recipient);
            if (recSev < 0.05f || recSev <= initSev)
                return false;

            return initiator.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        }

        static float GetWeightSeverity(Pawn p)
        {
            return p.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight)?.Severity ?? 0;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = new LookTargets(initiator, recipient);

            var recAtt = recipient.TryGetComp<ThingComp_PawnAttitude>();
            var initThought = initiator.needs?.mood?.thoughts;
            var recThought = recipient.needs?.mood?.thoughts;

            float sizeDiff = GetWeightSeverity(recipient) - GetWeightSeverity(initiator);
            int stage = Mathf.Min(5, (int)(sizeDiff * 3f));

            // Initiator explores - they enjoy it
            initThought?.memories.TryGainMemory(
                ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_ExploredInitiator, stage), recipient);

            // Recipient reaction
            switch (recAtt?.weightOpinion)
            {
                case WeightOpinion.Hate:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_ExploredHate, stage), initiator);
                    break;
                case WeightOpinion.Dislike:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_ExploredDislike, stage), initiator);
                    break;
                case WeightOpinion.NeutralMinus:
                case WeightOpinion.Neutral:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_ExploredNeutral, stage), initiator);
                    break;
                case WeightOpinion.NeutralPlus:
                case WeightOpinion.Like:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_ExploredGood, stage), initiator);
                    break;
                case WeightOpinion.Love:
                case WeightOpinion.Fanatical:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_ExploredGreat, stage), initiator);
                    break;
            }

            // Intimacy
            float relief = 0.06f + stage * 0.02f;
            var initIntimacy = initiator.needs?.AllNeeds?.FirstOrDefault(n => n.def.defName == "SEX_Intimacy");
            if (initIntimacy != null)
                initIntimacy.CurLevelPercentage -= relief * 0.4f;

            var recIntimacy = recipient.needs?.AllNeeds?.FirstOrDefault(n => n.def.defName == "SEX_Intimacy");
            if (recIntimacy != null)
            {
                var att = recipient.TryGetComp<ThingComp_PawnAttitude>();
                float mult = att?.weightOpinion switch
                {
                    WeightOpinion.Hate or WeightOpinion.Dislike => -0.3f,
                    _ => 1f,
                };
                recIntimacy.CurLevelPercentage += relief * 0.6f * mult;
            }
        }
    }

    // 3. Wet Smothering: lactating larger pawn force-feeds milk to a smaller pawn
    public class InteractionWorker_WetSmother : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (!CanWetSmother(initiator, recipient))
                return 0;

            var initAtt = initiator.TryGetComp<ThingComp_PawnAttitude>();
            if (initAtt is null)
                return 0;

            float weightBonus = GetWeightSeverity(initiator) * 3f;

            return initAtt.weightOpinion switch
            {
                WeightOpinion.Like => 4f + weightBonus,
                WeightOpinion.Love => 7f + weightBonus,
                WeightOpinion.Fanatical => 12f + weightBonus,
                _ => 0,
            };
        }

        static bool CanWetSmother(Pawn initiator, Pawn recipient)
        {
            if (initiator == recipient || !initiator.RaceProps.Humanlike || !recipient.RaceProps.Humanlike)
                return false;
            if (initiator.Inhumanized() || recipient.Inhumanized())
                return false;
            if (initiator.relations.OpinionOf(recipient) < 40)
                return false;

            float initSev = GetWeightSeverity(initiator);
            if (initSev < 0.09f)
                return false;

            float recSev = GetWeightSeverity(recipient);
            if (initSev <= recSev)
                return false;

            if (!initiator.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return false;

            // Must be lactating
            var lactating = initiator.health?.hediffSet?.GetFirstHediffOfDef(HediffDef.Named("Lactating"));
            if (lactating is null || lactating.Severity < 0.1f)
                return false;

            return true;
        }

        static float GetWeightSeverity(Pawn p)
        {
            return p.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight)?.Severity ?? 0;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = new LookTargets(initiator, recipient);

            float milkNutrition = 0.15f;
            var weight = initiator.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
            if (weight != null)
                milkNutrition += weight.Severity * 0.1f;

            var recFN = recipient.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (recFN != null && !recFN.Disabled)
            {
                float kilos = milkNutrition * 0.6f;
                recFN.activeWeightGainRequests.Enqueue(
                    new WeightGainRequest(kilos, Find.TickManager.TicksGame + 5, 6000, false));
            }

            var recAtt = recipient.TryGetComp<ThingComp_PawnAttitude>();
            var initAtt = initiator.TryGetComp<ThingComp_PawnAttitude>();
            var recThought = recipient.needs?.mood?.thoughts;
            var initThought = initiator.needs?.mood?.thoughts;

            int stage = Mathf.Min(5, (int)((GetWeightSeverity(initiator) - 0.09f) * 5f));

            // Initiator
            initThought?.memories.TryGainMemory(
                ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_WetSmotherInitiator, stage), recipient);

            // Recipient
            switch (recAtt?.weightOpinion)
            {
                case WeightOpinion.Hate:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_WetSmotherHate, stage), initiator);
                    break;
                case WeightOpinion.Dislike:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_WetSmotherDislike, stage), initiator);
                    break;
                case WeightOpinion.NeutralMinus:
                case WeightOpinion.Neutral:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_WetSmotherNeutral, stage), initiator);
                    break;
                case WeightOpinion.NeutralPlus:
                case WeightOpinion.Like:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_WetSmotherGood, stage), initiator);
                    break;
                case WeightOpinion.Love:
                case WeightOpinion.Fanatical:
                    recThought?.memories.TryGainMemory(
                        ThoughtMaker.MakeThought(RimRound.Defs.ThoughtDefOf.RR_WetSmotherGreat, stage), initiator);
                    break;
            }

            AdjustIntimacy(initiator, recipient, stage, milkNutrition);

            Messages.Message(
                $"{initiator.LabelShort} pressed {initiator.Possessive()} weight against {recipient.LabelShort}, " +
                $"forcing {initiator.Possessive()} milk down {recipient.Possessive()} throat!",
                new LookTargets(new Pawn[] { initiator, recipient }),
                recAtt?.weightOpinion switch
                {
                    WeightOpinion.Hate or WeightOpinion.Dislike => MessageTypeDefOf.NegativeEvent,
                    WeightOpinion.Love or WeightOpinion.Fanatical => MessageTypeDefOf.PositiveEvent,
                    _ => MessageTypeDefOf.NeutralEvent,
                });
        }

        static void AdjustIntimacy(Pawn initiator, Pawn recipient, int stage, float nutrition)
        {
            float relief = 0.1f + stage * 0.04f + nutrition * 0.1f;
            var initIntimacy = initiator.needs?.AllNeeds?.FirstOrDefault(n => n.def.defName == "SEX_Intimacy");
            if (initIntimacy != null)
                initIntimacy.CurLevelPercentage -= relief * 0.5f;

            var recIntimacy = recipient.needs?.AllNeeds?.FirstOrDefault(n => n.def.defName == "SEX_Intimacy");
            if (recIntimacy != null)
            {
                var att = recipient.TryGetComp<ThingComp_PawnAttitude>();
                float mult = att?.weightOpinion switch
                {
                    WeightOpinion.Hate or WeightOpinion.Dislike => -0.5f,
                    _ => 1f,
                };
                recIntimacy.CurLevelPercentage += relief * mult;
            }
        }
    }
}
