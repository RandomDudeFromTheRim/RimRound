using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimRound.GorgeWorld
{
    /// <summary>
    /// The Gorge World: an Alien Worlds planet type made entirely of meat (1.6/ExternalMods/GorgeWorld,
    /// loaded only with Alien Worlds). Its flesh biomes never grow anywhere else.
    /// </summary>
    public static class GorgeWorldUtility
    {
        public const string PlanetTypeName = "RR_GorgeWorld";

        static PropertyInfo currentPlanetType;
        static bool looked;

        /// <summary>Whether the world being generated or played is the Gorge World (read from Alien Worlds, softly).</summary>
        public static bool Active
        {
            get
            {
                if (!looked)
                {
                    looked = true;
                    currentPlanetType = AccessTools.TypeByName("AlienWorlds.PlanetTypeManager")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                }
                try
                {
                    return currentPlanetType != null && (string)currentPlanetType.GetValue(null) == PlanetTypeName;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static bool IsGorgeBiome(BiomeDef biome) => biome != null && biome.HasModExtension<GorgeBiomeExtension>();
    }

    /// <summary>Marks a Gorge World flesh biome, and says how thickly its growths come up (TileMutatorWorker_GorgeGrowths).</summary>
    public class GorgeBiomeExtension : DefModExtension
    {
        public float mawsPer10k;
        public float spittersPer10k;
        public float geysersPer10k;
    }

    /// <summary>A flesh biome: only ever on the Gorge World, and never under water.</summary>
    public abstract class BiomeWorker_Gorge : BiomeWorker
    {
        public override float GetScore(BiomeDef biome, Tile tile, PlanetTile planetTile)
        {
            if (!GorgeWorldUtility.Active || tile.WaterCovered)
                return -100f;
            return Score(tile);
        }

        protected abstract float Score(Tile tile);
    }

    /// <summary>The planet's commonest flesh: wherever it's wet enough and not a marsh.</summary>
    public class BiomeWorker_Fleshwood : BiomeWorker_Gorge
    {
        protected override float Score(Tile tile) => 12f + Mathf.Clamp(tile.rainfall, 0f, 2400f) / 120f;
    }

    /// <summary>Rendered-down lowlands: where the ground is swampy, the fat pools.</summary>
    public class BiomeWorker_TallowMarsh : BiomeWorker_Gorge
    {
        protected override float Score(Tile tile)
        {
            if (tile.swampiness < 0.25f || tile.rainfall < 600f)
                return 0f;
            return 22f + tile.swampiness * 40f + tile.rainfall / 300f;
        }
    }

    /// <summary>Dry, scarred flesh: where little rain falls, the maws take over.</summary>
    public class BiomeWorker_MawWastes : BiomeWorker_Gorge
    {
        protected override float Score(Tile tile) => 34f - Mathf.Clamp(tile.rainfall, 0f, 2400f) / 40f;
    }

    /// <summary>
    /// The Gorge World's seas and big lakes are blood (Alien Worlds' oceanBiome/lakeBiome). An
    /// ocean worker that stands aside on any other planet, so plain water stays plain water there.
    /// </summary>
    public class BiomeWorker_GorgeSea : BiomeWorker_Ocean
    {
        public override float GetScore(BiomeDef biome, Tile tile, PlanetTile planetTile) =>
            GorgeWorldUtility.Active ? base.GetScore(biome, tile, planetTile) : -100f;
    }

    /// <summary>
    /// Scatters the flesh dimension's growths over a flesh biome's map: gorge maws hidden in
    /// the floor, bloat spitters and bloatgas vents, as thick as its GorgeBiomeExtension says,
    /// and one to three gullets down into its guts (Building_Gullet), well apart.
    /// Kept away from the map edge and the middle, where a colony usually starts.
    /// </summary>
    public class TileMutatorWorker_GorgeGrowths : TileMutatorWorker
    {
        const float KeepClearOfCentre = 22f;
        const float GulletSpacing = 30f;

        public TileMutatorWorker_GorgeGrowths(TileMutatorDef def) : base(def) { }

        public override void GenerateNonCriticalStructures(Map map)
        {
            if (map.Biome.GetModExtension<GorgeBiomeExtension>() is not GorgeBiomeExtension ext)
                return;
            Scatter(map, "RR_Gullet", Rand.RangeInclusive(1, 3), GulletSpacing);
            Scatter(map, "RR_GorgeMaw", GenMath.RoundRandom(ext.mawsPer10k * map.Area / 10000f));
            Scatter(map, "RR_BloatSpitter", GenMath.RoundRandom(ext.spittersPer10k * map.Area / 10000f));
            Scatter(map, "RR_BloatGeyser", GenMath.RoundRandom(ext.geysersPer10k * map.Area / 10000f));
        }

        static void Scatter(Map map, string defName, int count, float spacing = 0f)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
                return;
            var placed = new List<IntVec3>();
            for (int i = 0; i < count; i++)
            {
                if (!CellFinder.TryFindRandomCell(map, c => Fits(def, c, map) && placed.All(p => p.DistanceTo(c) >= spacing), out IntVec3 cell))
                    return;
                GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map);
                placed.Add(cell);
            }
        }

        static bool Fits(ThingDef def, IntVec3 at, Map map)
        {
            if (at.DistanceToEdge(map) < 10 || at.DistanceTo(map.Center) < KeepClearOfCentre)
                return false;
            foreach (IntVec3 c in GenAdj.OccupiedRect(at, Rot4.North, def.size))
                if (!c.InBounds(map) || !c.Standable(map) || c.GetEdifice(map) != null || c.GetTerrain(map).IsWater || c.GetFirstPawn(map) != null)
                    return false;
            return true;
        }
    }

    /// <summary>
    /// Vanilla Genetics Expanded's failed experiments wither and die unless someone tends to
    /// them. On the Gorge World they aren't failures - they're the wildlife - so wild ones
    /// living in a flesh biome don't. Tamed, they need tending again. Patched only if VGE is on.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class GorgeWorldVGECompat
    {
        static GorgeWorldVGECompat()
        {
            MethodInfo tick = AccessTools.TypeByName("GeneticRim.CompDieUnlessReset") is Type t ? AccessTools.Method(t, "CompTick") : null;
            if (tick != null)
                new Harmony("RimRound.GorgeWorld.VGE").Patch(tick, prefix: new HarmonyMethod(typeof(GorgeWorldVGECompat), nameof(Prefix)));
        }

        static bool Prefix(ThingComp __instance) =>
            !(__instance.parent is Pawn p && p.Faction == null && p.Spawned && GorgeWorldUtility.IsGorgeBiome(p.Map.Biome));
    }
}
