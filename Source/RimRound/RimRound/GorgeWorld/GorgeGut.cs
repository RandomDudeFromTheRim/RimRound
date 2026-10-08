using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace RimRound.GorgeWorld
{
    /// <summary>
    /// A gullet: a throat in the Gorge World's flesh, leading down into one of its guts. One to
    /// three on every flesh-biome map (TileMutatorWorker_GorgeGrowths). The gut below is
    /// generated when someone first climbs in; once nobody of yours has been down there for a
    /// couple of days, it churns everything inside away and the gullet clenches shut while it
    /// digests. Some ten to twenty days later it opens again on a fresh gut.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Building_Gullet : MapPortal
    {
        const int CheckInterval = 250;
        const int ChurnAfterTicks = 2 * GenDate.TicksPerDay;
        static readonly IntRange ClosedTicksRange = new IntRange(10 * GenDate.TicksPerDay, 20 * GenDate.TicksPerDay);

        int lastOccupiedTick = -1;
        int closedUntilTick = -1;
        Graphic closedGraphic;

        public bool Closed => Find.TickManager.TicksGame < closedUntilTick;

        public override bool AutoDraftOnEnter => true;

        public override Graphic Graphic
        {
            get
            {
                if (!Closed)
                    return base.Graphic;
                return closedGraphic ??= GraphicDatabase.Get<Graphic_Single>(def.graphicData.texPath + "_Closed", ShaderDatabase.Cutout, def.graphicData.drawSize, Color.white);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastOccupiedTick, "lastOccupiedTick", -1);
            Scribe_Values.Look(ref closedUntilTick, "closedUntilTick", -1);
        }

        protected override void Tick()
        {
            base.Tick();
            if (!this.IsHashIntervalTick(CheckInterval))
                return;
            int now = Find.TickManager.TicksGame;
            if (closedUntilTick >= 0 && !Closed)
            {
                // it has finished digesting and opens up again
                closedUntilTick = -1;
                DirtyMapMesh(Map);
            }
            Map gut = PocketMap;
            if (gut == null)
                return;
            if (lastOccupiedTick < 0 || gut.mapPawns.AllPawnsSpawned.Any(p => p.Faction == Faction.OfPlayer || p.HostFaction == Faction.OfPlayer))
                lastOccupiedTick = now;
            else if (now - lastOccupiedTick > ChurnAfterTicks)
                Churn();
        }

        /// <summary>The gut churns over: whatever is left down there is gone, and the gullet clenches shut while it digests.</summary>
        void Churn()
        {
            if (PocketMapExists)
                PocketMapUtility.DestroyPocketMap(PocketMap);
            pocketMap = null;
            exit = null;
            lastOccupiedTick = -1;
            closedUntilTick = Find.TickManager.TicksGame + ClosedTicksRange.RandomInRange;
            DirtyMapMesh(Map);
            Messages.Message("A gullet has clenched shut. The gut beneath it is digesting whatever was left inside; it will open again on an empty gut.",
                this, MessageTypeDefOf.NeutralEvent);
        }

        public override bool IsEnterable(out string reason)
        {
            if (Closed)
            {
                reason = "Clenched shut while the gut digests (opens in " + (closedUntilTick - Find.TickManager.TicksGame).ToStringTicksToPeriod() + ").";
                return false;
            }
            return base.IsEnterable(out reason);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string extra = null;
            if (Closed)
                extra = "Clenched shut, digesting. Opens in " + (closedUntilTick - Find.TickManager.TicksGame).ToStringTicksToPeriod() + ".";
            else if (PocketMapExists && lastOccupiedTick >= 0 && Find.TickManager.TicksGame - lastOccupiedTick > CheckInterval * 2)
                extra = "Nobody inside. The gut will churn over in " + (lastOccupiedTick + ChurnAfterTicks - Find.TickManager.TicksGame).ToStringTicksToPeriod() + ".";
            if (extra == null)
                return s;
            return s.NullOrEmpty() ? extra : s + "\n" + extra;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            if (!DebugSettings.ShowDevGizmos)
                yield break;
            if (!Closed)
                yield return new Command_Action { defaultLabel = "DEV: Churn gut", action = Churn };
            else
                yield return new Command_Action { defaultLabel = "DEV: Open gullet", action = () => { closedUntilTick = 0; DirtyMapMesh(Map); } };
        }
    }

    /// <summary>
    /// A gut, under a gullet: thick muscle walls (no drop pods, no shuttles) around a great
    /// stomach half-full of bile, with a few side pouches off it. The pouches hold what the
    /// planet swallowed and hasn't finished with - the half-digested belongings and bodies of
    /// people who came before - and the stomach is littered with slag from swallowed ships.
    /// </summary>
    public class GenStep_GorgeGut : GenStep
    {
        public override int SeedPart => 0x6E7C47;

        public override void Generate(Map map, GenStepParams parms)
        {
            ThingDef wall = DefDatabase<ThingDef>.GetNamed("RR_GutWall");
            TerrainDef flesh = DefDatabase<TerrainDef>.GetNamed("RR_GorgeFlesh");
            TerrainDef veined = DefDatabase<TerrainDef>.GetNamed("RR_FleshVeined");
            TerrainDef bileShallow = DefDatabase<TerrainDef>.GetNamed("RR_BileShallow");
            TerrainDef bileDeep = DefDatabase<TerrainDef>.GetNamed("RR_BileDeep");

            var open = new HashSet<IntVec3>();
            var chamber = new HashSet<IntVec3>();
            IntVec3 centre = map.Center;
            float reach = Mathf.Min(map.Size.x, map.Size.z) / 2f;
            IntVec3 At(float angle, float radius) =>
                (centre.ToVector3Shifted() + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * radius).ToIntVec3().ClampInsideMap(map);

            float a0 = Rand.Range(0f, 360f);
            IntVec3 entry = At(a0, reach - 8f);
            float stomachRadius = Rand.Range(11f, 14f);
            var pouches = new List<IntVec3>();
            int pouchCount = Rand.RangeInclusive(3, 4);
            for (int i = 0; i < pouchCount; i++)
                pouches.Add(At(a0 + (i + 1) * 360f / (pouchCount + 1) + Rand.Range(-20f, 20f), reach * Rand.Range(0.6f, 0.72f)));

            var noise = new Perlin(0.06, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
            Carve(map, entry, At(a0 + Rand.Range(-30f, 30f), reach * 0.45f), 1.6f, noise, open);
            Carve(map, At(a0, reach * 0.45f), centre, 1.8f, noise, open);
            foreach (IntVec3 p in pouches)
                Carve(map, centre, p, Rand.Range(1.2f, 1.7f), noise, open);
            Dig(map, entry, 4f, open, chamber);
            Dig(map, centre, stomachRadius, open, chamber);
            var pouchRadii = new List<float>();
            foreach (IntVec3 p in pouches)
            {
                float r = Rand.Range(4.5f, 6.5f);
                pouchRadii.Add(r);
                Dig(map, p, r, open, chamber);
            }

            var bileNoise = new Perlin(0.12, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
            foreach (IntVec3 c in map.AllCells)
            {
                map.roofGrid.SetRoof(c, RoofDefOf.RoofRockThick);
                TerrainDef floor = chamber.Contains(c) ? veined : flesh;
                // the stomach's bile: a deep pool with a shallow, uneven edge
                float r = c.DistanceTo(centre) / stomachRadius + (float)bileNoise.GetValue(c.x, 0, c.z) * 0.15f;
                if (r < 0.32f)
                    floor = bileDeep;
                else if (r < 0.55f)
                    floor = bileShallow;
                map.terrainGrid.SetTerrain(c, floor);
                if (!open.Contains(c))
                    GenSpawn.Spawn(wall, c, map);
            }

            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.CaveExit), entry, map);
            MapGenerator.PlayerStartSpot = entry;

            for (int i = 0; i < pouches.Count; i++)
                FillPouch(map, pouches[i], pouchRadii[i], bileShallow);
            ScatterSlag(map, centre, stomachRadius);
            SpawnDwellers(map, chamber.ToList());
        }

        static void FillPouch(Map map, IntVec3 at, float radius, TerrainDef bile)
        {
            // a little bile pooled in the bottom of it
            IntVec3 puddle = at + new IntVec3(Rand.RangeInclusive(-2, 2), 0, Rand.RangeInclusive(-2, 2));
            foreach (IntVec3 c in GenRadial.RadialCellsAround(puddle, Rand.Range(1.2f, 2.2f), true))
                if (c.InBounds(map) && c.GetEdifice(map) == null)
                    map.terrainGrid.SetTerrain(c, bile);

            // what's left of someone's belongings, half-digested
            ThingSetMakerDef maker = ThingSetMakerDefOf.MapGen_AbandonedColonyStockpile ?? ThingSetMakerDefOf.Reward_ItemsStandard;
            var parms = new ThingSetMakerParams { totalMarketValueRange = new FloatRange(250f, 600f) };
            foreach (Thing t in maker.root.Generate(parms))
                Place(map, t, at, radius, damage: true);

            // and of them
            int bodies = Rand.RangeInclusive(0, 2);
            for (int i = 0; i < bodies; i++)
                PlaceBody(map, at, radius);
        }

        static void PlaceBody(Map map, IntVec3 at, float radius)
        {
            string[] kinds = { "Drifter", "Villager", "Mercenary_Gunner", "Mercenary_Slasher", "Tribal_Warrior", "SpaceRefugee" };
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kinds.RandomElement());
            if (kind == null || !TryCell(map, at, radius, out IntVec3 cell))
                return;
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, null, forceGenerateNewPawn: true));
            GenSpawn.Spawn(pawn, cell, map);
            pawn.Kill(null);
            if (pawn.Corpse is Corpse corpse && corpse.TryGetComp<CompRottable>() is CompRottable rot)
                rot.RotProgress = Rand.Range(0.6f, 1.2f) * rot.PropsRot.TicksToDessicated;
            foreach (Thing worn in pawn.apparel?.WornApparel.Concat<Thing>(pawn.equipment?.AllEquipmentListForReading ?? Enumerable.Empty<Thing>()) ?? Enumerable.Empty<Thing>())
                if (worn.def.useHitPoints)
                    worn.HitPoints = Mathf.Max(1, Mathf.RoundToInt(worn.MaxHitPoints * Rand.Range(0.2f, 0.6f)));
        }

        static void ScatterSlag(Map map, IntVec3 centre, float radius)
        {
            int count = Rand.RangeInclusive(5, 9);
            for (int i = 0; i < count; i++)
                Place(map, ThingMaker.MakeThing(ThingDefOf.ChunkSlagSteel), centre, radius, damage: false);
        }

        /// <summary>A few of the surface's wild animals live down here, used to it.</summary>
        static void SpawnDwellers(Map map, List<IntVec3> cells)
        {
            BiomeDef surface = (map.Parent as PocketMapParent)?.sourceMap?.Biome;
            if (surface == null)
                return;
            List<PawnKindDef> kinds = surface.AllWildAnimals.Where(k => surface.CommonalityOfAnimal(k) > 0f).ToList();
            if (kinds.Count == 0)
                return;
            int count = Rand.RangeInclusive(2, 4);
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = cells.Where(c => c.Standable(map) && !c.GetTerrain(map).IsWater).RandomElementWithFallback(IntVec3.Invalid);
                if (!cell.IsValid)
                    return;
                GenSpawn.Spawn(PawnGenerator.GeneratePawn(kinds.RandomElementByWeight(k => surface.CommonalityOfAnimal(k))), cell, map);
            }
        }

        static void Place(Map map, Thing t, IntVec3 at, float radius, bool damage)
        {
            if (damage && t.def.useHitPoints)
                t.HitPoints = Mathf.Max(1, Mathf.RoundToInt(t.MaxHitPoints * Rand.Range(0.25f, 0.6f)));
            if (TryCell(map, at, radius, out IntVec3 cell))
                GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near);
            else
                t.Destroy();
        }

        static bool TryCell(Map map, IntVec3 at, float radius, out IntVec3 cell) =>
            CellFinder.TryFindRandomCellNear(at, map, Mathf.CeilToInt(radius),
                c => c.Standable(map) && !c.GetTerrain(map).IsWater && c.GetFirstItem(map) == null, out cell);

        static void Carve(Map map, IntVec3 a, IntVec3 b, float width, Perlin noise, HashSet<IntVec3> open)
        {
            Vector3 pos = a.ToVector3Shifted();
            Vector3 end = b.ToVector3Shifted();
            for (int step = 0; step < 600 && (pos - end).MagnitudeHorizontal() > 1.5f; step++)
            {
                Vector3 dir = (end - pos).normalized;
                float wander = (float)noise.GetValue(pos.x, 0, pos.z) * 70f;
                pos += Quaternion.AngleAxis(wander, Vector3.up) * dir * 0.8f;
                pos.x = Mathf.Clamp(pos.x, 4f, map.Size.x - 5f);
                pos.z = Mathf.Clamp(pos.z, 4f, map.Size.z - 5f);
                Dig(map, pos.ToIntVec3(), width + (float)noise.GetValue(pos.z, 0, pos.x) * 0.5f, open, null);
            }
        }

        static void Dig(Map map, IntVec3 at, float radius, HashSet<IntVec3> open, HashSet<IntVec3> chamber)
        {
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, Mathf.Max(radius, 1f), true))
                if (c.InBounds(map) && c.DistanceToEdge(map) > 1)
                {
                    open.Add(c);
                    chamber?.Add(c);
                }
        }
    }

    /// <summary>
    /// What a gut does to whoever is in it. Every pawn that isn't one of the wild things living
    /// down here slowly takes on RR_GutDigestion (which wears off once they're out): the
    /// further along it is, the more often the acid burns them. The slurry also soaks into
    /// them - they don't go hungry, and they put on weight. Anything left lying on the floor is
    /// slowly eaten away, and bodies rot fast.
    /// </summary>
    public static class GutDigestion
    {
        public const int PawnInterval = 250;
        public const int ItemInterval = GenTicks.TickLongInterval;
        /// <summary>Severity per day while in a gut: the hediff also loses 3 a day on its own, so this nets about +2.6 (dissolving within a working day).</summary>
        const float SeverityPerDayInGut = 5.6f;
        const float BurnsFrom = 0.3f;
        const float BurnChancePerSeverity = 0.15f;
        const float FoodPerPulse = 0.006f;
        const float KilosPerPulse = 0.08f;
        const float ItemDamageFraction = 0.03f;
        const float RotPerItemPulse = 5000f;

        public static bool IsGut(Map map) => map.Biome.defName == "RR_GorgeGut";

        public static void DigestPawns(Map map)
        {
            HediffDef def = GorgeDefs.Digestion;
            if (def == null)
                return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                Pawn p = pawns[i];
                if (p.Dead || !p.RaceProps.IsFlesh || (p.Faction == null && !p.RaceProps.Humanlike))
                    continue;
                Hediff h = p.health.hediffSet.GetFirstHediffOfDef(def) ?? p.health.AddHediff(def);
                h.Severity += SeverityPerDayInGut * PawnInterval / GenDate.TicksPerDay;
                if (h.Severity > BurnsFrom && Rand.Chance((h.Severity - BurnsFrom) * BurnChancePerSeverity))
                {
                    BodyPartRecord part = p.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside).RandomElementWithFallback();
                    if (part != null)
                        p.TakeDamage(new DamageInfo(DamageDefOf.AcidBurn, Rand.Range(1f, 4f), 0f, -1f, null, part));
                }
                if (p.Dead)
                    continue;
                if (p.needs?.food is Need_Food food)
                    food.CurLevel = Mathf.Min(food.MaxLevel, food.CurLevel + FoodPerPulse);
                if (p.RaceProps.Humanlike)
                    Utilities.HediffUtility.QueueWeightGain(p, KilosPerPulse);
            }
        }

        public static void DigestItems(Map map)
        {
            List<Thing> things = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (!t.Spawned)
                    continue;
                if (t is Corpse corpse)
                {
                    if (corpse.TryGetComp<CompRottable>() is CompRottable rot)
                        rot.RotProgress += RotPerItemPulse;
                }
                else if (t.def.useHitPoints && t.def.category == ThingCategory.Item)
                    t.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, Mathf.Max(1f, t.MaxHitPoints * ItemDamageFraction)));
            }
        }
    }
}
