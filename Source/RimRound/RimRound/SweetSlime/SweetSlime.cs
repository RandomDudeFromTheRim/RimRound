using System.Collections.Generic;
using System.Linq;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.SweetSlime
{
    // Sweet slime: a revenant-like entity after Lobotomy Corporation's Melting Love.
    // It stalks unseen, creeps up on one person at a time (sleepers first), bursts
    // pink slime over them and the ground around them, and leaves a seed of itself
    // inside. The seed grows like a metalhorror's: hidden at first, then a warm,
    // clinging comfort, then a flushed, spreading infection, until the host melts into
    // a slime cocoon. Nothing about it kills: break the cocoon and the host comes out
    // heavier; leave it and they come out far heavier, budding slime spawn that carry
    // the infestation on. Surgery can extract the seed before it gets that far.

    public static class SweetSlimeUtility
    {
        public static HediffDef Infestation => DefDatabase<HediffDef>.GetNamed("RR_SweetSlimeInfestation");
        public static HediffDef Veil => DefDatabase<HediffDef>.GetNamed("RR_SlimeVeil");
        public static ThingDef Filth => DefDatabase<ThingDef>.GetNamed("RR_Filth_SweetSlime");

        /// <summary>Someone a puffkiss can swell.</summary>
        public static bool CanPuff(Pawn p) =>
            p != null && !p.Dead && p.RaceProps.Humanlike && Utilities.HediffUtility.WeightHediff(p) != null;

        public static bool CanInfest(Pawn p) =>
            p != null && !p.Dead && p.RaceProps.Humanlike && !p.health.hediffSet.HasHediff(Infestation)
            && p.TryGetComp<Comps.FullnessAndDietStats_ThingComp>() is Comps.FullnessAndDietStats_ThingComp fnd && !fnd.Disabled;

        public static bool Infest(Pawn p)
        {
            if (!CanInfest(p))
                return false;
            p.health.AddHediff(Infestation);
            return true;
        }

        /// <summary>
        /// A wet, smothering hug: the same moodlets as RimRound's wet smother, by how the
        /// one being hugged feels about weight.
        /// </summary>
        public static void SmotherHug(Pawn slime, Pawn target)
        {
            WeightOpinion o = target.TryGetComp<Comps.ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.Neutral;
            string defName =
                o <= WeightOpinion.Hate ? "RR_WetSmotherHate" :
                o == WeightOpinion.Dislike ? "RR_WetSmotherDislike" :
                o <= WeightOpinion.Neutral ? "RR_WetSmotherNeutral" :
                o <= WeightOpinion.Like ? "RR_WetSmotherGood" : "RR_WetSmotherGreat";
            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            if (def != null && target.needs?.mood != null)
                target.needs.mood.thoughts.memories.TryGainMemory(ThoughtMaker.MakeThought(def, Mathf.Min(2, def.stages.Count - 1)));
            target.stances?.stunner?.StunFor(60, slime, addBattleLog: false);
            // they come out of it dripping, and won't stop talking about it for a day
            SlimeCoatUtility.Coat(target, 15000);
            ThoughtDef hugged = DefDatabase<ThoughtDef>.GetNamedSilentFail("RR_SlimeHugged");
            if (hugged != null && target.needs?.mood != null)
                target.needs.mood.thoughts.memories.TryGainMemory(ThoughtMaker.MakeThought(hugged,
                    o <= WeightOpinion.Dislike ? 0 : o <= WeightOpinion.Neutral ? 1 : 2));
        }

        /// <summary>A light puffkiss: a little sudden swelling, and a heart.</summary>
        public static void Puffkiss(Pawn target)
        {
            HediffDef sudden = Defs.HediffDefOf.RimRound_SuddenWeightGain;
            Hediff h = target.health.hediffSet.GetFirstHediffOfDef(sudden);
            if (h == null)
            {
                h = HediffMaker.MakeHediff(sudden, target);
                h.Severity = 0f;
                target.health.AddHediff(h);
            }
            h.Severity = Mathf.Min(h.def.maxSeverity, h.Severity + 0.1f);
            if (target.Spawned)
                FleckMaker.ThrowMetaIcon(target.Position, target.Map, FleckDefOf.Heart);
        }

        /// <summary>A splash of pink slime over a spot, like a firefoam pop.</summary>
        public static void Burst(IntVec3 at, Map map, float radius)
        {
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, radius, useCenter: true))
            {
                if (c.InBounds(map) && c.Walkable(map) && Rand.Chance(c == at ? 1f : 0.75f))
                    FilthMaker.TryMakeFilth(c, map, Filth, c == at ? 3 : Rand.RangeInclusive(1, 2));
            }
            FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, radius, new Color(1f, 0.6f, 0.8f));
            FleckMaker.Static(at, map, FleckDefOf.PsycastAreaEffect, radius);
        }
    }

    public class CompProperties_RRSweetSlime : CompProperties
    {
        /// <summary>The spawn are visible, faster to return and only sometimes take.</summary>
        public bool isSpawn;
        public FloatRange huntCooldownDays = new FloatRange(1.5f, 2.5f);
        public float infestChance = 1f;
        public float burstRadius = 2.4f;
        public int revealedTicks = 7500;

        public CompProperties_RRSweetSlime()
        {
            compClass = typeof(Comp_RRSweetSlime);
        }
    }

    /// <summary>
    /// Keeps the sweet slime's rhythm: when it may hunt again, and whether it is
    /// hidden. The main slime is veiled (psychic invisibility, like a revenant's)
    /// except for a few hours after each gift, when it is spent and can be shot.
    /// </summary>
    public class Comp_RRSweetSlime : ThingComp
    {
        int nextHuntTick = -1;
        int revealedUntil = -1;
        int fleeUntil = -1;

        /// <summary>Spent and seen: once hurt, it slips back out of sight this soon.</summary>
        const int HurtRevealTicks = 900;
        const int FleeTicks = 1200;

        public CompProperties_RRSweetSlime Props => (CompProperties_RRSweetSlime)props;
        Pawn Slime => (Pawn)parent;

        public bool ReadyToHunt => Find.TickManager.TicksGame >= nextHuntTick;
        public bool Fleeing => Find.TickManager.TicksGame < fleeUntil;

        /// <summary>
        /// It never fights back - it gets away. Anyone who lays hands on it gets stuck
        /// in it for a moment (hugged, slimed and puffed a little), it flees, and a
        /// spent slime that gets hurt hides again far sooner.
        /// </summary>
        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);
            Pawn slime = Slime;
            if (slime.Dead || !slime.Spawned)
                return;
            int now = Find.TickManager.TicksGame;
            fleeUntil = now + FleeTicks;
            if (!Props.isSpawn && revealedUntil > now + HurtRevealTicks)
                revealedUntil = now + HurtRevealTicks;
            if (dinfo.Instigator is Pawn attacker && !attacker.Dead && attacker.Spawned && !dinfo.Def.isRanged
                && attacker.Position.AdjacentTo8Way(slime.Position) && Rand.Chance(0.6f))
            {
                attacker.stances?.stunner?.StunFor(Props.isSpawn ? 120 : 240, slime, addBattleLog: false);
                SlimeCoatUtility.Coat(attacker, 15000);
                if (SweetSlimeUtility.CanPuff(attacker))
                    SweetSlimeUtility.Puffkiss(attacker);
                FilthMaker.TryMakeFilth(attacker.Position, attacker.Map, SweetSlimeUtility.Filth);
                MoteMaker.ThrowText(attacker.DrawPos, attacker.Map, "stuck in slime", new Color(1f, 0.6f, 0.8f));
            }
            if (slime.CurJobDef != JobDefOf.Flee)
                slime.jobs?.EndCurrentJob(JobCondition.InterruptForced);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (nextHuntTick < 0)
                nextHuntTick = Find.TickManager.TicksGame + (Props.isSpawn ? 2500 : GenDate.TicksPerDay / 2);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (Props.isSpawn || !Slime.Spawned || !Slime.IsHashIntervalTick(120))
                return;
            bool veiled = Slime.health.hediffSet.HasHediff(SweetSlimeUtility.Veil);
            // it knits itself back together while nothing can see it
            if (veiled)
                Slime.health.hediffSet.hediffs.OfType<Hediff_Injury>().FirstOrDefault()?.Heal(1.5f);
            bool shouldVeil = !Slime.Downed && Find.TickManager.TicksGame >= revealedUntil;
            if (shouldVeil && !veiled)
                Slime.health.AddHediff(SweetSlimeUtility.Veil);
            else if (!shouldVeil && veiled)
                Slime.health.RemoveHediff(Slime.health.hediffSet.GetFirstHediffOfDef(SweetSlimeUtility.Veil));
        }

        /// <summary>Someone to visit: sleepers first, then whoever is alone.</summary>
        public Pawn ChooseTarget()
        {
            Pawn slime = Slime;
            return slime.Map.mapPawns.FreeColonistsAndPrisonersSpawned
                .Where(p => SweetSlimeUtility.CanInfest(p) && slime.CanReach(p, PathEndMode.Touch, Danger.Deadly))
                .OrderByDescending(p => (p.InBed() || !p.Awake() ? 2 : 0) + (p.Position.GetRoom(p.Map)?.ContainedAndAdjacentThings.OfType<Pawn>().Count() <= 1 ? 1 : 0))
                .ThenBy(p => p.Position.DistanceToSquared(slime.Position))
                .FirstOrDefault();
        }

        /// <summary>
        /// The gift, after a smothering hug (JobDriver_RRSlimeGift): a puffkiss that
        /// swells them a little, slime over them and the ground, a seed inside, then rest.
        /// </summary>
        public void Gift(Pawn target)
        {
            Pawn slime = Slime;
            SweetSlimeUtility.Puffkiss(target);
            SweetSlimeUtility.Burst(target.Position, target.Map, Props.burstRadius);
            bool took = Rand.Chance(Props.infestChance) && SweetSlimeUtility.Infest(target);
            nextHuntTick = Find.TickManager.TicksGame + Mathf.RoundToInt(Props.huntCooldownDays.RandomInRange * GenDate.TicksPerDay);
            revealedUntil = Find.TickManager.TicksGame + Props.revealedTicks;
            if (!Props.isSpawn)
            {
                Find.LetterStack.ReceiveLetter(
                    "Sweet slime",
                    $"Something slipped up on {target.LabelShort}, folded {target.ProObj()} into a smothering, sticky hug and planted a warm, puffy kiss on {target.Possessive()} lips - then burst over {target.ProObj()} in a wave of pink slime. {(took ? $"{target.ProSubj().CapitalizeFirst()} feels strangely calm." : "")}\n\nThe thing that did it is out in the open for now, spent and sluggish - a translucent pink mass with bones floating in it. It will slip out of sight again in a few hours.",
                    LetterDefOf.ThreatBig, new LookTargets(slime, target));
            }
            else if (took)
            {
                Messages.Message($"A slime spawn hugs {target.LabelShort} tight and leaves {target.ProObj()} dripping.", target, MessageTypeDefOf.NegativeEvent);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (Props.isSpawn)
                return null;
            return Find.TickManager.TicksGame < revealedUntil ? "Spent and sluggish: it can't hide right now." : null;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextHuntTick, "nextHuntTick", -1);
            Scribe_Values.Look(ref revealedUntil, "revealedUntil", -1);
            Scribe_Values.Look(ref fleeUntil, "fleeUntil", -1);
        }
    }

    /// <summary>Runs from whoever is hurting it, well away, before anything else.</summary>
    public class JobGiver_RRSweetSlimeFlee : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var comp = pawn.TryGetComp<Comp_RRSweetSlime>();
            if (comp == null || !comp.Fleeing)
                return null;
            List<Thing> threats = pawn.Map.mapPawns.AllPawnsSpawned
                .Where(p => p != pawn && !p.Downed && !p.Dead && p.RaceProps.Humanlike && p.Position.InHorDistOf(pawn.Position, 18f))
                .Cast<Thing>().ToList();
            if (threats.Count == 0)
                return null;
            IntVec3 dest = CellFinderLoose.GetFleeDest(pawn, threats, 24f);
            if (!dest.IsValid || dest == pawn.Position)
                return null;
            Job job = JobMaker.MakeJob(JobDefOf.Flee, dest, threats[0]);
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            job.expiryInterval = 300;
            return job;
        }
    }

    /// <summary>Goes to visit someone when the slime is ready to.</summary>
    public class JobGiver_RRSweetSlimeGift : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var comp = pawn.TryGetComp<Comp_RRSweetSlime>();
            if (comp == null || !comp.ReadyToHunt)
                return null;
            Pawn target = comp.ChooseTarget();
            if (target == null)
                return null;
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RR_SlimeGift"), target);
            job.expiryInterval = 2500;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }

    public class JobDriver_RRSlimeGift : JobDriver
    {
        Pawn Target => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !SweetSlimeUtility.CanInfest(Target));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            // a smothering hug: it wraps round them and holds them still
            Toil hug = Toils_General.Wait(180, TargetIndex.A);
            hug.initAction = () => SweetSlimeUtility.SmotherHug(pawn, Target);
            hug.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Target);
                if (pawn.IsHashIntervalTick(30))
                {
                    Target.stances?.stunner?.StunFor(45, pawn, addBattleLog: false, showMote: false);
                    FilthMaker.TryMakeFilth(Target.Position, pawn.Map, SweetSlimeUtility.Filth);
                }
            };
            yield return hug;
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () => pawn.TryGetComp<Comp_RRSweetSlime>()?.Gift(Target),
            };
        }
    }

    /// <summary>
    /// The seed of slime inside a host. Hidden for most of a day, then a clinging,
    /// comforting warmth (a little weight, slime trailing off them), then flushed pink
    /// and catching: anyone who lingers beside them may get slimed too. When it is
    /// full grown, the host melts into a slime cocoon. Extract it by surgery.
    /// </summary>
    public class Hediff_RRSweetSlimeInfestation : HediffWithComps
    {
        const int CheckInterval = 2000;
        const float SpreadChance = 0.2f;

        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead || !pawn.IsHashIntervalTick(CheckInterval))
                return;
            int stage = CurStageIndex;
            if (stage >= 1)
            {
                Utilities.HediffUtility.QueueWeightGain(pawn, stage >= 2 ? 3f : 1.5f);
                if (pawn.Spawned && Rand.Chance(0.5f))
                    FilthMaker.TryMakeFilth(pawn.Position, pawn.Map, SweetSlimeUtility.Filth);
            }
            if (stage >= 2 && pawn.Spawned)
            {
                foreach (Pawn other in GenAdjFast.AdjacentCells8Way(pawn.Position).SelectMany(c => c.InBounds(pawn.Map) ? c.GetThingList(pawn.Map).OfType<Pawn>() : Enumerable.Empty<Pawn>()).ToList())
                {
                    if (Rand.Chance(SpreadChance) && SweetSlimeUtility.Infest(other))
                    {
                        FilthMaker.TryMakeFilth(other.Position, other.Map, SweetSlimeUtility.Filth, 2);
                        Messages.Message($"{pawn.LabelShort}'s slime has got onto {other.LabelShort}.", other, MessageTypeDefOf.NegativeEvent);
                    }
                }
            }
            if (Severity >= def.maxSeverity - 0.001f)
                Engulf();
        }

        void Engulf()
        {
            Pawn host = pawn;
            host.health.RemoveHediff(this);
            if (!host.Spawned)
            {
                // away from home: it just dissolves into them
                Utilities.HediffUtility.QueueWeightGain(host, 60f);
                return;
            }
            Map map = host.Map;
            IntVec3 at = host.Position;
            var cocoon = (Building_RRSlimeCocoon)ThingMaker.MakeThing(ThingDef.Named("RR_SlimeCocoon"));
            GenSpawn.Spawn(cocoon, at, map, WipeMode.VanishOrMoveAside);
            cocoon.Enclose(host);
            SweetSlimeUtility.Burst(at, map, 1.9f);
            Find.LetterStack.ReceiveLetter(
                $"{host.LabelShort} melted",
                $"The slime inside {host.LabelShort} has swallowed {host.ProObj()} whole, and hardened into a glistening cocoon.\n\nBreak the cocoon open and {host.LabelShort} will come out heavier, but free. Leave it, and in two days {host.ProSubj()} will come out a great deal heavier - and the cocoon will bud slime spawn that go looking for new hosts.",
                LetterDefOf.ThreatBig, cocoon);
        }
    }

    /// <summary>
    /// A melted host, held in hardened slime. Destroy it to free them early; left alone
    /// it matures, releases them much heavier and buds slime spawn. Never kills.
    /// </summary>
    public class Building_RRSlimeCocoon : Building, IThingHolder
    {
        const int MatureTicks = 2 * GenDate.TicksPerDay;
        const float EarlyKilos = 40f;
        const float MatureKilos = 160f;

        ThingOwner<Pawn> inner;
        int enclosedTick = -1;
        bool released;
        /// <summary>How big the one inside is drawn (RimRound's mesh size): the cocoon grows to hold them.</summary>
        float hostScale = 1f;
        Graphic scaledGraphic;

        public override Graphic Graphic
        {
            get
            {
                if (hostScale <= 1.01f)
                    return base.Graphic;
                if (scaledGraphic == null)
                    scaledGraphic = base.Graphic.GetCopy(def.graphicData.drawSize * hostScale, null);
                return scaledGraphic;
            }
        }

        public Building_RRSlimeCocoon()
        {
            inner = new ThingOwner<Pawn>(this, oneStackOnly: true);
        }

        public Pawn Host => inner.Count > 0 ? inner[0] : null;

        public ThingOwner GetDirectlyHeldThings() => inner;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

        public void Enclose(Pawn p)
        {
            hostScale = Mathf.Max(1f, (Utilities.RacialBodyTypeInfoUtility.GetRacialBodyTypeInfo(p)?.meshSize ?? 1f) * 1.1f);
            scaledGraphic = null;
            p.DeSpawnOrDeselect();
            inner.TryAddOrTransfer(p);
            enclosedTick = Find.TickManager.TicksGame;
            if (Spawned)
                Map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);
        }

        protected override void Tick()
        {
            base.Tick();
            if (Host == null || !this.IsHashIntervalTick(250))
                return;
            if (Find.TickManager.TicksGame >= enclosedTick + MatureTicks)
                Hatch();
        }

        void Hatch()
        {
            Map map = Map;
            IntVec3 at = Position;
            Pawn host = Release(MatureKilos);
            int count = Rand.RangeInclusive(1, 2);
            PawnKindDef spawnKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("RR_SlimeSpawn");
            if (spawnKind != null)
            {
                for (int i = 0; i < count; i++)
                    GenSpawn.Spawn(PawnGenerator.GeneratePawn(spawnKind, Faction.OfEntities), CellFinder.RandomClosewalkCellNear(at, map, 2), map);
            }
            Find.LetterStack.ReceiveLetter(
                "Slime cocoon hatched",
                $"The slime cocoon has split open. {host?.LabelShort ?? "Its host"} slides out, dazed and a great deal heavier - and {count} slime spawn ooze out after, already looking for someone new.",
                LetterDefOf.ThreatBig, new LookTargets(at, map));
            Destroy(DestroyMode.Vanish);
        }

        /// <summary>Lets the host out, heavier; safe to call more than once.</summary>
        Pawn Release(float kilos)
        {
            if (released)
                return null;
            released = true;
            Pawn host = Host;
            if (host == null || !Spawned)
                return host;
            inner.TryDrop(host, Position, Map, ThingPlaceMode.Near, out _);
            Utilities.HediffUtility.QueueWeightGain(host, kilos);
            host.stances?.stunner?.StunFor(300, host, addBattleLog: false);
            SlimeCoatUtility.Coat(host, 30000);
            SweetSlimeUtility.Burst(Position, Map, 1.9f);
            return host;
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Spawned && !released)
            {
                Pawn host = Release(EarlyKilos);
                if (host != null && mode != DestroyMode.Vanish)
                    Messages.Message($"The slime cocoon bursts open and {host.LabelShort} spills out, heavier but free.", host, MessageTypeDefOf.PositiveEvent);
            }
            base.DeSpawn(mode);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            if (Host != null)
                s += (s.NullOrEmpty() ? "" : "\n") + $"Contains {Host.LabelShort}. Matures in {(enclosedTick + MatureTicks - Find.TickManager.TicksGame).ToStringTicksToPeriod()}.";
            return s;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref inner, "inner", this);
            Scribe_Values.Look(ref enclosedTick, "enclosedTick", -1);
            Scribe_Values.Look(ref released, "released");
            Scribe_Values.Look(ref hostScale, "hostScale", 1f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && inner == null)
                inner = new ThingOwner<Pawn>(this, oneStackOnly: true);
        }
    }

    /// <summary>Anomaly incident: a sweet slime slips onto the map, already hidden.</summary>
    public class IncidentWorker_RRSweetSlime : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return ModsConfig.AnomalyActive && parms.target is Map map && map.mapPawns.FreeColonistsSpawnedCount > 0
                && DefDatabase<PawnKindDef>.GetNamedSilentFail("RR_SweetSlime") != null;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 cell, map, CellFinder.EdgeRoadChance_Hostile))
                return false;
            Pawn slime = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("RR_SweetSlime"), Faction.OfEntities);
            GenSpawn.Spawn(slime, cell, map);
            slime.health.AddHediff(SweetSlimeUtility.Veil);
            SendStandardLetter(parms, LookTargets.Invalid);
            return true;
        }
    }
}
