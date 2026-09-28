using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// Runs the bloat that replaces an awoken unnatural corpse's killing blow:
    /// the corpse swells up and bursts into flesh, and its victim survives
    /// downed, heavier and afflicted. A saved game component, ticked by the game,
    /// so it survives save/load and behaves the same paused or at any speed.
    /// </summary>
    public class CorpseBloatManager : GameComponent
    {
        private class BloatState : IExposable
        {
            public Pawn attacker;
            public Pawn victim;
            public int startTick;
            // where to burst if the corpse vanishes before the swell completes
            public Map map;
            public IntVec3 lastPosition = IntVec3.Invalid;

            public void ExposeData()
            {
                Scribe_References.Look(ref attacker, "attacker");
                Scribe_References.Look(ref victim, "victim");
                Scribe_Values.Look(ref startTick, "startTick");
                Scribe_References.Look(ref map, "map");
                Scribe_Values.Look(ref lastPosition, "lastPosition", IntVec3.Invalid);
            }
        }

        // Vanilla makes the awoken corpse vanish 600 ticks after its "kill", so
        // burst just before that.
        const int SwellDurationTicks = 590;

        private List<BloatState> activeBloats = new List<BloatState>();

        public CorpseBloatManager(Game game)
        {
        }

        public static void StartBloat(Pawn attacker, Pawn victim)
        {
            Current.Game?.GetComponent<CorpseBloatManager>()?.Start(attacker, victim);
        }

        private void Start(Pawn attacker, Pawn victim)
        {
            if (activeBloats.Any(b => b.attacker == attacker))
                return;

            activeBloats.Add(new BloatState
            {
                attacker = attacker,
                victim = victim,
                startTick = Find.TickManager.TicksGame,
                map = attacker.MapHeld,
                lastPosition = attacker.PositionHeld
            });

            if (victim?.stances?.stunner != null)
                victim.stances.stunner.StunFor(900, attacker, addBattleLog: false);

            Messages.Message(
                $"{attacker.LabelShort} swells rapidly, its distended form pulsing with dark energy!",
                attacker,
                MessageTypeDefOf.ThreatBig);
        }

        public override void GameComponentTick()
        {
            if (activeBloats.Count == 0)
                return;

            int curTick = Find.TickManager.TicksGame;
            for (int i = activeBloats.Count - 1; i >= 0; i--)
            {
                BloatState state = activeBloats[i];
                if (state.attacker != null && state.attacker.Spawned)
                {
                    state.map = state.attacker.Map;
                    state.lastPosition = state.attacker.Position;
                }

                int elapsed = curTick - state.startTick;
                if (elapsed < SwellDurationTicks)
                {
                    if (elapsed > 0 && elapsed % 10 == 0 && state.attacker != null && !state.attacker.Dead)
                        Swell(state.attacker);
                }
                else
                {
                    activeBloats.RemoveAt(i);
                    Explode(state);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref activeBloats, "activeBloats", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (activeBloats == null)
                    activeBloats = new List<BloatState>();
                activeBloats.RemoveAll(b => b == null || b.map == null);
            }
        }

        static void Swell(Pawn attacker)
        {
            var sudden = attacker.health?.hediffSet?.GetFirstHediffOfDef(RimRound.Defs.HediffDefOf.RimRound_SuddenWeightGain);
            if (sudden != null)
                sudden.Severity += 0.15f;
            else if (attacker.health != null)
            {
                var s = HediffMaker.MakeHediff(RimRound.Defs.HediffDefOf.RimRound_SuddenWeightGain, attacker);
                s.Severity = 0.15f;
                attacker.health.AddHediff(s);
            }
        }

        static void Explode(BloatState state)
        {
            // burst where the corpse last stood, even if it has already vanished
            Map map = state.map;
            IntVec3 pos = state.lastPosition;
            if (map == null || !pos.InBounds(map)) return;

            if (state.attacker != null && !state.attacker.Dead && !state.attacker.Destroyed)
                state.attacker.Kill(null);

            // Spawn blob wall cluster (roughly 4x4 lump)
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pos, 2.5f, useCenter: true))
            {
                if (cell.InBounds(map) && cell.Walkable(map) && Rand.Chance(0.7f))
                {
                    var wall = ThingMaker.MakeThing(ThingDef.Named("RR_BlobWall"));
                    GenSpawn.Spawn(wall, cell, map, Rot4.North, WipeMode.Vanish);
                }
            }

            // Void gluttonium drops on adjacent walkable cells
            IntVec3[] offsets = {
                new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0),
                new IntVec3(0, 0, 1), new IntVec3(0, 0, -1)
            };
            foreach (var offset in offsets)
            {
                IntVec3 cell = pos + offset;
                if (cell.InBounds(map) && cell.Walkable(map))
                {
                    int count = Rand.RangeInclusive(3, 8);
                    var ore = ThingMaker.MakeThing(ThingDef.Named("RR_VoidGluttonium"));
                    ore.stackCount = count;
                    GenPlace.TryPlaceThing(ore, cell, map, ThingPlaceMode.Near);
                }
            }

            // Down the victim and apply debuffs
            if (state.victim != null && !state.victim.Dead && !state.victim.Downed)
            {
                HealthUtility.DamageUntilDowned(state.victim);

                var sudden = state.victim.health?.hediffSet?.GetFirstHediffOfDef(RimRound.Defs.HediffDefOf.RimRound_SuddenWeightGain);
                if (sudden != null)
                    sudden.Severity = 0.85f;
                else if (state.victim.health != null)
                {
                    var s = HediffMaker.MakeHediff(RimRound.Defs.HediffDefOf.RimRound_SuddenWeightGain, state.victim);
                    s.Severity = 0.85f;
                    state.victim.health.AddHediff(s);
                }

                if (RimRound.Defs.HediffDefOf.RR_BloatedDeathAffliction != null)
                {
                    var affliction = HediffMaker.MakeHediff(RimRound.Defs.HediffDefOf.RR_BloatedDeathAffliction, state.victim);
                    affliction.Severity = 1.0f;
                    state.victim.health.AddHediff(affliction);
                }
            }

            // Witness thoughts
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                float dist = (p.Position - pos).LengthHorizontal;
                if (dist > 30f) continue;

                var witnessAtt = p.TryGetComp<ThingComp_PawnAttitude>();
                if (witnessAtt == null) continue;

                if (witnessAtt.weightOpinion >= WeightOpinion.NeutralPlus)
                    p.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDef.Named("RR_WitnessedBloatedDeath_Aroused"));
                else
                    p.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDef.Named("RR_WitnessedBloatedDeath_Horror"));
            }
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.TakeDamage))]
    public class UnnaturalCorpse_BloatPatch
    {
        // Skipping TakeDamage leaves its result null unless we set one, and other mods'
        // TakeDamage postfixes (DamageMotes, EBSG...) read it and crash on null.
        static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!(__instance is Pawn victim))
                return true;

            if (dinfo.Def != DamageDefOf.Psychic || dinfo.Amount < 90000)
                return true;

            Pawn attacker = dinfo.Instigator as Pawn;
            if (attacker == null || attacker.jobs == null)
                return true;

            if (attacker.jobs.curDriver?.job?.def?.defName != "UnnaturalCorpseAttack")
                return true;

            CorpseBloatManager.StartBloat(attacker, victim);
            __result = new DamageWorker.DamageResult();
            return false;
        }
    }

}
