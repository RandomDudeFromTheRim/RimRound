using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimRound.Patch
{
    public static class CorpseBloatManager
    {
        private struct BloatState
        {
            public Pawn attacker;
            public Pawn victim;
            public int startTick;
        }

        private static List<BloatState> activeBloats = new List<BloatState>();

        public static void StartBloat(Pawn attacker, Pawn victim)
        {
            if (Find.TickManager == null) return;

            for (int i = 0; i < activeBloats.Count; i++)
                if (activeBloats[i].attacker == attacker)
                    return;

            activeBloats.Add(new BloatState
            {
                attacker = attacker,
                victim = victim,
                startTick = Find.TickManager.TicksGame
            });

            if (victim?.stances?.stunner != null)
                victim.stances.stunner.StunFor(900, attacker, addBattleLog: false);

            Messages.Message(
                $"{attacker.LabelShort} swells rapidly, its distended form pulsing with dark energy!",
                attacker,
                MessageTypeDefOf.ThreatBig);
        }

        public static void Tick()
        {
            if (activeBloats.Count == 0) return;
            if (Find.TickManager == null) return;

            int curTick = Find.TickManager.TicksGame;
            int swellDuration = 600;

            for (int i = activeBloats.Count - 1; i >= 0; i--)
            {
                var state = activeBloats[i];
                int elapsed = curTick - state.startTick;

                if (state.attacker == null || state.attacker.Dead)
                {
                    activeBloats.RemoveAt(i);
                    continue;
                }

                if (elapsed < swellDuration)
                {
                    if (elapsed % 10 == 0 && elapsed > 0)
                    {
                        var attacker = state.attacker;

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
                }
                else
                {
                    activeBloats.RemoveAt(i);
                    Explode(state);
                }
            }
        }

        static void Explode(BloatState state)
        {
            Map map = state.attacker?.Map;
            IntVec3 pos = state.attacker?.Position ?? IntVec3.Invalid;
            if (map == null || !pos.IsValid) return;

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
        static bool Prefix(Thing __instance, DamageInfo dinfo)
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
            return false;
        }
    }

    [HarmonyPatch(typeof(Root_Play))]
    [HarmonyPatch(nameof(Root_Play.Update))]
    public class CorpseBlast_GlobalTick
    {
        static void Postfix()
        {
            CorpseBloatManager.Tick();
        }
    }
}
