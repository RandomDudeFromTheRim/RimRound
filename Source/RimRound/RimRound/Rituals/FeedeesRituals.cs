using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.Incidents;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Rituals
{
    /// <summary>
    /// Helpers for the Feedees meme's rituals and role: who counts as a weight-liker,
    /// who is big enough to worship or to call the seam, and how heavy a pawn is.
    /// </summary>
    public static class FeedeesRitualUtility
    {
        public const string FeedeeRoleDefName = "RR_IdeoRole_Feedee";

        public static WeightOpinion Opinion(Pawn p) => p.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.None;

        public static bool LikesWeight(Pawn p) => Opinion(p) >= WeightOpinion.Like;

        public static bool AtLeast(Pawn p, BodyTypeDef bodyType) => BodyTypeUtility.PawnIsOverWeightThreshold(p, bodyType);

        /// <summary>Weight severity, normalised for the pawn's race so a big ratkin counts like a big human.</summary>
        public static float NormalisedSize(Pawn p)
        {
            float sev = Utilities.HediffUtility.WeightHediff(p)?.Severity ?? 0f;
            return sev / Mathf.Max(0.01f, RacialBodyTypeInfoUtility.GetBodyTypeWeightRequirementMultiplier(p));
        }

        public static float Kilos(Pawn p) => Utilities.HediffUtility.SeverityToKilosWithoutBaseWeight(Utilities.HediffUtility.WeightHediff(p)?.Severity ?? 0f);
    }

    // ------------------------------------------------------------------ the feedee role

    /// <summary>Only someone who loves growing can be the Feedee.</summary>
    public class RoleRequirement_RRLikesWeight : RoleRequirement
    {
        public override string GetLabel(Precept_Role role) => "Likes weight (Weight Opinion: Like or higher)";

        public override bool Met(Pawn p, Precept_Role role) => FeedeesRitualUtility.LikesWeight(p);
    }

    /// <summary>The worshipped: the ideoligion's Feedee, and big enough to be worth worshipping (Obese or bigger).</summary>
    public class RitualRole_RRFeedee : RitualRoleTag
    {
        public override bool AppliesToPawn(Pawn p, out string reason, TargetInfo selectedTarget, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            if (!base.AppliesToPawn(p, out reason, selectedTarget, ritual, assignments, precept, skipReason))
                return false;
            if (!FeedeesRitualUtility.AtLeast(p, Defs.BodyTypeDefOf.F_040_Obese))
            {
                if (!skipReason)
                    reason = $"{p.LabelShort} isn't big enough to be worshipped yet (needs to be Obese or bigger).";
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Worship happens at the Feedee's bed: the one they own or are lying in. A
    /// Feedee too big to walk stays put and the worshippers come to them.
    /// </summary>
    public class RitualObligationTargetWorker_RRFeedeeBed : RitualObligationTargetFilter
    {
        public RitualObligationTargetWorker_RRFeedeeBed() { }
        public RitualObligationTargetWorker_RRFeedeeBed(RitualObligationTargetFilterDef def) : base(def) { }

        static bool IsFeedee(Pawn p, Ideo ideo) =>
            p.Ideo == ideo && ideo?.GetRole(p)?.def.defName == FeedeesRitualUtility.FeedeeRoleDefName;

        public static Pawn FeedeeOf(Building_Bed bed, Ideo ideo)
        {
            foreach (Pawn p in bed.CurOccupants)
                if (IsFeedee(p, ideo))
                    return p;
            foreach (Pawn p in bed.OwnersForReading)
                if (IsFeedee(p, ideo) && p.Spawned && p.Map == bed.Map)
                    return p;
            return null;
        }

        public override IEnumerable<TargetInfo> GetTargets(RitualObligation obligation, Map map)
        {
            Ideo ideo = parent?.ideo;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (!IsFeedee(p, ideo))
                    continue;
                Building_Bed bed = p.CurrentBed() ?? p.ownership?.OwnedBed;
                if (bed != null && bed.Map == map)
                    yield return bed;
            }
        }

        protected override RitualTargetUseReport CanUseTargetInternal(TargetInfo target, RitualObligation obligation)
        {
            if (!(target.Thing is Building_Bed bed) || !bed.def.building.bed_humanlike)
                return false;
            if (FeedeeOf(bed, parent?.ideo) == null)
                return false;
            return true;
        }

        public override IEnumerable<string> GetBlockingIssues(TargetInfo target, RitualRoleAssignments assignments)
        {
            Pawn feedee = assignments.FirstAssignedPawn("feedee");
            if (feedee == null || !(target.Thing is Building_Bed bed))
                yield break;
            if (feedee.CurrentBed() == bed)
                yield break;
            if (feedee.Downed)
                yield return $"{feedee.LabelShort} can't get to {feedee.Possessive()} bed alone. Carry {feedee.ProObj()} there first.";
            else if (!feedee.CanReach(bed, Verse.AI.PathEndMode.OnCell, Danger.Deadly))
                yield return $"{feedee.LabelShort} can't reach {feedee.Possessive()} bed.";
        }

        public override IEnumerable<string> GetTargetInfos(RitualObligation obligation)
        {
            yield return "The Feedee's bed";
        }
    }

    /// <summary>While the worship runs, worshippers call out their praise to the Feedee.</summary>
    public class RitualBehaviorWorker_RRFeedeeWorship : RitualBehaviorWorker
    {
        public RitualBehaviorWorker_RRFeedeeWorship() { }
        public RitualBehaviorWorker_RRFeedeeWorship(RitualBehaviorDef def) : base(def) { }

        public override void Tick(LordJob_Ritual ritual)
        {
            base.Tick(ritual);
            if (Find.TickManager.TicksGame % 420 != 0)
                return;
            Pawn feedee = ritual.PawnWithRole("feedee");
            InteractionDef praise = DefDatabase<InteractionDef>.GetNamedSilentFail("RR_Speech_FeedeeWorship");
            if (feedee == null || !feedee.Spawned || praise == null)
                return;
            Pawn worshipper = ritual.lord.ownedPawns.Where(p => p != feedee && p.Spawned && !p.Downed && p.Position.InHorDistOf(feedee.Position, 8f)).RandomElementWithFallback();
            if (worshipper == null)
                return;
            Find.PlayLog.Add(new PlayLogEntry_Interaction(praise, worshipper, feedee, null));
            MoteMaker.MakeInteractionBubble(worshipper, feedee, praise.interactionMote, praise.GetSymbol(worshipper.Faction, worshipper.Ideo), praise.GetSymbolColor(worshipper.Faction));
        }
    }

    /// <summary>Only those who like weight will come and worship.</summary>
    public class RitualSpectatorFilter_RRLikesWeight : RitualSpectatorFilter
    {
        public override bool Allowed(Pawn p) => FeedeesRitualUtility.LikesWeight(p);
    }

    // ------------------------------------------------------------------ seam calling

    /// <summary>The vessel the void is called to: a colonist heavy enough to squeeze through a seam (Chubby or bigger).</summary>
    public class RitualRole_RRSeamVessel : RitualRoleColonist
    {
        public override bool AppliesToPawn(Pawn p, out string reason, TargetInfo selectedTarget, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            if (!base.AppliesToPawn(p, out reason, selectedTarget, ritual, assignments, precept, skipReason))
                return false;
            if (!FeedeesRitualUtility.AtLeast(p, Defs.BodyTypeDefOf.F_006_Chonky))
            {
                if (!skipReason)
                    reason = $"{p.LabelShort} is too small to call a seam (needs to be Chubby or bigger).";
                return false;
            }
            return true;
        }
    }

    /// <summary>The seam can only be called where one could open: Anomaly, a surface map, no seam already open.</summary>
    public class RitualBehaviorWorker_RRSeamCalling : RitualBehaviorWorker
    {
        public RitualBehaviorWorker_RRSeamCalling() { }
        public RitualBehaviorWorker_RRSeamCalling(RitualBehaviorDef def) : base(def) { }

        public override string CanStartRitualNow(TargetInfo target, Precept_Ritual ritual, Pawn selectedPawn = null, Dictionary<string, Pawn> forcedForRole = null)
        {
            if (!ModsConfig.AnomalyActive)
                return "The void does not answer here (requires Anomaly).";
            if (target.IsValid && target.Map != null)
            {
                if (target.Map.IsPocketMap)
                    return "A seam can't be called from inside another place.";
                if (IncidentWorker_VoidPortalOpens.PortalExists(target.Map))
                    return "A void seam is already open here.";
            }
            return base.CanStartRitualNow(target, ritual, selectedPawn, forcedForRole);
        }
    }

    /// <summary>A good calling tears a void seam open next to the ritual spot.</summary>
    public class RitualOutcomeEffectWorker_RRSeamCalling : RitualOutcomeEffectWorker_FromQuality
    {
        public RitualOutcomeEffectWorker_RRSeamCalling() { }
        public RitualOutcomeEffectWorker_RRSeamCalling(RitualOutcomeEffectDef def) : base(def) { }

        protected override void ApplyExtraOutcome(Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual, RitualOutcomePossibility outcome, out string extraOutcomeDesc, ref LookTargets letterLookTargets)
        {
            extraOutcomeDesc = null;
            if (!outcome.Positive)
                return;
            Map map = jobRitual.Map;
            if (map == null || IncidentWorker_VoidPortalOpens.PortalExists(map))
                return;
            Thing seam = IncidentWorker_VoidPortalOpens.TryOpenNear(jobRitual.selectedTarget.Cell, map);
            if (seam == null)
            {
                extraOutcomeDesc = "The void answered, but there was nowhere for it to open.";
                return;
            }
            letterLookTargets = new LookTargets(seam);
            extraOutcomeDesc = "The ground split open into a void seam. Anyone Chubby or heavier can climb down into the void maze.";

            // the best callings: the void takes a taste of its vessel first
            Pawn vessel = jobRitual.PawnWithRole("vessel");
            if (vessel != null && outcome.BestPositiveOutcome(jobRitual))
            {
                Utilities.HediffUtility.QueueWeightGain(vessel, 25f);
                extraOutcomeDesc += $" Warm breath rolled over {vessel.LabelShort} as it opened, and {vessel.ProSubj()} came away heavier.";
            }
        }
    }

    /// <summary>Quality from how much weight the congregation brings: the void answers the heavy.</summary>
    public class RitualOutcomeComp_RRCongregationWeight : RitualOutcomeComp_QualitySingleOffset
    {
        public override bool DataRequired => false;

        static float TotalTons(IEnumerable<Pawn> pawns) => pawns.Sum(FeedeesRitualUtility.Kilos) / 1000f;

        public override float Count(LordJob_Ritual ritual, RitualOutcomeComp_Data data) => TotalTons(ritual.assignments.Participants);

        public override float QualityOffset(LordJob_Ritual ritual, RitualOutcomeComp_Data data) => curve.Evaluate(Count(ritual, data));

        public override string GetDesc(LordJob_Ritual ritual = null, RitualOutcomeComp_Data data = null)
        {
            if (ritual == null)
                return labelAbstract ?? label;
            return $"{label.CapitalizeFirst()} ({Count(ritual, data):0.0} t): " + "OutcomeBonusDesc_QualitySingleOffset".Translate("+" + QualityOffset(ritual, data).ToStringPercent()) + ".";
        }

        public override QualityFactor GetQualityFactor(Precept_Ritual ritual, TargetInfo ritualTarget, RitualObligation obligation, RitualRoleAssignments assignments, RitualOutcomeComp_Data data)
        {
            float tons = TotalTons(assignments.Participants);
            float q = curve.Evaluate(tons);
            return new QualityFactor
            {
                label = label.CapitalizeFirst(),
                count = $"{tons:0.0} t",
                qualityChange = "OutcomeBonusDesc_QualitySingleOffset".Translate(q.ToStringWithSign("0.#%")).Resolve(),
                positive = q >= 0f,
                quality = q,
                priority = 1f,
            };
        }
    }

    // ------------------------------------------------------------------ feedee worship

    /// <summary>Quality from the Feedee's size: the bigger, the more there is to worship.</summary>
    public class RitualOutcomeComp_RRFeedeeSize : RitualOutcomeComp_QualitySingleOffset
    {
        [NoTranslate] public string roleId = "feedee";

        public override bool DataRequired => false;

        public override float Count(LordJob_Ritual ritual, RitualOutcomeComp_Data data)
        {
            Pawn p = ritual.PawnWithRole(roleId);
            return p == null ? 0f : FeedeesRitualUtility.NormalisedSize(p);
        }

        public override float QualityOffset(LordJob_Ritual ritual, RitualOutcomeComp_Data data) => curve.Evaluate(Count(ritual, data));

        public override string GetDesc(LordJob_Ritual ritual = null, RitualOutcomeComp_Data data = null)
        {
            if (ritual == null)
                return labelAbstract ?? label;
            Pawn p = ritual.PawnWithRole(roleId);
            if (p == null)
                return null;
            return label.Formatted(p.Named("PAWN")).CapitalizeFirst() + ": " + "OutcomeBonusDesc_QualitySingleOffset".Translate("+" + QualityOffset(ritual, data).ToStringPercent()) + ".";
        }

        public override QualityFactor GetQualityFactor(Precept_Ritual ritual, TargetInfo ritualTarget, RitualObligation obligation, RitualRoleAssignments assignments, RitualOutcomeComp_Data data)
        {
            Pawn p = assignments.FirstAssignedPawn(roleId);
            if (p == null)
                return null;
            float q = curve.Evaluate(FeedeesRitualUtility.NormalisedSize(p));
            return new QualityFactor
            {
                label = label.Formatted(p.Named("PAWN")).CapitalizeFirst(),
                count = p.story?.bodyType?.defName.Substring(Mathf.Min(6, p.story.bodyType.defName.Length)).Replace('_', ' ') ?? "",
                qualityChange = "OutcomeBonusDesc_QualitySingleOffset".Translate(q.ToStringWithSign("0.#%")).Resolve(),
                positive = q >= 0f,
                quality = q,
                priority = 0f,
            };
        }
    }

    /// <summary>
    /// Worship pays the Feedee in offerings: every worshipper brings food, and the
    /// better it goes, the more they bring. The Feedee also gets a memory of it.
    /// </summary>
    public class RitualOutcomeEffectWorker_RRFeedeeWorship : RitualOutcomeEffectWorker_FromQuality
    {
        public RitualOutcomeEffectWorker_RRFeedeeWorship() { }
        public RitualOutcomeEffectWorker_RRFeedeeWorship(RitualOutcomeEffectDef def) : base(def) { }

        protected override void ApplyExtraOutcome(Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual, RitualOutcomePossibility outcome, out string extraOutcomeDesc, ref LookTargets letterLookTargets)
        {
            extraOutcomeDesc = null;
            Pawn feedee = jobRitual.PawnWithRole("feedee");
            if (feedee == null || feedee.Dead)
                return;

            int worshippers = totalPresence.Keys.Count(p => p != feedee);
            float perWorshipper = outcome.positivityIndex <= -2 ? 0f
                : outcome.positivityIndex < 0 ? 3f
                : outcome.positivityIndex == 1 ? 8f
                : 14f;
            float kilos = worshippers * perWorshipper;
            if (kilos > 0f)
            {
                Utilities.HediffUtility.QueueWeightGain(feedee, kilos);
                extraOutcomeDesc = $"The worshippers fed {feedee.LabelShort} their offerings: about {kilos:0} kg of them.";
            }
            if (outcome.Positive)
                feedee.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDef.Named("RR_Worshipped"));
        }
    }

    // ------------------------------------------------------------------ the meme's precepts in existing ideoligions

    /// <summary>
    /// Vanilla gives an ideoligion a meme's rituals when it is founded or reformed,
    /// but not its role, and not to ideoligions that took the meme before these
    /// precepts existed. This adds whatever the player's Feedees ideoligions lack,
    /// once per ideoligion, so a player who removes one later is left alone.
    /// </summary>
    public class GameComponent_RRFeedeesPrecepts : GameComponent
    {
        static readonly string[] PreceptNames = { "RR_IdeoRole_Feedee", "RR_FeedeeWorship", "RR_SeamCalling" };

        HashSet<int> handled = new HashSet<int>();

        public GameComponent_RRFeedeesPrecepts(Game game) { }

        public override void LoadedGame() => EnsurePrecepts();

        public override void StartedNewGame() => EnsurePrecepts();

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 2500 == 0)
                EnsurePrecepts();
        }

        void EnsurePrecepts()
        {
            if (!ModsConfig.IdeologyActive || Faction.OfPlayerSilentFail?.ideos == null)
                return;
            MemeDef meme = DefDatabase<MemeDef>.GetNamedSilentFail(FeedeesUtility.MemeDefName);
            if (meme == null)
                return;

            foreach (Ideo ideo in Faction.OfPlayer.ideos.AllIdeos)
            {
                if (!ideo.HasMeme(meme) || handled.Contains(ideo.id))
                    continue;
                handled.Add(ideo.id);
                foreach (string name in PreceptNames)
                {
                    PreceptDef def = DefDatabase<PreceptDef>.GetNamedSilentFail(name);
                    if (def == null || ideo.HasPrecept(def))
                        continue;
                    ideo.AddPrecept(PreceptMaker.MakePrecept(def), init: true, null, def.ritualPatternBase);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref handled, "handledIdeos", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && handled == null)
                handled = new HashSet<int>();
        }
    }

    // ------------------------------------------------------------------ dislikers in a Feedees ideoligion

    /// <summary>Someone who dislikes weight, stuck believing in the Feedees: uneasy, or worse.</summary>
    public class ThoughtWorker_RRFeedeesDoubt : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!ModsConfig.IdeologyActive || p.Ideo == null)
                return ThoughtState.Inactive;
            MemeDef meme = DefDatabase<MemeDef>.GetNamedSilentFail(FeedeesUtility.MemeDefName);
            if (meme == null || !p.Ideo.HasMeme(meme))
                return ThoughtState.Inactive;
            WeightOpinion o = FeedeesRitualUtility.Opinion(p);
            if (o == WeightOpinion.Hate)
                return ThoughtState.ActiveAtStage(1);
            if (o == WeightOpinion.Dislike)
                return ThoughtState.ActiveAtStage(0);
            return ThoughtState.Inactive;
        }
    }
}
