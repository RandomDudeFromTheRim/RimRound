using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.GorgeWorld
{
    /// <summary>Gorge World defs, looked up softly: they only load with Alien Worlds.</summary>
    public static class GorgeDefs
    {
        static GameConditionDef air, feedingSeason;
        static HediffDef digestion;

        public static GameConditionDef Air => air ??= DefDatabase<GameConditionDef>.GetNamedSilentFail("RR_GorgeAir");
        public static GameConditionDef FeedingSeason => feedingSeason ??= DefDatabase<GameConditionDef>.GetNamedSilentFail("RR_FeedingSeason");
        public static HediffDef Digestion => digestion ??= DefDatabase<HediffDef>.GetNamedSilentFail("RR_GutDigestion");
    }

    /// <summary>
    /// The air of the Gorge World: warm, wet and red, like being inside something - because you
    /// are. A permanent condition on every flesh-biome map (MapComponent_GorgeWorld) that darkens
    /// the sky to a deep red and hangs a slow red haze over it. Not underground.
    /// </summary>
    public class GameCondition_GorgeAir : GameCondition
    {
        static readonly SkyColorSet Colors = new SkyColorSet(
            new Color(1f, 0.42f, 0.38f), new Color(0.85f, 0.55f, 0.55f), new Color(0.75f, 0.35f, 0.35f), 0.85f);

        readonly List<SkyOverlay> overlays = new List<SkyOverlay> { new WeatherOverlay_GorgeHaze() };

        public override int TransitionTicks => 600;

        public override float SkyTargetLerpFactor(Map map) => GameConditionUtility.LerpInOutValue(this, TransitionTicks, 0.75f);

        public override SkyTarget? SkyTarget(Map map) => new SkyTarget(0.9f, Colors, 1f, 1f);

        public override List<SkyOverlay> SkyOverlays(Map map) => overlays;

        public override void GameConditionTick()
        {
            List<Map> maps = AffectedMaps;
            for (int i = 0; i < overlays.Count; i++)
                for (int j = 0; j < maps.Count; j++)
                    overlays[i].TickOverlay(maps[j], 1f);
        }

        public override void GameConditionDraw(Map map)
        {
            for (int i = 0; i < overlays.Count; i++)
                overlays[i].DrawOverlay(map);
        }
    }

    /// <summary>A slow red haze: vanilla's fog, on a material of its own so it doesn't tint real fog.</summary>
    [StaticConstructorOnStartup]
    public class WeatherOverlay_GorgeHaze : WeatherOverlayDualPanner
    {
        static readonly Material HazeMat = new Material(MatLoader.LoadMat("Weather/FogOverlayWorld"));

        public WeatherOverlay_GorgeHaze()
        {
            worldOverlayMat = HazeMat;
            worldOverlayPanSpeed1 = 0.0003f;
            worldOverlayPanSpeed2 = 0.00022f;
            worldPanDir1 = new Vector2(1f, 0.6f).normalized;
            worldPanDir2 = new Vector2(-0.4f, 1f).normalized;
            ForcedOverlayColor = new Color(0.55f, 0.04f, 0.07f);
        }
    }

    /// <summary>Breathing the Gorge World's air. Worse outside, under its red sky, than under a roof.</summary>
    public class ThoughtWorker_GorgeAir : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!p.Spawned || GorgeDefs.Air == null || !p.Map.gameConditionManager.ConditionIsActive(GorgeDefs.Air))
                return ThoughtState.Inactive;
            return ThoughtState.ActiveAtStage(p.Position.Roofed(p.Map) ? 0 : 1);
        }
    }

    /// <summary>
    /// Keeps the Gorge World's red air over every flesh-biome map (pocket maps and caves aside),
    /// and digests whatever is down in its guts (GutDigestion). On every map, but it only ever
    /// does anything on a flesh biome or in a gut.
    /// </summary>
    public class MapComponent_GorgeWorld : MapComponent
    {
        bool gut;

        public MapComponent_GorgeWorld(Map map) : base(map) { }

        public override void FinalizeInit()
        {
            gut = GutDigestion.IsGut(map);
            EnsureAir();
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % GenTicks.TickLongInterval == 77)
                EnsureAir();
            if (!gut)
                return;
            if (now % GutDigestion.PawnInterval == 31)
                GutDigestion.DigestPawns(map);
            if (now % GutDigestion.ItemInterval == 113)
                GutDigestion.DigestItems(map);
        }

        void EnsureAir()
        {
            if (GorgeDefs.Air == null || map.IsPocketMap || !GorgeWorldUtility.IsGorgeBiome(map.Biome))
                return;
            if (map.gameConditionManager.ConditionIsActive(GorgeDefs.Air))
                return;
            GameCondition air = GameConditionMaker.MakeConditionPermanent(GorgeDefs.Air);
            map.gameConditionManager.RegisterCondition(air);
        }
    }
}
