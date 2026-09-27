using System.Collections.Generic;
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
    /// stomach perks let them take more - before the load runs out, they swell for
    /// a few seconds and burst, and the constrictor with them. If it runs dry first,
    /// it drops off, a spent husk. Wounding it before it latches spills its load.
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
        const int StruggleIntervalTicks = 250;
        const float StruggleChance = 0.03f;

        public Pawn constrictor;
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

        /// <summary>How much of what it latched with is still in it: 1 when it latches, 0 when spent.</summary>
        public float LoadFraction => startLoad <= 0f ? 0f : Mathf.Clamp01(load / startLoad);

        /// <summary>Which coil sprite to draw: it is biggest when it latches and shrinks as it empties.</summary>
        public int Stage => Mathf.Clamp((int)(LoadFraction * Stages), 0, Stages - 1);

        /// <summary>How far through the current size stage it still is, 0..1.</summary>
        public float WithinStage => Mathf.Clamp01(LoadFraction * Stages - Stage);

        /// <summary>0 just after a pump, rising to 1 as the next one comes: drives the pumping animation.</summary>
        public float PumpPhase => latchTick < 0 ? 0f : (float)((Find.TickManager.TicksGame - latchTick) % Interval) / Interval;

        /// <summary>Ticks between pumps: a bound constrictor's mutations can quicken it.</summary>
        public int Interval => Mathf.Max(30, Mathf.RoundToInt(PumpIntervalTicks * (bound?.PumpIntervalFactor ?? 1f)));

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
            Scribe_Values.Look(ref leashed, "leashed");
            Scribe_Deep.Look(ref bound, "bound");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (leashed && bound == null)
                    bound = new BoundConstrictorData();
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
            int tier = extraTiers;
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
            if (PastBurstPoint(victim))
            {
                // already too big for it: one surge of weight, then the beast overfills and bursts
                Utilities.HediffUtility.QueueWeightGain(victim, OverloadKilos);
                Messages.Message(
                    $"The gorge constrictor wraps around {victim.LabelShort} and pumps - but {victim.LabelShort} is far too much for it. It swells, splits and bursts!",
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

            bool enough = h.load >= KilosToBurst(victim);
            SoundDef.Named("RR_StomachGurgles_Heavy").PlayOneShot(new TargetInfo(victim.Position, map));
            Messages.Message(
                enough
                    ? $"A gorge constrictor wraps itself around {victim.LabelShort} and forces its proboscis into {victim.Possessive()} mouth! It is carrying more than enough to burst {victim.ProObj()} - tear it off!"
                    : $"A gorge constrictor wraps itself around {victim.LabelShort} and forces its proboscis into {victim.Possessive()} mouth! It has lost too much to burst {victim.ProObj()}, but it will empty everything it has left into {victim.ProObj()}.",
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
            if (PastBurstPoint(victim, data?.ExtraBurstTiers ?? 0))
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

            // a lone victim can still wrench free, rarely - less and less as they fill
            if (pawn.IsHashIntervalTick(StruggleIntervalTicks) && Rand.Chance(StruggleChance * (1f - Severity)))
            {
                Messages.Message($"{pawn.LabelShort} wrenches free of the gorge constrictor!", new LookTargets(pawn), MessageTypeDefOf.PositiveEvent);
                Release(null);
            }
        }

        void Pump()
        {
            if (PastBurstPoint(pawn, ExtraTiers))
            {
                if (leashed)
                {
                    Messages.Message($"The bound gorge constrictor has pumped {pawn.LabelShort} as full as {pawn.ProSubj()} can go. It lets go, sated.", new LookTargets(pawn), MessageTypeDefOf.PositiveEvent);
                    Release(null, sated: true);
                    return;
                }
                Burst();
                return;
            }

            float kilos = Mathf.Min(load, Mathf.Max(MinKilosPerPump, startLoad / PumpsToEmpty));
            load -= kilos;
            if (kilos > 0f)
                Utilities.HediffUtility.QueueWeightGain(pawn, kilos);
            Severity = Mathf.Clamp(1f - LoadFraction, 0.01f, 1f);

            if (load <= 0.01f)
            {
                RunDry();
                return;
            }
            if (Rand.Chance(0.2f))
                SoundDef.Named("RR_StomachGurgles_Heavy").PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
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
            if (pawn.health.hediffSet.hediffs.Contains(this))
                pawn.health.RemoveHediff(this);
            Messages.Message($"The gorge constrictor has emptied itself into {pawn.LabelShort}. It drops off, a spent husk, and dies.", new LookTargets(pawn), MessageTypeDefOf.PositiveEvent);
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
            MeldBurstUtility.BeginBurst(pawn,
                $"{pawn.LabelShort} swells past bearing in the gorge constrictor's grip and bursts - taking the beast with {pawn.ProObj()}!",
                bonusGluttonium: Rand.RangeInclusive(8, 14));
        }

        /// <summary>The constrictor dies with its victim: called at the moment of the burst.</summary>
        public void ConsumeBeast()
        {
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

            var hunt = beast.TryGetComp<Comp_RRConstrictorHunt>();
            if (hunt != null)
                hunt.load = left;
            GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(at, map, 2), map);
            beast.stances?.stunner?.StunFor(tornOffBy != null ? 300 : 180, tornOffBy ?? pawn, addBattleLog: false, showMote: true);
            if (tornOffBy != null)
                beast.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f, instigator: tornOffBy));
            FilthMaker.TryMakeFilth(at, map, ThingDefOf.Filth_Vomit, 2);
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
                string left = $"Load left in it: {load:0} kg";
                if (leashed)
                    return $"Bound: it will never burst anyone.\n{left}\nLets go on outgrowing: {BurstBodyType(pawn, ExtraTiers).defName.Substring(6).Replace('_', ' ')}";
                string verdict = load >= KilosToBurst(pawn)
                    ? "It has enough left to burst them."
                    : "It will run dry before they burst.";
                return $"{left} ({KilosToBurst(pawn):0} kg to their burst point). {verdict}\nBursts on outgrowing: {BurstBodyType(pawn).defName.Substring(6).Replace('_', ' ')}\nTear it off before then - harder the fuller it still is.";
            }
        }
    }
}
