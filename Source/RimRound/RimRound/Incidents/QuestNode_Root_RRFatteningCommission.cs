using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace RimRound.Incidents
{
    /// <summary>
    /// Fattening commission: a friendly faction sends one of its people - a willing
    /// weight-lover - to stay with you until they reach a target size. The bigger the
    /// target, the longer you get and the bigger the reward; from the Empire the reward
    /// can be honor. Checked when the stay is up (QuestPart_RRWeightGoal): short of the
    /// target, the guest goes home disappointed and the faction thinks less of you;
    /// losing them costs more.
    /// </summary>
    public class QuestNode_Root_RRFatteningCommission : QuestNode
    {
        public class Tier
        {
            public string label;
            /// <summary>The body type the guest must outgrow: reaching the target means being past this.</summary>
            public string pastBodyType;
            public int days;
            public float rewardValue;
            public float weight;

            public Tier(string label, string pastBodyType, int days, float rewardValue, float weight)
            {
                this.label = label;
                this.pastBodyType = pastBodyType;
                this.days = days;
                this.rewardValue = rewardValue;
                this.weight = weight;
            }
        }

        static readonly Tier[] Tiers =
        {
            new Tier("Obese",        "F_030_Fat",           8,  700f, 3f),
            new Tier("Lardy",        "F_050_MorbidlyObese", 12, 1300f, 3f),
            new Tier("Gigantic",     "F_070_Enormous",      16, 2100f, 2f),
            new Tier("Titanic",      "F_080_Gigantic",      20, 3000f, 1.5f),
            new Tier("Gelatinous I", "F_090_Titanic",       25, 4000f, 1f),
        };

        const int FailGoodwill = -10;
        const int LostGoodwill = -30;

        static IEnumerable<Faction> Candidates() =>
            Find.FactionManager.AllFactionsVisible.Where(f =>
                !f.IsPlayer && !f.defeated && !f.temporary && f.def.humanlikeFaction &&
                !f.HostileTo(Faction.OfPlayer) && GuestKinds(f).Any());

        /// <summary>
        /// Who a faction might send: an ordinary member from its usual groups - no
        /// leaders, no titled nobles. (basicMemberKind is only set on player and a few
        /// special factions, so it can't be relied on.)
        /// </summary>
        static IEnumerable<PawnKindDef> GuestKinds(Faction f)
        {
            if (f.def.basicMemberKind != null)
                return new[] { f.def.basicMemberKind };
            if (f.def.pawnGroupMakers == null)
                return Enumerable.Empty<PawnKindDef>();
            return f.def.pawnGroupMakers
                .SelectMany(g => g.options.Concat(g.traders ?? Enumerable.Empty<PawnGenOption>()))
                .Select(o => o.kind)
                .Where(k => k?.RaceProps != null && k.RaceProps.Humanlike && !k.factionLeader
                    && k.titleRequired == null && k.titleSelectOne.NullOrEmpty())
                .Distinct();
        }

        static PawnKindDef GuestKind(Faction f) =>
            GuestKinds(f).RandomElementByWeight(k => 1f / Mathf.Max(20f, k.combatPower));

        protected override bool TestRunInt(Slate slate)
        {
            return QuestGen_Get.GetMap() != null && Candidates().Any();
        }

        protected override void RunInt()
        {
            Quest quest = QuestGen.quest;
            Slate slate = QuestGen.slate;
            Map map = QuestGen_Get.GetMap();

            // the Empire is a likelier client: it pays in honor
            Faction faction = Candidates().RandomElementByWeight(f => f.def.HasRoyalTitles ? 3f : 1f);
            Tier tier = Tiers.RandomElementByWeight(t => t.weight);
            int durationTicks = tier.days * GenDate.TicksPerDay;

            Pawn guest = quest.GeneratePawn(GuestKind(faction), faction, allowAddictions: false, forceGenerateNewPawn: true);
            // they asked for this: make sure they love it
            guest.TryGetComp<ThingComp_PawnAttitude>()?.SetWeightOpinion(Rand.Bool ? WeightOpinion.Love : WeightOpinion.Like);
            var pawns = new List<Pawn> { guest };
            slate.Set("lodgers", pawns);
            slate.Set("asker", guest);

            quest.ExtraFaction(faction, pawns, ExtraFactionType.HomeFaction);
            quest.PawnsArrive(pawns, null, map.Parent, null, joinPlayer: true,
                customLetterLabel: "Guest arrived: [asker_nameShort]",
                customLetterText: "[asker_nameDef] of [faction_name] has arrived. [asker_pronoun] is yours to feed for [questDurationTicks_duration] - [asker_pronoun] needs to be [targetLabel] by the time [asker_pronoun] leaves.");

            // rewards, handed over if they go home big enough
            string reached = QuestGen.GenerateNewSignal("RR_TargetReached");
            string missed = QuestGen.GenerateNewSignal("RR_TargetMissed");
            quest.GiveRewards(new RewardsGeneratorParams
            {
                rewardValue = tier.rewardValue * Find.Storyteller.difficulty.EffectiveQuestRewardValueFactor,
                giverFaction = faction,
                allowGoodwill = true,
                allowRoyalFavor = faction.def.HasRoyalTitles,
            }, reached, asker: guest);

            // when the stay is up: weigh them and send them home
            quest.Delay(durationTicks, delegate
            {
                quest.AddPart(new QuestPart_RRWeightGoal
                {
                    inSignal = QuestGen.slate.Get<string>("inSignal"),
                    pawn = guest,
                    pastBodyTypeDefName = tier.pastBodyType,
                    targetLabel = tier.label,
                    outSignalReached = reached,
                    outSignalMissed = missed,
                });
            }, null, null, null, reactivatable: false, null, null, isQuestTimeout: false, "Leaves in", "Leaves on", "RR_FatteningCommissionDelay");

            // progress, shown on the guest
            quest.AddPart(new QuestPart_RRWeightGoalProgress
            {
                pawn = guest,
                pastBodyTypeDefName = tier.pastBodyType,
                targetLabel = tier.label,
            });

            quest.End(QuestEndOutcome.Success, 0, null, reached, QuestPart.SignalListenMode.OngoingOnly, sendStandardLetter: true);
            quest.End(QuestEndOutcome.Fail, FailGoodwill, faction, missed, QuestPart.SignalListenMode.OngoingOnly, sendStandardLetter: true);

            // losing the guest before then
            foreach (string sig in new[] { "lodgers.Destroyed", "lodgers.Arrested", "lodgers.Kidnapped", "lodgers.Banished", "lodgers.LeftMap", "lodgers.BecameMutant" })
                quest.End(QuestEndOutcome.Fail, LostGoodwill, faction, QuestGenUtility.HardcodedSignalWithQuestID(sig), QuestPart.SignalListenMode.OngoingOnly, sendStandardLetter: true);

            slate.Set("faction", faction);
            slate.Set("map", map);
            slate.Set("questDurationTicks", durationTicks);
            slate.Set("targetLabel", tier.label);
            slate.Set("rewardNote", faction.def.HasRoyalTitles
                ? "The Empire rewards this sort of hospitality with honor."
                : "The bigger they go home, the more grateful their people will be.");
        }
    }

    /// <summary>When the stay is up: did the guest reach their target size? Then they go home.</summary>
    public class QuestPart_RRWeightGoal : QuestPart
    {
        public string inSignal;
        public Pawn pawn;
        public string pastBodyTypeDefName;
        public string targetLabel;
        public string outSignalReached;
        public string outSignalMissed;

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                if (pawn != null)
                    yield return pawn;
            }
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || pawn == null || pawn.Dead)
                return;

            BodyTypeDef past = DefDatabase<BodyTypeDef>.GetNamedSilentFail(pastBodyTypeDefName);
            bool reached = past != null && BodyTypeUtility.PawnIsOverWeightThreshold(pawn, past);

            Find.LetterStack.ReceiveLetter(
                reached ? $"{pawn.LabelShort} reached {targetLabel}" : $"{pawn.LabelShort} fell short",
                reached
                    ? $"{pawn.LabelShort}'s stay is over, and {pawn.ProSubj()} is going home {targetLabel} - just as {pawn.Possessive()} people hoped. They're sending your reward."
                    : $"{pawn.LabelShort}'s stay is over, but {pawn.ProSubj()} never reached {targetLabel}. {pawn.Possessive().CapitalizeFirst()} people are disappointed.",
                reached ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent,
                pawn.Spawned ? new LookTargets(pawn) : LookTargets.Invalid, null, quest);

            SendHome(pawn, quest);
            Find.SignalManager.SendSignal(new Signal(reached ? outSignalReached : outSignalMissed));
        }

        /// <summary>
        /// Off they go. A guest who has got too big to walk is collected: a transport
        /// swoops down and hauls them home.
        /// </summary>
        static void SendHome(Pawn pawn, Quest quest)
        {
            LeaveQuestPartUtility.MakePawnsLeave(new[] { pawn }, sendLetter: false, quest, wakeUp: true);
            if (pawn.Spawned && pawn.Downed)
            {
                Map map = pawn.Map;
                IntVec3 at = pawn.Position;
                FleckMaker.ThrowSmoke(pawn.DrawPos, map, 2f);
                FleckMaker.ThrowDustPuffThick(pawn.DrawPos, map, 2f, Color.white);
                pawn.DeSpawn();
                if (!Find.WorldPawns.Contains(pawn))
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                Messages.Message($"A transport swoops down to collect {pawn.LabelShort}, who is far too big to walk home.", new TargetInfo(at, map), MessageTypeDefOf.NeutralEvent);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref pastBodyTypeDefName, "pastBodyType");
            Scribe_Values.Look(ref targetLabel, "targetLabel");
            Scribe_Values.Look(ref outSignalReached, "outSignalReached");
            Scribe_Values.Look(ref outSignalMissed, "outSignalMissed");
        }
    }

    /// <summary>Shows how far along the guest is, in the quest's description.</summary>
    public class QuestPart_RRWeightGoalProgress : QuestPart
    {
        public Pawn pawn;
        public string pastBodyTypeDefName;
        public string targetLabel;

        public override string DescriptionPart
        {
            get
            {
                if (pawn == null || quest == null || quest.State != QuestState.Ongoing)
                    return null;
                BodyTypeDef past = DefDatabase<BodyTypeDef>.GetNamedSilentFail(pastBodyTypeDefName);
                bool reached = past != null && BodyTypeUtility.PawnIsOverWeightThreshold(pawn, past);
                string now = pawn.story?.bodyType?.defName is string d && d.Length > 6 ? d.Substring(6).Replace('_', ' ') : "?";
                return reached
                    ? $"{pawn.LabelShort} has reached {targetLabel} (now {now})."
                    : $"{pawn.LabelShort} is {now} - still short of {targetLabel}.";
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref pastBodyTypeDefName, "pastBodyType");
            Scribe_Values.Look(ref targetLabel, "targetLabel");
        }
    }
}
