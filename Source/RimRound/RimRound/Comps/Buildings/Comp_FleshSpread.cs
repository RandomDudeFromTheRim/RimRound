using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Comps
{
    public class CompProperties_FleshSpread : CompProperties
    {
        public float maxRadius = 7.9f;
        /// <summary>Ticks for the flesh to spread from the centre out to maxRadius.</summary>
        public int ticksToFullSpread = 30000;
        public string fleshTerrain = "RR_FleshFloor";

        public CompProperties_FleshSpread()
        {
            compClass = typeof(Comp_FleshSpread);
        }
    }

    /// <summary>
    /// Flesh spreads across the ground around the void seam while it is open, and
    /// withers back to exactly the ground it covered once the seam is sealed.
    /// Water and anything the flesh can't hold (bridges, for one) is left alone.
    /// </summary>
    public class Comp_FleshSpread : ThingComp
    {
        const int SpreadIntervalTicks = 250;

        public CompProperties_FleshSpread Props => (CompProperties_FleshSpread)props;

        int spawnTick = -1;
        Dictionary<IntVec3, TerrainDef> covered = new Dictionary<IntVec3, TerrainDef>();
        List<IntVec3> tmpCells;
        List<TerrainDef> tmpTerrains;

        TerrainDef Flesh => DefDatabase<TerrainDef>.GetNamed(Props.fleshTerrain);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref spawnTick, "spawnTick", -1);
            Scribe_Collections.Look(ref covered, "coveredTerrain", LookMode.Value, LookMode.Def, ref tmpCells, ref tmpTerrains);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && covered == null)
                covered = new Dictionary<IntVec3, TerrainDef>();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (spawnTick < 0)
                spawnTick = Find.TickManager.TicksGame;
        }

        float CurrentRadius => Props.maxRadius * Mathf.Clamp01((float)(Find.TickManager.TicksGame - spawnTick) / Props.ticksToFullSpread);

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || !parent.IsHashIntervalTick(SpreadIntervalTicks))
                return;

            Map map = parent.Map;
            TerrainDef flesh = Flesh;
            float radius = CurrentRadius;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, radius, useCenter: true))
            {
                if (!cell.InBounds(map) || covered.ContainsKey(cell))
                    continue;

                TerrainDef under = map.terrainGrid.TerrainAt(cell);
                if (under == flesh || under.IsWater || under.bridge || cell.GetEdifice(map) is Building b && b.def.building.isNaturalRock)
                    continue;

                // ragged edge: the outer ring fills in patchily
                if (cell.DistanceTo(parent.Position) > radius - 1.5f && !Rand.Chance(0.35f))
                    continue;

                covered[cell] = under;
                map.terrainGrid.SetTerrain(cell, flesh);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            Withers(map);
        }

        /// <summary>Hands every covered cell back its original ground, leaving a little rotten flesh behind.</summary>
        void Withers(Map map)
        {
            if (map == null || covered.Count == 0)
                return;

            TerrainDef flesh = Flesh;
            foreach (KeyValuePair<IntVec3, TerrainDef> kv in covered.ToList())
            {
                if (!kv.Key.InBounds(map) || map.terrainGrid.TerrainAt(kv.Key) != flesh)
                    continue; // built over since: leave the player's floor alone
                map.terrainGrid.SetTerrain(kv.Key, kv.Value);
                if (Rand.Chance(0.15f))
                    FilthMaker.TryMakeFilth(kv.Key, map, ThingDefOf.Filth_Blood);
            }
            covered.Clear();
        }

        public override string CompInspectStringExtra()
        {
            return covered.Count > 0 ? $"Flesh has spread across {covered.Count} cells around it." : null;
        }
    }
}
