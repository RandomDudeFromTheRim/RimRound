using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimRound.Hediffs
{
    public class Hediff_MeldAerosol : Hediff
    {
        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead)
                return;

            if (!pawn.IsHashIntervalTick(60))
                return;

            DecaySeverity();

            if (Severity > 0f)
            {
                AddMeldGrowth();
                AddDirectWeightGain();
            }

            if (Severity >= 1f)
                TriggerMeldDetonation();
        }

        void DecaySeverity()
        {
            Severity -= 0.0005f;
            if (Severity < 0f)
                Severity = 0f;
        }

        void AddMeldGrowth()
        {
            float meldAmount = 0.02f + Severity * 0.05f;
            Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth);
            if (existing is Hediff_MeldGrowth meldGrowth)
            {
                meldGrowth.AddContribution(meldAmount, 1.0f);
            }
            else
            {
                Hediff_MeldGrowth meld = (Hediff_MeldGrowth)HediffMaker.MakeHediff(Defs.HediffDefOf.RR_MeldGrowth, pawn);
                meld.AddContribution(meldAmount, 1.0f);
                pawn.health.AddHediff(meld);
            }
        }

        void AddDirectWeightGain()
        {
            var fnd = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fnd == null || fnd.Disabled)
                return;

            float kilos = 0.02f + Severity * 0.1f;
            fnd.activeWeightGainRequests.Enqueue(
                new WeightGainRequest(kilos, Find.TickManager.TicksGame + 5, 0, false));
        }

        void TriggerMeldDetonation()
        {
            if (pawn == null || pawn.Dead || pawn.Map == null)
                return;

            Map map = pawn.Map;
            IntVec3 pos = pawn.Position;

            float weightSev = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight)?.Severity ?? 0.035f;
            float meldSev = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth)?.Severity ?? 0f;
            float extraKilos = (weightSev + meldSev) / 0.001f;

            int blobWallCount = 1 + (int)(extraKilos / 20f);
            blobWallCount = Mathf.Clamp(blobWallCount, 3, 80);
            float blobRadius = 1.5f + extraKilos * 0.004f;

            Messages.Message(
                $"{pawn.LabelShort}'s body swells and bursts, releasing a torrent of fleshmass that solidifies into blob walls!",
                new LookTargets(pawn),
                MessageTypeDefOf.ThreatBig);

            pawn.Kill(null);

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

            int gluttoniumCount = Rand.RangeInclusive(3, 8) + (int)(extraKilos * 0.005f);
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

        public override string TipStringExtra
        {
            get
            {
                float weightSev = pawn?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight)?.Severity ?? 0.035f;
                float meldSev = pawn?.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth)?.Severity ?? 0f;
                float ek = (weightSev + meldSev) / 0.001f;
                int blobEstimate = 1 + (int)(ek / 20f);
                blobEstimate = Mathf.Clamp(blobEstimate, 3, 80);

                return $"Meld aerosol infection: {Severity:P1}\nDetonation: ~{blobEstimate} blob walls\nHeavier victims produce more walls";
            }
        }
    }
}
