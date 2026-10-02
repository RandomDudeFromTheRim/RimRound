using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.Things;
using RimRound.Utilities;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace RimRound.Hediffs
{
    /// <summary>
    /// A gorge constrictor wrapped around this pawn. The beast itself is held off
    /// the map as a world pawn and drawn around the victim by the hediff's render
    /// nodes: its coils round the body, and a proboscis in the victim's mouth.
    ///
    /// A constrictor is a sac of slurry (its load, in kilos). Latched on, it holds
    /// the victim still and pumps that load into them every few seconds, shrinking
    /// as it empties (drawn in five stages, biggest first). If the victim reaches
    /// their burst point - Gelatinous I, or a later Gelatinous tier for a pawn whose
    /// stomach perks let them take more - every further pump strains them
    /// (Hediff_RROverfilled) and may burst them, and the constrictor with them. If it
    /// runs dry first, it drops off, a spent husk. Wounding it before it latches spills
    /// its load. With bursting turned off (GlobalSettings.burstingEnabled) nobody bursts:
    /// at the burst point it's the constrictor that gives, and bursts on its own.
    ///
    /// A bound constrictor (leashed) works the same, but never bursts anyone: it lets
    /// go, sated, at the burst point, and its load is the feed it got in a vat.
    /// </summary>
    public class Hediff_RRConstricted : Hediff
    {
        // slow enough to react: about three minutes to empty a full load at normal speed
        public const int PumpIntervalTicks = 180;
        const int PumpsToEmpty = 60;
        public const int Stages = 5;
        const float MinKilosPerPump = 4f;
        const float OverloadKilos = 40f;

        public Pawn constrictor;
        /// <summary>
        /// Constrictors that found her already taken and coiled in with the first one instead
        /// of fighting over her (world pawns, like the first). Three coils make a troika.
        /// </summary>
        public List<Pawn> joined = new List<Pawn>();
        /// <summary>
        /// A bound constrictor (the player's, from the binding ritual) rather than a
        /// wild one: there is no beast pawn, it never bursts anyone - it lets go, sated,
        /// at the burst point - and it drops back as the item whenever it lets go.
        /// </summary>
        public bool leashed;
        /// <summary>A bound constrictor's feed and mutations, carried while it is latched on.</summary>
        public BoundConstrictorData bound;
        float load = -1f;           // kilos of slurry still in it
        float startLoad = -1f;      // kilos it had when it latched
        int latchTick = -1;
        int pumps;
        bool bursting;

        /// <summary>Victims on any map, for the proboscis drawn in world space (MapComponent_RRConstrictorDraw).</summary>
        public static readonly HashSet<Pawn> Victims = new HashSet<Pawn>();

        /// <summary>Kilos of slurry still in it.</summary>
        public float Load => load;

        /// <summary>A wild one that still holds enough to take them past their burst point, and is close to it.</summary>
        public bool WillBurstSoon => bound == null && !bursting && GlobalSettings.burstingEnabled && !Unburstable(pawn) && load >= KilosToBurst(pawn) && KilosToBurst(pawn) <= 300f;

        /// <summary>How much of what it latched with is still in it: 1 when it latches, 0 when spent.</summary>
        public float LoadFraction => startLoad <= 0f ? 0f : Mathf.Clamp01(load / startLoad);

        /// <summary>Which coil sprite to draw: it is biggest when it latches and shrinks as it empties.</summary>
        public int Stage => Mathf.Clamp((int)(LoadFraction * Stages), 0, Stages - 1);

        /// <summary>How far through the current size stage it still is, 0..1.</summary>
        public float WithinStage => Mathf.Clamp01(LoadFraction * Stages - Stage);

        /// <summary>0 just after a pump, rising to 1 as the next one comes: drives the pumping animation.</summary>
        public float PumpPhase => latchTick < 0 ? 0f : (float)((Find.TickManager.TicksGame - latchTick) % Interval) / Interval;

        /// <summary>Ticks between pumps: a bound constrictor's mutations can quicken it, and broods pump slower.</summary>
        public int Interval => Mathf.Max(30, Mathf.RoundToInt(PumpIntervalTicks * (bound?.PumpIntervalFactor ?? WildPumpFactor) / (1f + 0.5f * (Coils - 1))));

        /// <summary>How many constrictors are wrapped around her: a troika creature counts as three.</summary>
        public int Coils => (bound != null ? 1 : CoilsOf(constrictor)) + joined.Sum(CoilsOf);

        static int CoilsOf(Pawn beast) => beast?.TryGetComp<Comp_RRConstrictorHunt>()?.Props.coils ?? 1;

        /// <summary>Three coils: bound to her for good, like a voidworm troika. She can't struggle free, and if she bursts, she seeds a nest.</summary>
        public bool TroikaBound => bound == null && Coils >= 3;

        float WildPumpFactor => constrictor?.TryGetComp<Comp_RRConstrictorHunt>()?.Props.pumpIntervalFactor ?? 1f;

        /// <summary>What to call it: a gorge constrictor, a brood constrictor...</summary>
        string BeastLabel => constrictor?.def.label ?? "gorge constrictor";

        /// <summary>Gelatinous tiers past the usual burst point a bound constrictor keeps pumping to.</summary>
        int ExtraTiers => bound?.ExtraBurstTiers ?? 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref constrictor, "constrictor");
            Scribe_Values.Look(ref load, "load", -1f);
            Scribe_Values.Look(ref startLoad, "startLoad", -1f);
            Scribe_Values.Look(ref latchTick, "latchTick", -1);
            Scribe_Values.Look(ref pumps, "pumps");
            Scribe_Values.Look(ref bursting, "bursting");
            Scribe_Collections.Look(ref joined, "joined", LookMode.Reference);
            Scribe_Values.Look(ref leashed, "leashed");
            Scribe_Deep.Look(ref bound, "bound");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (leashed && bound == null)
                    bound = new BoundConstrictorData();
                if (joined == null)
                    joined = new List<Pawn>();
                joined.RemoveAll(p => p == null);
                // latched before constrictors carried a load: give it a fresh one
                if (startLoad <= 0f)
                    load = startLoad = Comp_RRConstrictorHunt.FullLoad;
                if (pawn != null)
                    Victims.Add(pawn);
            }
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            Victims.Add(pawn);
        }

        // ------------------------------------------------------------ the burst point

        // A pawn bursts on passing the top of one of these. The last body type below
        // Gelatinous I is the base; each doubling of the stomach's hard limit over the
        // default (perks like Black Hole and Limit Break) moves it one Gelatinous tier on.
        static readonly string[] BurstTiers =
        {
            "F_090_Titanic", "F_100_Gelatinous", "F_150_Gelatinous", "F_200_Gelatinous", "F_250_Gelatinous",
            "F_300_Gelatinous", "F_350_Gelatinous", "F_400_Gelatinous", "F_450_Gelatinous", "F_500_Gelatinous",
        };

        /// <summary>The body type a pawn bursts on outgrowing: Titanic, or later for a pawn whose perks let them take more.</summary>
        public static BodyTypeDef BurstBodyType(Pawn p, int extraTiers = 0)
        {
            int tier = extraTiers + Hediff_RROverfilled.StretchedLevel(p);
            var fnd = p.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fnd != null && !fnd.Disabled)
            {
                float baseline = FullnessAndDietStats_ThingComp.defaultSoftLimit * 1.3f;
                tier += Mathf.FloorToInt(Mathf.Log(Mathf.Max(1f, fnd.HardLimit / baseline), 2f));
            }
            tier = Mathf.Clamp(tier, 0, BurstTiers.Length - 1);
            for (; tier > 0; tier--)
            {
                BodyTypeDef def = DefDatabase<BodyTypeDef>.GetNamedSilentFail(BurstTiers[tier]);
                if (def != null && RacialBodyTypeInfoUtility.defaultFemaleSet.ContainsKey(def))
                    return def;
            }
            return Defs.BodyTypeDefOf.F_090_Titanic;
        }

        /// <summary>Weight severity at which this pawn bursts, for its race.</summary>
        static float BurstSeverity(Pawn p, int extraTiers = 0)
        {
            float max = RacialBodyTypeInfoUtility.defaultFemaleSet[BurstBodyType(p, extraTiers)].maxSeverity;
            return max * RacialBodyTypeInfoUtility.GetBodyTypeWeightRequirementMultiplier(p);
        }

        static float WeightSeverity(Pawn p) => Utilities.HediffUtility.WeightHediff(p)?.Severity ?? 0f;

        /// <summary>Kilos still to go before this pawn reaches their burst point.</summary>
        public static float KilosToBurst(Pawn p, int extraTiers = 0) =>
            Mathf.Max(0f, Utilities.HediffUtility.SeverityToKilosWithoutBaseWeight(BurstSeverity(p, extraTiers) - WeightSeverity(p)));

        public static bool PastBurstPoint(Pawn p, int extraTiers = 0) => BodyTypeUtility.PawnIsOverWeightThreshold(p, BurstBodyType(p, extraTiers));

        /// <summary>
        /// Ghouls and the void-touched knit back together faster than a constrictor can
        /// split them: it never bursts them, it just keeps pumping until it runs dry.
        /// </summary>
        public static bool Unburstable(Pawn p) =>
            ModsConfig.AnomalyActive && p != null
            && (p.IsGhoul || (RimWorld.HediffDefOf.VoidTouched != null && p.health.hediffSet.HasHediff(RimWorld.HediffDefOf.VoidTouched)));

        // ------------------------------------------------------------ latching on

        /// <summary>The constrictor wraps itself around the victim. Called when its melee attack lands.</summary>
        public static void Latch(Pawn victim, Pawn beast)
        {
            if (victim == null || beast == null || victim.Dead || beast.Dead || !victim.Spawned || !beast.Spawned)
                return;
            if (!victim.RaceProps.Humanlike || victim.health.hediffSet.HasHediff(Defs.HediffDefOf.RR_Constricted))
                return;
            var hunt = beast.TryGetComp<Comp_RRConstrictorHunt>();
            if (hunt != null && hunt.Spent)
                return;

            Map map = victim.Map;
            bool unburstable = Unburstable(victim);
            if (!unburstable && PastBurstPoint(victim))
            {
                // already too big for it: one surge of weight, then the beast overfills and bursts
                Utilities.HediffUtility.QueueWeightGain(victim, OverloadKilos);
                Messages.Message(
                    $"The {beast.def.label} wraps around {victim.LabelShort} and pumps - but {victim.LabelShort} is far too much for it. It swells, splits and bursts!",
                    new LookTargets(victim),
                    MessageTypeDefOf.PositiveEvent);
                BurstBeast(beast, map, victim.Position);
                return;
            }

            // out of its group AI first: a lord can't keep a world pawn
            beast.GetLord()?.Notify_PawnLost(beast, PawnLostCondition.Vanished, null);
            beast.jobs?.StopAll();
            beast.DeSpawn();
            Find.WorldPawns.PassToWorld(beast, PawnDiscardDecideMode.KeepForever);

            var h = (Hediff_RRConstricted)HediffMaker.MakeHediff(Defs.HediffDefOf.RR_Constricted, victim);
            h.constrictor = beast;
            h.load = h.startLoad = Mathf.Max(MinKilosPerPump, hunt?.load ?? Comp_RRConstrictorHunt.FullLoad);
            h.latchTick = Find.TickManager.TicksGame;
            h.Severity = 0.01f;
            victim.health.AddHediff(h);
            victim.jobs?.StopAll();

            bool enough = !unburstable && GlobalSettings.burstingEnabled && h.load >= KilosToBurst(victim);
            SoundDef.Named("RR_StomachGurgles_Heavy").PlayOneShot(new TargetInfo(victim.Position, map));
            if (unburstable)
            {
                Messages.Message(
                    $"{Find.ActiveLanguageWorker.WithIndefiniteArticle(beast.def.label).CapitalizeFirst()} wraps itself around {victim.LabelShort} and forces its proboscis into {victim.Possessive()} mouth! {victim.LabelShort} mends faster than it could ever burst {victim.ProObj()} - it will simply empty everything it has into {victim.ProObj()}.",
                    new LookTargets(victim),
                    MessageTypeDefOf.NegativeEvent);
                return;
            }
            Messages.Message(
                enough
                    ? $"{Find.ActiveLanguageWorker.WithIndefiniteArticle(beast.def.label).CapitalizeFirst()} wraps itself around {victim.LabelShort} and forces its proboscis into {victim.Possessive()} mouth! It is carrying more than enough to burst {victim.ProObj()} - tear it off!"
                    : $"{Find.ActiveLanguageWorker.WithIndefiniteArticle(beast.def.label).CapitalizeFirst()} wraps itself around {victim.LabelShort} and forces its proboscis into {victim.Possessive()} mouth! It can't burst {victim.ProObj()}, but it will empty everything it has left into {victim.ProObj()}.",
                new LookTargets(victim),
                enough ? MessageTypeDefOf.ThreatBig : MessageTypeDefOf.NegativeEvent);
        }

        /// <summary>A colonist puts a bound constrictor on someone. Checked by CannotLatchLeashedReason first.</summary>
        public static void LatchLeashed(Pawn victim, Pawn user, BoundConstrictorData data)
        {
            var h = (Hediff_RRConstricted)HediffMaker.MakeHediff(Defs.HediffDefOf.RR_Constricted, victim);
            h.leashed = true;
            h.bound = data ?? new BoundConstrictorData();
            h.load = h.startLoad = Mathf.Max(MinKilosPerPump, h.bound.LoadKilos);
            h.latchTick = Find.TickManager.TicksGame;
            h.Severity = 0.01f;
            victim.health.AddHediff(h);
            victim.jobs?.StopAll();

            SoundDef.Named("RR_StomachGurgles_Heavy").PlayOneShot(new TargetInfo(victim.Position, victim.Map));
            Messages.Message(
                $"{user.LabelShort} sets the bound gorge constrictor on {victim.LabelShort}. It coils around {victim.ProObj()} and starts to empty itself into {victim.ProObj()}. It will let go once it is empty, or once {victim.ProSubj()} can't hold any more.",
                new LookTargets(victim),
                MessageTypeDefOf.NeutralEvent);
        }

        /// <summary>Why a bound constrictor can't be put on this pawn, or null if it can.</summary>
        public static string CannotLatchLeashedReason(Pawn victim, BoundConstrictorData data = null)
        {
            if (data != null && data.HungryForUse)
                return "The bound constrictor is shrivelled and empty. Let it reswell in a constrictor vat first.";
            if (victim == null || victim.Dead || !victim.RaceProps.Humanlike)
                return "It only feeds on people.";
            if (victim.health.hediffSet.HasHediff(Defs.HediffDefOf.RR_Constricted))
                return $"{victim.LabelShort} already has a constrictor on {victim.ProObj()}.";
            if (!Unburstable(victim) && PastBurstPoint(victim, data?.ExtraBurstTiers ?? 0))
                return $"{victim.LabelShort} is already as big as it can make anyone.";
            return null;
        }

        // ------------------------------------------------------------ feeding

        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead || !pawn.Spawned || bursting)
                return;

            // held tight: the victim can't move or act
            if (pawn.IsHashIntervalTick(30))
                pawn.stances?.stunner?.StunFor(45, pawn, addBattleLog: false, showMote: false);

            // due-time rather than an exact tick match: a skipped tick never loses a pump
            if (Find.TickManager.TicksGame >= latchTick + (pumps + 1) * Interval)
            {
                pumps++;
                Pump();
                if (bursting || !pawn.health.hediffSet.hediffs.Contains(this))
                    return;
            }

            // no wrenching free alone: someone else has to tear it off
        }

        void Pump()
        {
            bool past = !Unburstable(pawn) && PastBurstPoint(pawn, ExtraTiers);
            if (past && leashed)
            {
                Messages.Message($"The bound gorge constrictor has pumped {pawn.LabelShort} as full as {pawn.ProSubj()} can go. It lets go, sated.", new LookTargets(pawn), MessageTypeDefOf.PositiveEvent);
                Release(null, sated: true);
                return;
            }
            if (past && !GlobalSettings.burstingEnabled)
            {
                // bursting is off: she can't take any more, so it's the beast that gives
                Messages.Message($"{pawn.LabelShort} can't hold any more - the {BeastLabel} swells past bearing instead, splits and bursts, letting {pawn.ProObj()} go!", new LookTargets(pawn), MessageTypeDefOf.PositiveEvent);
                BurstFree();
                return;
            }

            float kilos = Mathf.Min(load, Mathf.Max(MinKilosPerPump, startLoad / PumpsToEmpty));
            load -= kilos;
            if (kilos > 0f)
            {
                Utilities.HediffUtility.QueueWeightGain(pawn, kilos);
                FillMilk(kilos);
            }
            Severity = Mathf.Clamp(1f - LoadFraction, 0.01f, 1f);

            // past her burst point she doesn't burst outright: every pump strains her, and
            // each may be the one that does it - likelier and likelier as the strain builds
            if (past && kilos > 0f)
            {
                float strain = Hediff_RROverfilled.Strain(pawn, kilos, TroikaBound);
                if (strain >= 1f || Rand.Chance(Hediff_RROverfilled.BurstChancePerPump(strain, kilos)))
                {
                    Burst();
                    return;
                }
            }

            if (load <= 0.01f)
            {
                RunDry();
                return;
            }
            if (Rand.Chance(0.2f))
                SoundDef.Named("RR_StomachGurgles_Heavy").PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
        }

        /// <summary>Milk (nutrition) each kilo pumped into her turns into, at the start; up to twice that as she fills.</summary>
        const float MilkPerKilo = 0.004f;

        /// <summary>
        /// Pumped this full, a grown woman's breasts swell with it: she starts lactating (or
        /// keeps on), and part of every pump goes straight into her milk - more the fuller
        /// she is, up to what her breasts can hold (Lactation Expansion's milk store, which
        /// grows with her weight). Another reason to let it run.
        /// </summary>
        void FillMilk(float kilos)
        {
            if (!pawn.Spawned || !RRLactationUtility.CanLactate(pawn))
                return;
            float spilled = RRLactationUtility.InduceAndFill(pawn, kilos * MilkPerKilo * (1f + Severity));
            // what doesn't fit leaks out of her
            if (spilled > 0f)
                FleckMaker.ThrowDustPuffThick(pawn.DrawPos, pawn.Map, 0.8f, new Color(1f, 0.97f, 0.93f));
        }

        /// <summary>It has nothing left to give.</summary>
        void RunDry()
        {
            if (leashed)
            {
                Messages.Message($"The bound gorge constrictor has emptied everything it had into {pawn.LabelShort}. It lets go, shrivelled. Let it reswell in a constrictor vat.", new LookTargets(pawn), MessageTypeDefOf.NeutralEvent);
                Release(null);
                return;
            }

            Pawn beast = constrictor;
            constrictor = null;
            Map map = pawn.Map;
            IntVec3 at = pawn.Position;
            List<Pawn> others = TakeJoined();
            if (pawn.health.hediffSet.hediffs.Contains(this))
                pawn.health.RemoveHediff(this);
            foreach (Pawn other in others)
                KillSpent(other, at, map);
            Messages.Message($"The {beast?.def.label ?? "gorge constrictor"} has emptied itself into {pawn.LabelShort}. It drops off, a spent husk, and dies.", new LookTargets(pawn), MessageTypeDefOf.PositiveEvent);
            FilthMaker.TryMakeFilth(at, map, ThingDefOf.Filth_Vomit, 2);
            if (beast == null)
                return;
            if (Find.WorldPawns.Contains(beast))
                Find.WorldPawns.RemovePawn(beast);
            if (!beast.Destroyed && !beast.Dead)
            {
                GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(at, map, 1), map);
                beast.Kill(null);
            }
        }

        void Burst()
        {
            // a few seconds of swelling and straining first (Hediff_RRBursting), which
            // then calls ConsumeBeast and bursts the pawn
            bursting = true;
            WitnessBurst(pawn);
            MeldBurstUtility.BeginBurst(pawn,
                $"{pawn.LabelShort} swells past bearing in the {BeastLabel}'s grip and bursts - taking the beast with {pawn.ProObj()}!",
                bonusGluttonium: Rand.RangeInclusive(8, 14));
        }

        /// <summary>Everyone close enough to see it happen remembers it: by how they feel about weight.</summary>
        static void WitnessBurst(Pawn victim)
        {
            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail("RR_SawConstrictorBurst");
            if (def == null || !victim.Spawned)
                return;
            foreach (Pawn p in victim.Map.mapPawns.AllPawnsSpawned)
            {
                if (p == victim || !p.RaceProps.Humanlike || p.needs?.mood == null || p.Dead || p.Downed
                    || !p.Position.InHorDistOf(victim.Position, 12f) || !GenSight.LineOfSight(p.Position, victim.Position, victim.Map))
                    continue;
                WeightOpinion o = p.TryGetComp<Comps.ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.Neutral;
                // no otherPawn: the body is destroyed in the burst, and a memory pointing at a
                // discarded pawn breaks the save
                p.needs.mood.thoughts.memories.TryGainMemory(ThoughtMaker.MakeThought(def, o >= WeightOpinion.Love ? 1 : 0));
            }
        }

        /// <summary>The constrictor dies with its victim: called at the moment of the burst.</summary>
        public void ConsumeBeast()
        {
            bool troika = TroikaBound;
            Map map = pawn.MapHeld;
            IntVec3 at = pawn.PositionHeld;
            foreach (Pawn other in TakeJoined())
                if (!other.Destroyed)
                {
                    if (Find.WorldPawns.Contains(other))
                        Find.WorldPawns.RemovePawn(other);
                    other.Destroy();
                }
            if (troika && map != null)
                SeedNest(at, map);
            Pawn beast = constrictor;
            constrictor = null;
            if (beast == null)
                return;
            if (Find.WorldPawns.Contains(beast))
                Find.WorldPawns.RemovePawn(beast);
            if (!beast.Destroyed)
                beast.Destroy();
        }

        // ------------------------------------------------------------ letting go

        /// <summary>
        /// The constrictor lets go (torn off, struggled free, or its victim died of
        /// something else). It lands next to the victim, dazed, with whatever it has left.
        /// </summary>
        public void Release(Pawn tornOffBy, bool sated = false)
        {
            Pawn beast = constrictor;
            constrictor = null;
            Map map = pawn.MapHeld;
            IntVec3 at = pawn.PositionHeld;
            List<Pawn> others = TakeJoined();
            float share = others.Count > 0 ? Mathf.Max(0f, load) / (others.Count + 1) : Mathf.Max(0f, load);
            if (others.Count > 0)
                load = share;
            bool wasLeashed = leashed;
            BoundConstrictorData data = bound;
            float left = Mathf.Max(0f, load);
            leashed = false;
            bound = null;
            if (pawn.health.hediffSet.hediffs.Contains(this))
                pawn.health.RemoveHediff(this);

            if (wasLeashed)
            {
                if (data != null)
                {
                    data.LoadKilos = left;
                    if (!pawn.Dead)
                        ApplyAftereffects(data, sated);
                }
                if (map != null)
                    GenPlace.TryPlaceThing(CompRRBoundConstrictor.MakeItem(data), at, map, ThingPlaceMode.Near);
                return;
            }

            if (beast == null || map == null)
                return;
            if (Find.WorldPawns.Contains(beast))
                Find.WorldPawns.RemovePawn(beast);
            if (beast.Destroyed || beast.Dead)
                return;

            DropOff(beast, left, at, map, tornOffBy);
            foreach (Pawn other in others)
                DropOff(other, share, at, map, tornOffBy);
            FilthMaker.TryMakeFilth(at, map, ThingDefOf.Filth_Vomit, 2);
        }

        /// <summary>A constrictor lets go of her and lands next to her, dazed, with the load it has left.</summary>
        void DropOff(Pawn beast, float left, IntVec3 at, Map map, Pawn tornOffBy)
        {
            if (beast == null)
                return;
            if (Find.WorldPawns.Contains(beast))
                Find.WorldPawns.RemovePawn(beast);
            if (beast.Destroyed || beast.Dead)
                return;
            var hunt = beast.TryGetComp<Comp_RRConstrictorHunt>();
            if (hunt != null)
                hunt.load = left;
            GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(at, map, 2), map);
            beast.stances?.stunner?.StunFor(tornOffBy != null ? 300 : 180, tornOffBy ?? pawn, addBattleLog: false, showMote: true);
            if (tornOffBy != null)
                beast.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f, instigator: tornOffBy));
        }

        List<Pawn> TakeJoined()
        {
            List<Pawn> list = joined.Where(p => p != null).ToList();
            joined.Clear();
            return list;
        }

        /// <summary>Bursting is off and she is full: the constrictor (and anything coiled in with it) bursts instead, and lets her go.</summary>
        void BurstFree()
        {
            Map map = pawn.MapHeld;
            IntVec3 at = pawn.PositionHeld;
            List<Pawn> beasts = TakeJoined();
            beasts.Add(constrictor);
            constrictor = null;
            if (pawn.health.hediffSet.hediffs.Contains(this))
                pawn.health.RemoveHediff(this);
            if (map == null)
                return;
            foreach (Pawn beast in beasts)
            {
                if (beast == null)
                    continue;
                if (Find.WorldPawns.Contains(beast))
                    Find.WorldPawns.RemovePawn(beast);
                if (beast.Destroyed || beast.Dead)
                    continue;
                GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(at, map, 1), map);
                BurstBeast(beast, map, at);
            }
        }

        static void KillSpent(Pawn beast, IntVec3 at, Map map)
        {
            if (Find.WorldPawns.Contains(beast))
                Find.WorldPawns.RemovePawn(beast);
            if (beast.Destroyed || beast.Dead || map == null)
                return;
            GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(at, map, 1), map);
            beast.Kill(null);
        }

        /// <summary>
        /// Another constrictor reaches her while one is already wrapped around her: instead
        /// of fighting over her, it coils in with the first and empties itself into her too.
        /// Three make a troika.
        /// </summary>
        public bool TryJoin(Pawn beast)
        {
            if (leashed || bursting || beast == null || beast == constrictor || joined.Contains(beast) || !beast.Spawned || Coils >= 3)
                return false;
            var hunt = beast.TryGetComp<Comp_RRConstrictorHunt>();
            if (hunt == null || hunt.Spent)
                return false;
            float add = Mathf.Max(0f, hunt.load < 0f ? Comp_RRConstrictorHunt.FullLoad : hunt.load);
            beast.GetLord()?.Notify_PawnLost(beast, PawnLostCondition.Vanished, null);
            beast.jobs?.StopAll();
            beast.DeSpawn();
            Find.WorldPawns.PassToWorld(beast, PawnDiscardDecideMode.KeepForever);
            joined.Add(beast);
            load += add;
            startLoad += add;
            SoundDef.Named("RR_StomachGurgles_Heavy").PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
            if (TroikaBound)
                Find.LetterStack.ReceiveLetter("Constrictor troika",
                    $"A third constrictor has coiled around {pawn.LabelShort}. The three are bound to {pawn.ProObj()} now, like a mating troika: {pawn.ProSubj()} can't struggle free, and they pump faster together.\n\nIf {pawn.ProSubj()} bursts, the troika's brood will take root where {pawn.ProSubj()} stood. Tear them off - it will take a while.",
                    LetterDefOf.ThreatBig, new LookTargets(pawn));
            else
                Messages.Message($"Another {beast.def.label} coils in around {pawn.LabelShort} and forces its own proboscis in beside the first. Now there are {Coils} of them, pumping faster.",
                    new LookTargets(pawn), MessageTypeDefOf.ThreatBig);
            return true;
        }

        /// <summary>A troika's host burst: its brood takes root where she stood.</summary>
        static void SeedNest(IntVec3 at, Map map)
        {
            ThingDef nestDef = DefDatabase<ThingDef>.GetNamedSilentFail("RR_ConstrictorBroodNest");
            if (nestDef == null || !ModsConfig.AnomalyActive)
                return;
            IntVec3 cell = CellFinder.StandableCellNear(at, map, 3, c => GenAdj.OccupiedRect(c, Rot4.North, nestDef.size).All(x => x.InBounds(map) && x.Standable(map) && x.GetEdifice(map) == null));
            if (!cell.IsValid)
                return;
            Thing nest = ThingMaker.MakeThing(nestDef);
            nest.SetFaction(Faction.OfEntities);
            GenSpawn.Spawn(nest, cell, map);
            nest.TryGetComp<CompSpawnerPawn>()?.SpawnPawnsUntilPoints(70f);
            Find.LetterStack.ReceiveLetter("Troika brood", "Where the troika's host burst, its brood has already taken root: a nest of constrictor eggs, warm and pulsing. It will keep hatching until it is destroyed.",
                LetterDefOf.ThreatBig, new LookTargets(nest));
        }

        /// <summary>What a bound constrictor's mutations leave behind once it lets go.</summary>
        void ApplyAftereffects(BoundConstrictorData data, bool sated)
        {
            if (pumps < 5)
                return;
            foreach (RRConstrictorMutationDef m in data.mutations)
            {
                if (m.afterHediff != null)
                    pawn.health.AddHediff(m.afterHediff);
            }
            if (data.Euphoric)
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDef.Named("RR_ConstrictorAfterglow"));
            if (data.Persuasive && sated && pawn.TryGetComp<ThingComp_PawnAttitude>() is ThingComp_PawnAttitude attitude)
            {
                attitude.WeightOpinionFloat += 80f;
                Messages.Message($"{pawn.LabelShort} comes out of the constrictor's coils thinking about weight a little differently.", new LookTargets(pawn), MessageTypeDefOf.NeutralEvent);
            }
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            if (constrictor != null || leashed)
                Release(null);
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            Victims.Remove(pawn);
            // removed some other way (surgery, dev tools): don't lose the beast
            if (constrictor != null || leashed)
                Release(null);
        }

        /// <summary>The constrictor bursts on its own, overfilled: a spray of flesh and a little gluttonium.</summary>
        static void BurstBeast(Pawn beast, Map map, IntVec3 at)
        {
            FilthMaker.TryMakeFilth(at, map, ThingDefOf.Filth_Blood, 4);
            FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 2f, new Color(0.7f, 0.4f, 0.42f));
            map.GetComponent<MapComp_RRGasGrid>()?.AddGas(at, RRGasType.fatteningGas, 80);
            Thing glut = ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidGluttonium);
            glut.stackCount = Rand.RangeInclusive(2, 5);
            GenPlace.TryPlaceThing(glut, at, map, ThingPlaceMode.Near);
            beast.Kill(null);
        }

        public override string TipStringExtra
        {
            get
            {
                string left = (Coils > 1 ? $"{Coils} coils{(TroikaBound ? " - a troika, bound to her" : "")}. " : "") + $"Load left in it: {load:0} kg";
                if (Unburstable(pawn))
                    return $"{left}\n{pawn.LabelShort} regenerates faster than it can burst {pawn.ProObj()}: it will pump until it runs dry.";
                if (leashed)
                    return $"Bound: it will never burst anyone.\n{left}\nLets go on outgrowing: {BurstBodyType(pawn, ExtraTiers).defName.Substring(6).Replace('_', ' ')}";
                if (!GlobalSettings.burstingEnabled)
                    return $"{left} ({KilosToBurst(pawn):0} kg until {pawn.ProSubj()} can't hold any more).\nBursting is off: at that point the constrictor bursts instead, and lets go.\nTear it off - harder the fuller it still is.";
                string verdict = PastBurstPoint(pawn)
                    ? $"Past their burst point: every pump strains them, and any one may burst them (strain {(pawn.health.hediffSet.GetFirstHediffOfDef(Defs.HediffDefOf.RR_Overfilled)?.Severity ?? 0f):P0})."
                    : load >= KilosToBurst(pawn)
                        ? "It has enough left to push them past their burst point."
                        : "It will run dry before they reach their burst point.";
                return $"{left} ({KilosToBurst(pawn):0} kg to their burst point). {verdict}\nBurst point: outgrowing {BurstBodyType(pawn).defName.Substring(6).Replace('_', ' ')}\nTear it off - harder the fuller it still is.";
            }
        }
    }
}
