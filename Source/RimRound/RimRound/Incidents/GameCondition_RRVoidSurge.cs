using System.Collections.Generic;
using RimRound.Comps;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Incidents
{
    /// <summary>
    /// Void surge: something vast beneath the crust has overeaten and is belching it
    /// back up. For the duration a sweet pink haze drifts down over the whole map, and
    /// anyone out of doors builds up void exposure (RR_VoidExposure) - like toxic
    /// buildup under fallout - which fattens them gradually and fades once they're
    /// under a roof. RimRound's counterpart to toxic fallout.
    /// </summary>
    public class GameCondition_RRVoidSurge : GameCondition
    {
        const int PawnIntervalTicks = 250;
        /// <summary>Exposure per check outdoors: about eight hours outside to saturate.</summary>
        const float ExposurePerCheck = 0.012f;
        const int HazeIntervalTicks = 45;
        const int HazePuffsPerPulse = 6;

        static readonly Color HazeColor = new Color(1f, 0.62f, 0.82f, 0.9f);

        static readonly SkyColorSet SurgeColors = new SkyColorSet(
            new ColorInt(255, 128, 196).ToColor,
            new ColorInt(236, 190, 232).ToColor,
            new Color(0.95f, 0.58f, 0.76f),
            0.85f);

        readonly List<SkyOverlay> overlays = new List<SkyOverlay> { new WeatherOverlay_RRVoidSurge() };

        static HediffDef exposure;
        static HediffDef Exposure => exposure ?? (exposure = DefDatabase<HediffDef>.GetNamedSilentFail("RR_VoidExposure"));

        public override int TransitionTicks => 3000;

        public override void GameConditionTick()
        {
            List<Map> maps = AffectedMaps;
            for (int i = 0; i < overlays.Count; i++)
                for (int j = 0; j < maps.Count; j++)
                    overlays[i].TickOverlay(maps[j], 1f);

            int tick = Find.TickManager.TicksGame;
            bool dose = tick % PawnIntervalTicks == 0;
            bool haze = tick % HazeIntervalTicks == 0;
            if (!dose && !haze)
                return;
            foreach (Map map in maps)
            {
                if (haze && map == Find.CurrentMap)
                    Haze(map);
                if (dose)
                    Expose(map);
            }
        }

        /// <summary>Pink puffs welling up out of the open ground: only for the look of it.</summary>
        static void Haze(Map map)
        {
            CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(2).ClipInsideMap(map);
            for (int i = 0; i < HazePuffsPerPulse; i++)
            {
                IntVec3 c = view.RandomCell;
                if (c.InBounds(map) && !c.Roofed(map) && c.Walkable(map))
                    FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, Rand.Range(1.2f, 2.2f), HazeColor);
            }
        }

        /// <summary>Anyone outdoors is breathing it.</summary>
        static void Expose(Map map)
        {
            HediffDef def = Exposure;
            if (def == null)
                return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.kindDef.immuneToGameConditionEffects || p.Position.Roofed(map))
                    continue;
                if (p.TryGetComp<FullnessAndDietStats_ThingComp>() is FullnessAndDietStats_ThingComp fnd && !fnd.Disabled)
                    HealthUtility.AdjustSeverity(p, def, ExposurePerCheck);
            }
        }

        public override void GameConditionDraw(Map map)
        {
            for (int i = 0; i < overlays.Count; i++)
                overlays[i].DrawOverlay(map);
        }

        public override List<SkyOverlay> SkyOverlays(Map map) => overlays;

        public override float SkyTargetLerpFactor(Map map) => GameConditionUtility.LerpInOutValue(this, TransitionTicks, 0.6f);

        public override SkyTarget? SkyTarget(Map map) => new SkyTarget(0.85f, SurgeColors, 1f, 1f);

        public override bool AllowEnjoyableOutsideNow(Map map) => false;
    }

    /// <summary>A slow pink drift over the whole map, the way fallout drifts down.</summary>
    [StaticConstructorOnStartup]
    public class WeatherOverlay_RRVoidSurge : WeatherOverlayDualPanner
    {
        static readonly Material SurgeOverlayWorld;

        static WeatherOverlay_RRVoidSurge()
        {
            SurgeOverlayWorld = new Material(MatLoader.LoadMat("Weather/SnowOverlayWorld"));
            SurgeOverlayWorld.color = new Color(1f, 0.55f, 0.8f);
        }

        public WeatherOverlay_RRVoidSurge()
        {
            worldOverlayMat = SurgeOverlayWorld;
            worldOverlayPanSpeed1 = 0.0006f;
            worldPanDir1 = new Vector2(-0.2f, -1f).normalized;
            worldOverlayPanSpeed2 = 0.0009f;
            worldPanDir2 = new Vector2(-0.18f, -1f).normalized;
        }
    }

    /// <summary>
    /// Fattens by how saturated the pawn is: up to kilosPerHourAtFull at full exposure,
    /// a trickle at low exposure.
    /// </summary>
    public class HediffCompProperties_RRWeightPerHour : HediffCompProperties
    {
        public float kilosPerHourAtFull = 2.5f;

        public HediffCompProperties_RRWeightPerHour()
        {
            compClass = typeof(HediffComp_RRWeightPerHour);
        }
    }

    public class HediffComp_RRWeightPerHour : HediffComp
    {
        HediffCompProperties_RRWeightPerHour Props => (HediffCompProperties_RRWeightPerHour)props;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            if (Pawn.IsHashIntervalTick(GenDate.TicksPerHour, delta))
                Utilities.HediffUtility.QueueWeightGain(Pawn, Props.kilosPerHourAtFull * parent.Severity);
        }
    }
}
