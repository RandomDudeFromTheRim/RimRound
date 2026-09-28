using System.Collections.Generic;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Incidents
{
    /// <summary>
    /// Void surge: something vast beneath the crust has overeaten and is belching
    /// it back up. For the duration, fattening gas seeps up everywhere out of doors:
    /// a pink haze drifts over the map, and anyone caught outside keeps breathing it
    /// in. Roofs keep it out. RimRound's counterpart to toxic fallout.
    /// </summary>
    public class GameCondition_RRVoidSurge : GameCondition
    {
        const int PawnIntervalTicks = 250;
        const int PawnDose = 45;
        const int HazeIntervalTicks = 60;
        const int HazeCellsPerPulse = 8;
        const int HazePerCell = 40;

        static readonly SkyColorSet SurgeColors = new SkyColorSet(
            new ColorInt(255, 176, 206).ToColor,
            new ColorInt(236, 206, 232).ToColor,
            new Color(0.9f, 0.62f, 0.74f),
            0.85f);

        public override int TransitionTicks => 3000;

        public override void GameConditionTick()
        {
            int tick = Find.TickManager.TicksGame;
            bool dose = tick % PawnIntervalTicks == 0;
            bool haze = tick % HazeIntervalTicks == 0;
            if (!dose && !haze)
                return;

            foreach (Map map in AffectedMaps)
            {
                MapComp_RRGasGrid grid = map.GetComponent<MapComp_RRGasGrid>();
                if (grid == null)
                    continue;
                if (haze)
                    Haze(map, grid);
                if (dose)
                    DosePawns(map, grid);
            }
        }

        /// <summary>A few puffs welling up out of the open ground, for the look of it.</summary>
        static void Haze(Map map, MapComp_RRGasGrid grid)
        {
            for (int i = 0; i < HazeCellsPerPulse; i++)
            {
                IntVec3 c = CellFinder.RandomCell(map);
                if (!c.Roofed(map) && grid.GasCanMoveTo(c))
                    grid.AddGas(c, RRGasType.fatteningGas, HazePerCell);
            }
        }

        /// <summary>Anyone outdoors is standing in it.</summary>
        static void DosePawns(Map map, MapComp_RRGasGrid grid)
        {
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.kindDef.immuneToGameConditionEffects || p.Position.Roofed(map))
                    continue;
                if (p.TryGetComp<FullnessAndDietStats_ThingComp>() is FullnessAndDietStats_ThingComp fnd && !fnd.Disabled)
                    grid.AddGas(p.Position, RRGasType.fatteningGas, PawnDose);
            }
        }

        public override float SkyTargetLerpFactor(Map map) => GameConditionUtility.LerpInOutValue(this, TransitionTicks, 0.5f);

        public override SkyTarget? SkyTarget(Map map) => new SkyTarget(0.85f, SurgeColors, 1f, 1f);

        public override bool AllowEnjoyableOutsideNow(Map map) => false;
    }
}
