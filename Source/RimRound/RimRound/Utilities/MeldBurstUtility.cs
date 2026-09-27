using RimRound.Comps;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    public static class MeldBurstUtility
    {
        /// <summary>
        /// Starts the burst: a few seconds of swelling, trembling and gurgling
        /// (Hediff_RRBursting), then Burst. Does nothing if it has already begun.
        /// </summary>
        public static void BeginBurst(Pawn pawn, string message = null, int bonusGluttonium = 0)
        {
            if (pawn == null || pawn.Dead || pawn.health.hediffSet.HasHediff(Defs.HediffDefOf.RR_Bursting))
                return;
            var h = (Hediffs.Hediff_RRBursting)HediffMaker.MakeHediff(Defs.HediffDefOf.RR_Bursting, pawn);
            h.message = message;
            h.bonusGluttonium = bonusGluttonium;
            pawn.health.AddHediff(h);
        }

        /// <summary>
        /// The pawn swells past bearing and bursts: it dies, its body gone, into a spray of
        /// blob walls and void gluttonium (more for heavier pawns). Witnesses react by their
        /// weight opinion. Used by meld aerosol and by the gorge constrictor.
        /// </summary>
        /// <param name="message">Overrides the default "body swells and bursts" message.</param>
        /// <param name="bonusGluttonium">Extra void gluttonium dropped on top of the usual yield.</param>
        public static void Burst(Pawn pawn, string message = null, int bonusGluttonium = 0)
        {
            if (pawn == null || pawn.Dead || pawn.Map == null)
                return;

            Map map = pawn.Map;
            IntVec3 pos = pawn.Position;

            float weightSev = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight)?.Severity ?? 0.035f;
            float meldSev = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth)?.Severity ?? 0f;
            float extraKilos = (weightSev / 0.001f) + (meldSev * 10f);

            int blobWallCount = 1 + (int)(extraKilos / 20f);
            blobWallCount = Mathf.Clamp(blobWallCount, 3, 80);
            float blobRadius = 1.5f + extraKilos * 0.004f;

            Messages.Message(
                message ?? $"{pawn.LabelShort}'s body swells and bursts, releasing a torrent of fleshmass that solidifies into blob walls!",
                new LookTargets(pawn),
                MessageTypeDefOf.ThreatBig);

            // a wet, fleshy burst, like a fleshmass heart dying: meat spray, flying
            // chunks, blood everywhere - not a smoke puff
            if (ModsConfig.AnomalyActive)
            {
                DefDatabase<EffecterDef>.GetNamedSilentFail("FleshmassHeartDestroyed")?.Spawn(pos, map).Cleanup();
                FleshbeastUtility.MeatSplatter(Rand.RangeInclusive(6, 10), pos, map, FleshbeastUtility.MeatExplosionSize.Large);
            }
            else
            {
                GenExplosion.DoExplosion(pos, map, 2.9f, DamageDefOf.Smoke, null);
            }
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pos, 2.4f, useCenter: true))
                if (cell.InBounds(map) && cell.Walkable(map) && Rand.Chance(0.55f))
                    FilthMaker.TryMakeFilth(cell, map, ThingDefOf.Filth_Blood);
            if (map == Find.CurrentMap)
                Find.CameraDriver.shaker.DoShake(1f);

            pawn.Kill(null);
            pawn.Corpse?.Destroy();

            int spawned = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pos, blobRadius, useCenter: true))
            {
                if (spawned >= blobWallCount)
                    break;

                if (!cell.InBounds(map) || !cell.Walkable(map))
                    continue;

                if (!Rand.Chance(0.65f))
                    continue;

                Thing wall = ThingMaker.MakeThing(ThingDef.Named("RR_BlobWall"));
                GenSpawn.Spawn(wall, cell, map, Rot4.North, WipeMode.Vanish);
                spawned++;
            }

            int gluttoniumCount = Rand.RangeInclusive(3, 8) + Mathf.Min((int)(extraKilos * 0.005f), 12) + bonusGluttonium;
            IntVec3[] offsets = {
                new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0),
                new IntVec3(0, 0, 1), new IntVec3(0, 0, -1)
            };
            foreach (IntVec3 offset in offsets)
            {
                IntVec3 cell = pos + offset;
                if (cell.InBounds(map) && cell.Walkable(map))
                {
                    int dropCount = gluttoniumCount;
                    if (dropCount > 8) dropCount = 8;
                    if (dropCount > 0)
                    {
                        Thing ore = ThingMaker.MakeThing(ThingDef.Named("RR_VoidGluttonium"));
                        ore.stackCount = dropCount;
                        gluttoniumCount -= dropCount;
                        GenPlace.TryPlaceThing(ore, cell, map, ThingPlaceMode.Near);
                    }
                }
            }
            if (gluttoniumCount > 0)
            {
                // more than the four neighbouring cells took (bonus yield): heap the rest nearby
                Thing rest = ThingMaker.MakeThing(ThingDef.Named("RR_VoidGluttonium"));
                rest.stackCount = gluttoniumCount;
                GenPlace.TryPlaceThing(rest, pos, map, ThingPlaceMode.Near);
            }

            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                float dist = (p.Position - pos).LengthHorizontal;
                if (dist > 30f) continue;
                ThingComp_PawnAttitude witnessAtt = p.TryGetComp<ThingComp_PawnAttitude>();
                if (witnessAtt == null) continue;
                if (witnessAtt.weightOpinion >= WeightOpinion.NeutralPlus)
                    p.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDef.Named("RR_WitnessedBloatedDeath_Aroused"));
                else
                    p.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDef.Named("RR_WitnessedBloatedDeath_Horror"));
            }
        }
    }
}
