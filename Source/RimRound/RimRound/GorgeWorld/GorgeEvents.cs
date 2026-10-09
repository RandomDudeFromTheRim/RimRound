using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimRound.GorgeWorld
{
    /// <summary>
    /// The Gorge World's body rhythms (fever, chills, feeding season, peristalsis): on its flesh
    /// biomes always, and on the other biomes their incident lists (the overgrown broodmother)
    /// only while the world is the Gorge World - she sits on other planets too.
    /// </summary>
    public class IncidentWorker_GorgeCondition : IncidentWorker_MakeGameCondition
    {
        protected override bool CanFireNowSub(IncidentParms parms) =>
            base.CanFireNowSub(parms) && parms.target is Map map
            && (GorgeWorldUtility.IsGorgeBiome(map.Biome) || GorgeWorldUtility.Active);
    }

    /// <summary>
    /// Feeding season: the Gorge World is being fed, and everything on it gorges. Plants grow three
    /// times as fast, bloatgas geysers fill three times as fast, a few animals wander in for the
    /// glut, and the air is so thick with nourishment that anyone out under the sky barely gets hungry
    /// and slowly fills out.
    /// </summary>
    public class GameCondition_FeedingSeason : GameCondition
    {
        const int PawnInterval = 250;
        /// <summary>Food and kilos per PawnInterval for a pawn out under the sky.</summary>
        const float FoodPerPulse = 0.008f;
        const float KilosPerPulse = 0.06f;
        /// <summary>Extra growth, in multiples of a plant's own growth rate. Cells get visited less often than
        /// TicksBetweenCellVisits suggests: measured in game, this comes out at about three times the usual growth.</summary>
        const float ExtraGrowth = 3f;
        /// <summary>How often the steady-effects pass visits each cell (SteadyEnvironmentEffects does 0.06% of the map a tick).</summary>
        const float TicksBetweenCellVisits = 1f / 0.0006f;
        public const float GeyserPressureFactor = 3f;

        public override void Init()
        {
            base.Init();
            if (SingleMap != null)
                CallInAnimals(SingleMap);
        }

        /// <summary>How much faster a geyser on this map fills: faster in feeding season.</summary>
        public static float PressureFactor(Map map) =>
            GorgeDefs.FeedingSeason != null && map.gameConditionManager.ConditionIsActive(GorgeDefs.FeedingSeason) ? GeyserPressureFactor : 1f;

        static void CallInAnimals(Map map)
        {
            List<PawnKindDef> kinds = map.Biome.AllWildAnimals.Where(k => map.Biome.CommonalityOfAnimal(k) > 0f).ToList();
            if (kinds.Count == 0)
                return;
            int count = Rand.RangeInclusive(4, 8);
            for (int i = 0; i < count; i++)
            {
                if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 cell, map, CellFinder.EdgeRoadChance_Animal))
                    return;
                PawnKindDef kind = kinds.RandomElementByWeight(k => map.Biome.CommonalityOfAnimal(k));
                Pawn animal = PawnGenerator.GeneratePawn(kind);
                GenSpawn.Spawn(animal, CellFinder.RandomClosewalkCellNear(cell, map, 6), map);
            }
        }

        public override void DoCellSteadyEffects(IntVec3 c, Map map)
        {
            if (c.GetPlant(map) is Plant plant && plant.LifeStage == PlantLifeStage.Growing)
            {
                float before = plant.Growth;
                plant.Growth += ExtraGrowth * TicksBetweenCellVisits / (GenDate.TicksPerDay * plant.def.plant.growDays) * plant.GrowthRate;
                if ((int)(before * 10f) != (int)(plant.Growth * 10f))
                    map.mapDrawer.MapMeshDirty(c, MapMeshFlagDefOf.Things);
            }
        }

        public override void GameConditionTick()
        {
            if (Find.TickManager.TicksGame % PawnInterval != 0)
                return;
            foreach (Map map in AffectedMaps)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p.Dead || !p.RaceProps.IsFlesh || p.Position.Roofed(map))
                        continue;
                    if (p.needs?.food is Need_Food food)
                        food.CurLevel = Mathf.Min(food.MaxLevel, food.CurLevel + FoodPerPulse);
                    if (p.RaceProps.Humanlike)
                        Utilities.HediffUtility.QueueWeightGain(p, KilosPerPulse);
                }
            }
        }
    }

    /// <summary>
    /// Peristalsis: slow waves of muscle roll through the ground. Every so often the whole map
    /// heaves - the camera shakes, anyone on their feet staggers, and the flesh weeps a little
    /// blood. It doesn't break anything; it's just the planet swallowing.
    /// </summary>
    public class GameCondition_Peristalsis : GameCondition
    {
        static readonly IntRange TicksBetweenWaves = new IntRange(900, 2100);
        const int StaggerTicks = 150;

        int nextWaveTick = -1;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextWaveTick, "nextWaveTick", -1);
        }

        public override void GameConditionTick()
        {
            int now = Find.TickManager.TicksGame;
            if (nextWaveTick < 0)
                nextWaveTick = now + TicksBetweenWaves.RandomInRange / 3;
            if (now < nextWaveTick)
                return;
            nextWaveTick = now + TicksBetweenWaves.RandomInRange;
            foreach (Map map in AffectedMaps)
                Heave(map);
        }

        static void Heave(Map map)
        {
            if (Find.CurrentMap == map)
            {
                Find.CameraDriver.shaker.DoShake(1.6f);
                DefDatabase<SoundDef>.GetNamedSilentFail("RR_StomachGurgles_Heavy")?.PlayOneShotOnCamera(map);
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (!p.Dead && !p.Downed && !p.Flying && p.stances != null)
                    p.stances.stagger.StaggerFor(StaggerTicks);
            }
            int filth = Mathf.CeilToInt(map.Area / 4000f);
            for (int i = 0; i < filth; i++)
            {
                IntVec3 c = CellFinder.RandomCell(map);
                if (!c.Roofed(map) && c.Standable(map))
                    FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Blood, Rand.RangeInclusive(1, 3));
            }
        }
    }
}
