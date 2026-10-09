using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Gluttony
{
    /// <summary>
    /// The bottomless bowl (Anomaly): a battered bowl of void-gluttonium slop that is never empty.
    /// Like Anomaly's golden cube it draws people in - but only people who don't mind getting
    /// bigger (Neutral weight opinion and up; the more they like it, the harder it pulls).
    /// Allured pawns crave it (RR_BowlCraving) until they gorge from it, and the obsessed binge
    /// on it. Studied, it can be shattered with a shard, which throws everyone it held into a
    /// food binge. Anyone can be ordered to eat from it; it stuffs them to the brim.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BowlUtility
    {
        const float KilosPerFeedMin = 3f, KilosPerFeedMax = 6f;
        const float ExposurePerFeed = 0.06f;
        const float JoyPerFeed = 0.25f;
        static readonly FloatRange AllurePerFeed = new FloatRange(0.05f, 0.1f);

        static ThingDef bowl;
        static HediffDef allure, craving, exposure;
        static JobDef feedJob;
        static ThoughtDef ateThought;

        public static ThingDef Bowl => bowl ??= DefDatabase<ThingDef>.GetNamedSilentFail("RR_BottomlessBowl");
        public static HediffDef Allure => allure ??= DefDatabase<HediffDef>.GetNamedSilentFail("RR_BowlAllure");
        public static HediffDef Craving => craving ??= DefDatabase<HediffDef>.GetNamedSilentFail("RR_BowlCraving");
        public static HediffDef Exposure => exposure ??= DefDatabase<HediffDef>.GetNamedSilentFail("RR_GluttoniumExposure");
        public static JobDef FeedJob => feedJob ??= DefDatabase<JobDef>.GetNamedSilentFail("RR_FeedFromBowl");
        public static ThoughtDef AteThought => ateThought ??= DefDatabase<ThoughtDef>.GetNamedSilentFail("RR_AteFromBowl");

        /// <summary>
        /// How hard the bowl pulls on this pawn: 0 for anyone who minds getting bigger
        /// (NeutralMinus and below, or no opinion at all), rising with how much they like it.
        /// </summary>
        public static float AllureStrength(Pawn pawn)
        {
            switch (pawn?.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.None)
            {
                case WeightOpinion.Neutral: return 0.5f;
                case WeightOpinion.NeutralPlus: return 0.75f;
                case WeightOpinion.Like: return 1f;
                case WeightOpinion.Love: return 1.3f;
                case WeightOpinion.Fanatical: return 1.6f;
                case WeightOpinion.Extreme: return 2f;
                default: return 0f;
            }
        }

        public static bool CanFeed(Pawn pawn) =>
            pawn?.needs?.food != null && pawn.RaceProps.Humanlike && pawn.TryGetComp<FullnessAndDietStats_ThingComp>() is FullnessAndDietStats_ThingComp c && !c.Disabled;

        public static bool TryFindBowl(Pawn pawn, out Thing found)
        {
            found = null;
            if (Bowl == null || !pawn.Spawned)
                return false;
            found = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(Bowl), PathEndMode.ClosestTouch,
                TraverseParms.For(pawn), 9999f, t => !t.IsForbidden(pawn));
            return found != null;
        }

        public static Job FeedJobOn(Thing bowlThing) => JobMaker.MakeJob(FeedJob, bowlThing);

        /// <summary>Sends the pawn off to gorge from the nearest bowl, if there is one and they're free to go.</summary>
        public static bool TrySendToBowl(Pawn pawn, int times = 1)
        {
            if (FeedJob == null || pawn.Downed || pawn.Drafted || pawn.InMentalState || pawn.GetLord() != null || pawn.CurJobDef == FeedJob
                || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || !TryFindBowl(pawn, out Thing found))
                return false;
            pawn.jobs.StartJob(FeedJobOn(found), JobCondition.InterruptForced);
            for (int i = 1; i < times; i++)
                pawn.jobs.jobQueue.EnqueueLast(FeedJobOn(found));
            return true;
        }

        /// <summary>
        /// One helping from the bowl: it stuffs them to their soft limit - past it for the
        /// obsessed, but never up to the hard limit, so nobody bursts - and soaks them in its
        /// gluttonium.
        /// </summary>
        public static void Feed(Pawn pawn)
        {
            var fnd = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            Hediff allureHediff = Allure != null ? pawn.health.hediffSet.GetFirstHediffOfDef(Allure) : null;
            bool obsessed = allureHediff != null && allureHediff.Severity >= 0.66f;
            if (fnd != null && !fnd.Disabled)
            {
                float limit = obsessed ? Mathf.Lerp(fnd.SoftLimit, fnd.HardLimit, 0.6f) : fnd.SoftLimit;
                float ratio = FullnessAndDietStats_ThingComp.defaultFullnessToNutritionRatio;
                float nutrition = Mathf.Max(0.3f, fnd.RemainingFullnessUntil(limit) / Mathf.Max(0.01f, ratio * fnd.FullnessGainedMultiplier));
                fnd.UpdateRatio(nutrition, ratio);
                fnd.CurrentFullness = Mathf.Max(fnd.CurrentFullness, Mathf.Min(limit, fnd.CurrentFullness + nutrition * ratio * fnd.FullnessGainedMultiplier));
                if (pawn.needs?.food is Need_Food food)
                    food.CurLevel = Mathf.Min(food.MaxLevel, food.CurLevel + nutrition);
            }
            float strength = AllureStrength(pawn);
            Utilities.HediffUtility.QueueWeightGain(pawn, Rand.Range(KilosPerFeedMin, KilosPerFeedMax) * (1f + strength * 0.5f));
            if (Exposure != null)
                HealthUtility.AdjustSeverity(pawn, Exposure, ExposurePerFeed);
            pawn.needs?.joy?.GainJoy(JoyPerFeed, JoyKindDefOf.Gluttonous);
            if (AteThought != null)
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtMaker.MakeThought(AteThought, strength > 0f ? 0 : 1));
            if (allureHediff != null)
                allureHediff.Severity += AllurePerFeed.RandomInRange;
            if (Craving != null && pawn.health.hediffSet.GetFirstHediffOfDef(Craving) is Hediff c2)
                pawn.health.RemoveHediff(c2);
        }

        public static void CureAllure(Pawn pawn)
        {
            foreach (HediffDef d in new[] { Allure, Craving })
                if (d != null && pawn.health.hediffSet.GetFirstHediffOfDef(d) is Hediff h)
                    pawn.health.RemoveHediff(h);
        }
    }

    // ------------------------------------------------------------------ the bowl

    public class CompProperties_BottomlessBowl : CompProperties_Interactable
    {
        public CompProperties_BottomlessBowl()
        {
            compClass = typeof(CompBottomlessBowl);
        }
    }

    /// <summary>
    /// Picks out who the bowl draws in (one at once to start with, more over time), offers
    /// "eat from the bowl" to anyone, and - once studied - lets a colonist shatter it with a shard.
    /// </summary>
    public class CompBottomlessBowl : CompInteractable
    {
        const float FirstAllureMtbDays = 0.5f;
        const float MoreAllureMtbDays = 8f;
        static readonly IntRange VoidGluttoniumOnShatter = new IntRange(15, 25);

        CompStudyUnlocks studyUnlocks;

        public new CompProperties_BottomlessBowl Props => (CompProperties_BottomlessBowl)props;

        CompStudyUnlocks StudyUnlocks => studyUnlocks ??= parent.GetComp<CompStudyUnlocks>();

        public bool Shatterable => StudyUnlocks == null || StudyUnlocks.Completed;

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);
            if (!parent.IsHashIntervalTick(2500, delta) || parent.MapHeld == null || BowlUtility.Allure == null)
                return;
            bool anyone = parent.MapHeld.mapPawns.FreeColonists.Any(p => p.health.hediffSet.HasHediff(BowlUtility.Allure));
            if (!Rand.MTBEventOccurs(anyone ? MoreAllureMtbDays : FirstAllureMtbDays, GenDate.TicksPerDay, 2500f))
                return;
            Pawn pawn = parent.MapHeld.mapPawns.FreeColonistsSpawned
                .Where(p => !p.health.hediffSet.HasHediff(BowlUtility.Allure) && BowlUtility.AllureStrength(p) > 0f && BowlUtility.CanFeed(p))
                .RandomElementByWeightWithFallback(BowlUtility.AllureStrength);
            if (pawn == null)
                return;
            pawn.health.AddHediff(BowlUtility.Allure);
            Messages.Message($"{pawn.LabelShort} can't stop thinking about the bottomless bowl.", pawn, MessageTypeDefOf.NeutralEvent);
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (BowlUtility.FeedJob != null && BowlUtility.CanFeed(selPawn))
            {
                if (!selPawn.CanReach(parent, PathEndMode.ClosestTouch, Danger.Deadly))
                    yield return new FloatMenuOption("Eat from the bottomless bowl (no path)", null);
                else
                    yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption("Eat from the bottomless bowl", () =>
                    {
                        Job job = BowlUtility.FeedJobOn(parent);
                        job.playerForced = true;
                        selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }), selPawn, parent);
            }
            if (Shatterable)
            {
                AcceptanceReport report = CanInteract(selPawn);
                var opt = new FloatMenuOption(Props.jobString.CapitalizeFirst(), () => OrderShatter(selPawn));
                if (!report.Accepted)
                {
                    opt.Disabled = true;
                    opt.Label += " (" + report.Reason + ")";
                }
                yield return opt;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Shatterable)
                yield break;
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;
        }

        public override string CompInspectStringExtra() => "";

        public override void OrderForceTarget(LocalTargetInfo target)
        {
            if (ValidateTarget(target, showMessages: false))
                OrderShatter(target.Pawn);
        }

        public override AcceptanceReport CanInteract(Pawn activateBy = null, bool checkOptionalItems = true)
        {
            AcceptanceReport result = base.CanInteract(activateBy, checkOptionalItems);
            if (!result.Accepted)
                return result;
            if (activateBy != null)
            {
                if (BowlUtility.Allure != null && activateBy.health.hediffSet.HasHediff(BowlUtility.Allure))
                    return $"{activateBy.LabelShort} won't break it";
                if (checkOptionalItems && !activateBy.HasReserved(ThingDefOf.Shard) && !activateBy.CanReserveAndReachableOfDef(ThingDefOf.Shard))
                    return "NoItemReservedOrReachable".Translate(ThingDefOf.Shard.label);
            }
            else if (checkOptionalItems && !ReservationUtility.ExistsUnreservedAmountOfDef(parent.MapHeld, ThingDefOf.Shard, Faction.OfPlayer, 1))
                return "NoItemReserved".Translate(ThingDefOf.Shard.label);
            return true;
        }

        void OrderShatter(Pawn pawn)
        {
            List<Pawn> held = AllAllured().ToList();
            string text = "Shatter the bottomless bowl? It will be gone for good.";
            if (held.Any())
                text += "\n\nThe psychic whiplash will throw everyone it holds into a food binge:\n" + held.Select(p => p.LabelShort).ToLineList("- ");
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () =>
            {
                if (pawn.TryFindReserveAndReachableOfDef(ThingDefOf.Shard, out Thing shard))
                {
                    Job job = JobMaker.MakeJob(JobDefOf.InteractThing, parent, shard);
                    job.count = 1;
                    job.playerForced = true;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
            }));
        }

        static IEnumerable<Pawn> AllAllured() =>
            BowlUtility.Allure == null ? Enumerable.Empty<Pawn>() :
            Find.Maps.SelectMany(m => m.mapPawns.AllPawns).Where(p => p.health.hediffSet.HasHediff(BowlUtility.Allure));

        protected override void OnInteracted(Pawn caster)
        {
            Map map = parent.MapHeld;
            IntVec3 at = parent.PositionHeld;
            MentalStateDef binge = DefDatabase<MentalStateDef>.GetNamedSilentFail("Binging_Food");
            var bingeing = new List<string>();
            foreach (Pawn p in AllAllured().ToList())
            {
                BowlUtility.CureAllure(p);
                if (binge != null && p.Spawned && !p.Downed
                    && p.mindState.mentalStateHandler.TryStartMentalState(binge, "the bottomless bowl shattered", forced: true, transitionSilently: true))
                    bingeing.Add(p.LabelShort);
            }
            ThingDef voidGluttonium = DefDatabase<ThingDef>.GetNamedSilentFail("RR_VoidGluttonium");
            if (voidGluttonium != null && map != null)
            {
                Thing loot = ThingMaker.MakeThing(voidGluttonium);
                loot.stackCount = VoidGluttoniumOnShatter.RandomInRange;
                GenPlace.TryPlaceThing(loot, at, map, ThingPlaceMode.Near);
            }
            string letter = $"{caster.LabelShort} pressed the shard into the slop, and the bottomless bowl cracked clean in two. For the first time, it is empty. Only a heap of void gluttonium is left where it sat.";
            if (bingeing.Any())
                letter += "\n\nThe psychic whiplash has thrown everyone it held into a food binge:\n" + bingeing.ToLineList("- ");
            Find.LetterStack.ReceiveLetter("Bowl shattered", letter, bingeing.Any() ? LetterDefOf.ThreatSmall : LetterDefOf.NeutralEvent, new TargetInfo(at, map));
            parent.Destroy();
        }
    }

    // ------------------------------------------------------------------ eating from it

    public class JobDriver_RRFeedFromBowl : JobDriver
    {
        const int FeedTicks = 700;

        Thing BowlThing => job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            Toil eat = Toils_General.Wait(FeedTicks, TargetIndex.A);
            eat.WithProgressBarToilDelay(TargetIndex.A);
            eat.WithEffect(EffecterDefOf.EatMeat, TargetIndex.A);
            eat.PlaySustainerOrSound(() => SoundDefOf.RawMeat_Eat);
            eat.handlingFacing = true;
            eat.tickAction = () => pawn.rotationTracker.FaceTarget(BowlThing);
            yield return eat;
            yield return Toils_General.Do(() => BowlUtility.Feed(pawn));
        }
    }

    // ------------------------------------------------------------------ its pull

    /// <summary>
    /// Being drawn to the bowl. Every so often it turns into a craving that only the bowl
    /// satisfies; the obsessed now and then drop everything to binge on it. Wears off if the
    /// pawn stops liking weight.
    /// </summary>
    public class Hediff_BowlAllure : Hediff
    {
        const float CravingMtbDays = 1.5f;
        const float BingeMtbDays = 5f;
        const float ObsessedFrom = 0.66f;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (ageTicks < 1000 || !pawn.IsHashIntervalTick(250, delta))
                return;
            float strength = BowlUtility.AllureStrength(pawn);
            if (strength <= 0f)
            {
                pawn.health.RemoveHediff(this);
                return;
            }
            if (BowlUtility.Craving != null && !pawn.health.hediffSet.HasHediff(BowlUtility.Craving)
                && Rand.MTBEventOccurs(CravingMtbDays / strength, GenDate.TicksPerDay, 250f))
                pawn.health.AddHediff(BowlUtility.Craving);
            if (Severity >= ObsessedFrom && pawn.Spawned && Rand.MTBEventOccurs(BingeMtbDays / strength, GenDate.TicksPerDay, 250f)
                && BowlUtility.TrySendToBowl(pawn, Rand.RangeInclusive(2, 3)) && PawnUtility.ShouldSendNotificationAbout(pawn))
                Messages.Message($"{pawn.LabelShort} has dropped everything to gorge at the bottomless bowl.", pawn, MessageTypeDefOf.NegativeEvent);
        }

        protected override void OnStageIndexChanged(int stageIndex)
        {
            if (stageIndex > 0 && PawnUtility.ShouldSendNotificationAbout(pawn))
                Messages.Message($"{pawn.LabelShort} is falling further under the bottomless bowl's pull.", pawn, MessageTypeDefOf.NeutralEvent);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            if (!DebugSettings.ShowDevGizmos)
                yield break;
            yield return new Command_Action { defaultLabel = "DEV: Bowl craving", action = () => pawn.health.AddHediff(BowlUtility.Craving) };
            yield return new Command_Action { defaultLabel = "DEV: Bowl binge", action = () => BowlUtility.TrySendToBowl(pawn, 3) };
        }
    }

    /// <summary>
    /// Craving the bowl: it builds until they eat from it, and they'll break off what they're
    /// doing to go to it. Left too long it snaps into a food binge.
    /// </summary>
    public class Hediff_BowlCraving : Hediff
    {
        const float SeverityPerHour = 0.025f;
        const int RetryInterval = 6 * GenDate.TicksPerHour;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn.CurJobDef == BowlUtility.FeedJob || !pawn.Awake())
                return;
            Hediff allure = BowlUtility.Allure != null ? pawn.health.hediffSet.GetFirstHediffOfDef(BowlUtility.Allure) : null;
            Severity += SeverityPerHour * (0.5f + (allure?.Severity ?? 0.5f)) * delta / GenDate.TicksPerHour;
            if (CurStageIndex > 0 && pawn.IsHashIntervalTick(RetryInterval, delta))
                BowlUtility.TrySendToBowl(pawn);
            if (Severity < 1f)
                return;
            MentalStateDef binge = DefDatabase<MentalStateDef>.GetNamedSilentFail("Binging_Food");
            if (binge != null && !pawn.InMentalState && pawn.Spawned)
                pawn.mindState.mentalStateHandler.TryStartMentalState(binge, "craving the bottomless bowl", forced: true);
            Severity = 0.5f;
        }

        protected override void OnStageIndexChanged(int stageIndex)
        {
            if (stageIndex <= 0 || !PawnUtility.ShouldSendNotificationAbout(pawn))
                return;
            Messages.Message(stageIndex == 1 ? $"{pawn.LabelShort} is craving the bottomless bowl." : $"{pawn.LabelShort}'s craving for the bottomless bowl is getting worse.",
                pawn, MessageTypeDefOf.NegativeHealthEvent);
            BowlUtility.TrySendToBowl(pawn);
        }
    }

    // ------------------------------------------------------------------ how it arrives

    /// <summary>Someone left a bowl at the edge of the colony. It's full. It stays full.</summary>
    public class IncidentWorker_RRBottomlessBowl : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) =>
            BowlUtility.Bowl != null && parms.target is Map map && !map.listerThings.ThingsOfDef(BowlUtility.Bowl).Any();

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            Pawn anyone = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (anyone == null || !RCellFinder.TryFindRandomSpotJustOutsideColony(anyone, out IntVec3 at))
                at = DropCellFinder.RandomDropSpot(map);
            Thing thing = BowlArrival.Spawn(at, map);
            if (thing == null)
                return false;
            SendStandardLetter(def.letterLabel, def.letterText, def.letterDef, parms, thing);
            return true;
        }
    }

    public static class BowlArrival
    {
        public static Thing Spawn(IntVec3 near, Map map)
        {
            if (BowlUtility.Bowl == null)
                return null;
            Thing thing = ThingMaker.MakeThing(BowlUtility.Bowl);
            if (!GenPlace.TryPlaceThing(thing, near, map, ThingPlaceMode.Near))
                return null;
            FleckMaker.ThrowDustPuffThick(thing.DrawPos, map, 1.5f, new Color(0.45f, 0.3f, 0.55f));
            return thing;
        }
    }
}
