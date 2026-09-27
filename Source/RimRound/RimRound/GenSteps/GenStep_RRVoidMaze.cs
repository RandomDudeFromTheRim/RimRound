using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace RimRound.GenSteps
{
    /// <summary>
    /// Carves the void maze as one long gut: a linear run of lumpy chambers from
    /// where the pawn arrives to the way home at the far end, joined by coiling
    /// tubes that swell and pinch like peristalsis.
    ///
    /// The map is split into a coarse grid and the route is the longest path
    /// through a random spanning tree of it. Each tube may only coil inside its
    /// own pair of grid cells, so tubes never touch other parts of the route.
    /// Everything open is lined with a band of diggable flesh, which turns into
    /// void gluttonium flesh deeper in; everything past that band is hardened
    /// (indestructible) flesh, so there are no shortcuts to dig either.
    /// </summary>
    public class GenStep_RRVoidMaze : GenStep
    {
        public override int SeedPart => 20415307;

        const int HardenedShellDepth = 3;
        const int Grid = 5;               // the route runs through a Grid x Grid layout of cells
        const int RouteTries = 40;
        const int CellMargin = 3;         // open space stays this far inside its grid cell
        const int RoomInset = 5;          // extra inset for chamber centres
        const int WallBand = 2;           // diggable flesh this deep around everything open
        const float MaxGluttoniumWallChance = 0.7f;

        const float VesselWobbleDegrees = 115f;
        const float GutWobbleDegrees = 115f;
        const float MaxTurnPerStep = 14f;
        const float StepLength = 0.75f;
        const float BlindGutChance = 0.5f;
        const int PolypsPerBigChamber = 3;

        const int MaxSpitters = 4;
        const int MaxMaws = 12;
        const float MawSpacing = 9f;
        const int MaxGeysers = 10;
        const float GeyserSpacing = 14f;
        const float HazardEntryClearance = 12f;
        const int MaxRewards = 45;
        static readonly IntRange EndCacheRange = new IntRange(25, 40);

        // Per-run state. GenStep instances belong to the (shared) def, so these
        // are cleared again once generation finishes.
        Map map;
        float origin, cellSize;
        List<IntVec2> route;
        Dictionary<IntVec2, int> routeIndex;
        List<Chamber> chambers;
        HashSet<IntVec3> carved;
        List<IntVec3> carvedList; // carved cells that hazards and rewards can be placed on
        Perlin noise;

        struct Chamber
        {
            public Vector2 pos;
            public float radius;
            public IntVec3 Cell => ToCell(pos);
        }

        /// <summary>An inclusive axis-aligned rectangle in map coordinates.</summary>
        struct Box
        {
            public float x0, z0, x1, z1;
            public bool Contains(Vector2 p) => p.x >= x0 && p.x <= x1 && p.y >= z0 && p.y <= z1;
        }

        public override void Generate(Map map, GenStepParams parms)
        {
            this.map = map;
            origin = HardenedShellDepth + 2;
            cellSize = (map.Size.x - 2f * origin) / Grid;
            carved = new HashSet<IntVec3>();
            noise = new Perlin(1.0, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
            try
            {
                GenerateInternal();
            }
            finally
            {
                this.map = null;
                route = null;
                routeIndex = null;
                chambers = null;
                carved = null;
                carvedList = null;
                noise = null;
            }
        }

        void GenerateInternal()
        {
            TerrainDef floor = TerrainDef.Named("RR_FleshFloor");
            foreach (IntVec3 cell in map.AllCells)
                map.terrainGrid.SetTerrain(cell, floor);

            route = LongestRoute();
            routeIndex = new Dictionary<IntVec2, int>();
            for (int i = 0; i < route.Count; i++)
                routeIndex[route[i]] = i;

            CarveChambers();
            for (int i = 0; i < route.Count - 1; i++)
                CarveVessel(i);
            for (int i = 1; i < route.Count - 1; i++)
                if (Rand.Chance(BlindGutChance))
                    CarveBlindGut(i);
            GrowPolyps();

            carvedList = carved.ToList();
            BuildWalls();

            IntVec3 start = chambers[0].Cell, end = chambers[chambers.Count - 1].Cell;
            map.GetComponent<Comps.MapComponent_RRVoidMaze>()?.SetRoute(chambers.Select(c => c.Cell).ToList());
            GenSpawn.Spawn(ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidMazeEntryScar), start, map);
            GenSpawn.Spawn(ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidPortalReturn), end, map);
            PlaceEndCache(end);

            PlaceSpitters();
            PlaceGeysers();
            PlaceMaws();
            PlaceRewards();

            // GenStep_Fog floods the unfog out from here, so it must be an open cell
            MapGenerator.PlayerStartSpot = start;
        }

        // ------------------------------------------------------------------ route

        /// <summary>
        /// The longest path from a start cell on the west edge through a random
        /// spanning tree of the grid. Usually visits (nearly) every cell.
        /// </summary>
        static List<IntVec2> LongestRoute()
        {
            List<IntVec2> best = new List<IntVec2>();
            for (int attempt = 0; attempt < RouteTries && best.Count < Grid * Grid; attempt++)
            {
                IntVec2 start = new IntVec2(0, Rand.Range(0, Grid));
                var parent = new Dictionary<IntVec2, IntVec2?> { [start] = null };
                var depth = new Dictionary<IntVec2, int> { [start] = 0 };
                var stack = new List<IntVec2> { start };
                while (stack.Count > 0)
                {
                    IntVec2 c = stack[stack.Count - 1];
                    List<IntVec2> open = GridNeighbours(c).Where(n => !parent.ContainsKey(n)).ToList();
                    if (open.Count == 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                        continue;
                    }
                    IntVec2 next = open.RandomElement();
                    parent[next] = c;
                    depth[next] = depth[c] + 1;
                    stack.Add(next);
                }

                IntVec2? far = depth.MaxBy(kv => kv.Value).Key;
                var path = new List<IntVec2>();
                while (far != null)
                {
                    path.Add(far.Value);
                    far = parent[far.Value];
                }
                path.Reverse();
                if (path.Count > best.Count)
                    best = path;
            }
            return best;
        }

        static IEnumerable<IntVec2> GridNeighbours(IntVec2 c)
        {
            if (c.x > 0) yield return new IntVec2(c.x - 1, c.z);
            if (c.x < Grid - 1) yield return new IntVec2(c.x + 1, c.z);
            if (c.z > 0) yield return new IntVec2(c.x, c.z - 1);
            if (c.z < Grid - 1) yield return new IntVec2(c.x, c.z + 1);
        }

        Box CellBox(IntVec2 gc, float inset)
        {
            float x0 = origin + gc.x * cellSize, z0 = origin + gc.z * cellSize;
            return new Box { x0 = x0 + inset, z0 = z0 + inset, x1 = x0 + cellSize - inset, z1 = z0 + cellSize - inset };
        }

        /// <summary>Where a tube between two neighbouring grid cells may go: both cells, plus a bridge across their shared edge.</summary>
        List<Box> PairBoxes(IntVec2 a, IntVec2 b)
        {
            Box ra = CellBox(a, CellMargin), rb = CellBox(b, CellMargin);
            Box bridge = a.x != b.x
                ? new Box { x0 = Mathf.Min(ra.x1, rb.x1), z0 = Mathf.Max(ra.z0, rb.z0), x1 = Mathf.Max(ra.x0, rb.x0), z1 = Mathf.Min(ra.z1, rb.z1) }
                : new Box { x0 = Mathf.Max(ra.x0, rb.x0), z0 = Mathf.Min(ra.z1, rb.z1), x1 = Mathf.Min(ra.x1, rb.x1), z1 = Mathf.Max(ra.z0, rb.z0) };
            return new List<Box> { ra, rb, bridge };
        }

        /// <summary>How far along the route a cell is, 0 at the start to 1 at the end; -1 off the route.</summary>
        float Progress(IntVec3 c)
        {
            int gx = Mathf.Clamp(Mathf.FloorToInt((c.x - origin) / cellSize), 0, Grid - 1);
            int gz = Mathf.Clamp(Mathf.FloorToInt((c.z - origin) / cellSize), 0, Grid - 1);
            return routeIndex.TryGetValue(new IntVec2(gx, gz), out int i) ? (float)i / Mathf.Max(1, route.Count - 1) : -1f;
        }

        // ------------------------------------------------------------------ carving

        /// <summary>A lumpy chamber in each grid cell of the route: the radius swells and dents with angle.</summary>
        void CarveChambers()
        {
            chambers = new List<Chamber>();
            for (int i = 0; i < route.Count; i++)
            {
                Box inner = CellBox(route[i], CellMargin + RoomInset);
                var chamber = new Chamber
                {
                    pos = new Vector2(Rand.Range(inner.x0, inner.x1), Rand.Range(inner.z0, inner.z1)),
                    radius = i == 0 ? 4f : i == route.Count - 1 ? 5.5f : Rand.Range(3.2f, 5.2f)
                };
                chambers.Add(chamber);

                var boxes = new List<Box> { CellBox(route[i], CellMargin) };
                int reach = Mathf.CeilToInt(chamber.radius * 1.4f);
                float seed = Rand.Range(0f, 1000f);
                IntVec3 c0 = chamber.Cell;
                for (int dx = -reach; dx <= reach; dx++)
                for (int dz = -reach; dz <= reach; dz++)
                {
                    var cell = new IntVec3(c0.x + dx, 0, c0.z + dz);
                    Vector2 off = new Vector2(cell.x, cell.z) - chamber.pos;
                    float angle = Mathf.Atan2(off.y, off.x);
                    float lump = (float)noise.GetValue(seed + Mathf.Cos(angle) * 1.2f, seed + Mathf.Sin(angle) * 1.2f, 0.0);
                    if (off.magnitude <= chamber.radius * (1f + 0.35f * lump))
                        Carve(cell, boxes);
                }
            }
        }

        /// <summary>
        /// A long coiling tube from chamber i to chamber i+1. Heading follows smooth
        /// noise around the direction of the target, turning back whenever it would
        /// leave its pair of grid cells. The coiling fades out close to the target
        /// so the tube always arrives.
        /// </summary>
        void CarveVessel(int i)
        {
            List<Box> boxes = PairBoxes(route[i], route[i + 1]);
            Vector2 from = chambers[i].pos, to = chambers[i + 1].pos;
            Vector2 pos = from;
            float heading = Angle(to - from);
            float noiseOffset = Rand.Range(0f, 1000f);
            float phase = Rand.Range(0f, Mathf.PI * 2f);
            int maxSteps = Mathf.CeilToInt(Vector2.Distance(from, to) * 4f) + 30;
            IntVec3 last = ToCell(pos);
            int pouchCooldown = 10;

            for (int step = 0; step < maxSteps; step++)
            {
                float dist = Vector2.Distance(pos, to);
                if (dist < 1.5f)
                    break;

                float wobble = VesselWobbleDegrees * Mathf.Clamp01((dist - 3f) / 6f);
                float desired = Angle(to - pos) + (float)noise.GetValue(noiseOffset + step * 0.06, 0.0, 0.0) * wobble;
                float next = Mathf.MoveTowardsAngle(heading, desired, MaxTurnPerStep);
                if (!InAny(boxes, pos + Dir(next) * 3f))
                    next = Mathf.MoveTowardsAngle(heading, Angle(to - pos), MaxTurnPerStep * 2f);
                heading = next;
                pos += Dir(heading) * StepLength;

                // peristalsis: the tube slowly swells to ~3 wide and pinches to 1
                float radius = 1.15f + 0.55f * Mathf.Sin(step * 0.16f + phase);
                last = CarveDisc(pos, radius, last, boxes);

                // now and then a pouch buds off the side of the tube
                if (--pouchCooldown <= 0 && Rand.Chance(0.03f))
                {
                    float side = Rand.Bool ? 90f : -90f;
                    CarveDisc(pos + Dir(heading + side) * 2.2f, Rand.Range(0.9f, 1.4f), ToCell(pos), boxes);
                    pouchCooldown = 14;
                }
            }

            // steps ran out: force the last stretch straight so the chambers stay connected
            int lineSteps = Mathf.CeilToInt(Vector2.Distance(pos, to) / 0.5f);
            for (int s = 1; s <= lineSteps; s++)
                last = CarveDisc(Vector2.Lerp(pos, to, (float)s / lineSteps), 0.75f, last, boxes);
        }

        /// <summary>A narrow, tightly coiled dead end off chamber i, kept inside its own grid cell, ending in a pouch.</summary>
        void CarveBlindGut(int i)
        {
            var boxes = new List<Box> { CellBox(route[i], CellMargin) };
            Box own = CellBox(route[i], 0f);
            IntVec3 start = carved.Where(c => own.Contains(new Vector2(c.x, c.z))).RandomElementWithFallback(IntVec3.Invalid);
            if (!start.IsValid)
                return;

            Vector2 pos = new Vector2(start.x, start.z);
            float heading = Rand.Range(0f, 360f);
            float noiseOffset = Rand.Range(0f, 1000f);
            int length = Rand.RangeInclusive(20, 40);
            IntVec3 last = start;

            for (int step = 0; step < length; step++)
            {
                float next = Mathf.MoveTowardsAngle(heading,
                    heading + (float)noise.GetValue(noiseOffset + step * 0.07, 0.0, 0.0) * GutWobbleDegrees,
                    MaxTurnPerStep * 1.4f);
                if (!InAny(boxes, pos + Dir(next) * 2f))
                    next = Mathf.MoveTowardsAngle(heading, Angle(chambers[i].pos - pos), MaxTurnPerStep * 3f);
                heading = next;
                pos += Dir(heading) * StepLength;
                last = CarveDisc(pos, Rand.Chance(0.8f) ? 0.75f : 1.1f, last, boxes);
            }
            CarveDisc(pos, Rand.Range(1.4f, 2f), last, boxes);
        }

        /// <summary>
        /// Lumps of flesh growing up out of the floor of the bigger chambers. Only
        /// cells with all eight neighbours open, so a polyp never blocks a passage.
        /// </summary>
        void GrowPolyps()
        {
            for (int i = 1; i < chambers.Count - 1; i++)
            {
                Chamber chamber = chambers[i];
                if (chamber.radius < 4f)
                    continue;
                int grown = 0;
                for (int t = 0; t < 20 && grown < PolypsPerBigChamber; t++)
                {
                    IntVec3 cell = ToCell(chamber.pos + Rand.InsideUnitCircle * (chamber.radius - 1.5f));
                    if (!carved.Contains(cell) || OpenNeighbors(cell) < 8)
                        continue;
                    carved.Remove(cell);
                    grown++;
                }
            }
        }

        /// <summary>Carves a disc and returns its centre cell. A 1-wide tube that steps diagonally gets the corner cell too, since pawns can't cut wall corners.</summary>
        IntVec3 CarveDisc(Vector2 pos, float radius, IntVec3 last, List<Box> boxes)
        {
            IntVec3 cell = ToCell(pos);
            int reach = Mathf.CeilToInt(radius);
            for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                var c = new IntVec3(cell.x + dx, 0, cell.z + dz);
                if (Vector2.Distance(new Vector2(c.x, c.z), pos) <= radius)
                    Carve(c, boxes);
            }
            Carve(cell, boxes);
            if (last.IsValid && last.x != cell.x && last.z != cell.z)
                Carve(new IntVec3(last.x, 0, cell.z), boxes);
            return cell;
        }

        void Carve(IntVec3 cell, List<Box> boxes)
        {
            if (cell.InBounds(map) && InAny(boxes, new Vector2(cell.x, cell.z)))
                carved.Add(cell);
        }

        static bool InAny(List<Box> boxes, Vector2 p)
        {
            for (int i = 0; i < boxes.Count; i++)
                if (boxes[i].Contains(p))
                    return true;
            return false;
        }

        static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;

        static Vector2 Dir(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        static IntVec3 ToCell(Vector2 v) => new IntVec3(Mathf.RoundToInt(v.x), 0, Mathf.RoundToInt(v.y));

        IntVec3 RandomCarvedCell() => carvedList[Rand.Range(0, carvedList.Count)];

        // ------------------------------------------------------------------ walls

        /// <summary>
        /// A band of diggable flesh around everything open (plain flesh and blob
        /// walls early on, more and more void gluttonium flesh toward the end), and
        /// hardened flesh everywhere else so no shortcut can be dug.
        /// </summary>
        void BuildWalls()
        {
            ThingDef hardWall = ThingDef.Named("RR_HardenedFleshWall");
            ThingDef blobWall = ThingDef.Named("RR_BlobWall");
            ThingDef gluttonWall = ThingDef.Named("RR_BlobWallMineable");
            // RimRound's own flesh wall, not Anomaly's Fleshmass_Active: that one links
            // on a different flag (broken edges against our walls) and belongs to the
            // live fleshmass-heart system
            ThingDef fleshWall = ThingDef.Named("RR_FleshWall");

            HashSet<IntVec3> band = WallBandCells();
            foreach (IntVec3 cell in map.AllCells)
            {
                if (carved.Contains(cell))
                    continue;

                ThingDef def = hardWall;
                if (band.Contains(cell) && !NearMapEdge(cell))
                {
                    float progress = Mathf.Max(0f, Progress(cell));
                    if (Rand.Chance(MaxGluttoniumWallChance * Mathf.Pow(progress, 1.5f)))
                        def = gluttonWall;
                    else
                        def = Rand.Chance(0.62f) ? blobWall : fleshWall;
                }
                GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map);
            }
        }

        HashSet<IntVec3> WallBandCells()
        {
            var band = new HashSet<IntVec3>();
            var frontier = new List<IntVec3>(carved);
            for (int depth = 0; depth < WallBand; depth++)
            {
                var next = new List<IntVec3>();
                foreach (IntVec3 c in frontier)
                {
                    foreach (IntVec3 offset in GenAdj.AdjacentCells)
                    {
                        IntVec3 n = c + offset;
                        if (n.InBounds(map) && !carved.Contains(n) && band.Add(n))
                            next.Add(n);
                    }
                }
                frontier = next;
            }
            return band;
        }

        bool NearMapEdge(IntVec3 c)
        {
            return System.Math.Min(System.Math.Min(c.x, map.Size.x - 1 - c.x), System.Math.Min(c.z, map.Size.z - 1 - c.z)) <= HardenedShellDepth;
        }

        // ------------------------------------------------------------------ contents

        bool NearEntry(IntVec3 c) => c.InHorDistOf(chambers[0].Cell, HazardEntryClearance);

        /// <summary>The prize for making it to the end: a heap of void gluttonium by the way home.</summary>
        void PlaceEndCache(IntVec3 end)
        {
            Thing cache = ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidGluttonium);
            cache.stackCount = EndCacheRange.RandomInRange;
            GenPlace.TryPlaceThing(cache, end, map, ThingPlaceMode.Near);
        }

        /// <summary>
        /// Bloat spitters (2x2 organs) in chambers, more likely deeper in. Only where
        /// every cell around the organ is open too, so it can never plug a passage.
        /// </summary>
        void PlaceSpitters()
        {
            if (!ModsConfig.AnomalyActive)
                return; // the spitter wears Anomaly's fleshmass spitter sprite

            ThingDef spitterDef = ThingDef.Named("RR_BloatSpitter");
            int placed = 0;
            for (int i = 1; i < chambers.Count - 1 && placed < MaxSpitters; i++)
            {
                float progress = (float)i / (chambers.Count - 1);
                if (progress < 0.25f || !Rand.Chance(0.15f + 0.35f * progress))
                    continue;

                for (int t = 0; t < 12; t++)
                {
                    IntVec3 cell = ToCell(chambers[i].pos + Rand.InsideUnitCircle * (chambers[i].radius * 0.5f));
                    CellRect rect = GenAdj.OccupiedRect(cell, Rot4.North, spitterDef.size);
                    if (!rect.ExpandedBy(1).All(c => carved.Contains(c)) || rect.Any(c => c.GetFirstThing<Thing>(map) != null))
                        continue;

                    Thing spitter = ThingMaker.MakeThing(spitterDef);
                    spitter.SetFaction(Faction.OfEntities);
                    GenSpawn.Spawn(spitter, cell, map);
                    foreach (IntVec3 c in rect)
                        carved.Remove(c); // no longer open floor for geysers and rewards
                    placed++;
                    break;
                }
            }
        }

        /// <summary>Bloatgas geysers in the roomier stretches (not 1-wide squeezes), spread apart and clear of the entry.</summary>
        void PlaceGeysers()
        {
            var placed = new List<IntVec3>();
            for (int i = 0; i < 800 && placed.Count < MaxGeysers; i++)
            {
                IntVec3 spot = RandomCarvedCell();
                if (NearEntry(spot) || !spot.Standable(map) || spot.GetFirstBuilding(map) != null)
                    continue;
                if (OpenNeighbors(spot) < 7 || placed.Any(p => p.DistanceTo(spot) < GeyserSpacing))
                    continue;

                GenSpawn.Spawn(ThingMaker.MakeThing(Defs.ThingDefOf.RR_BloatGeyser), spot, map);
                placed.Add(spot);
            }
        }

        /// <summary>Gorge maws in 1-wide squeezes, where there's no stepping around them; more of them deeper in.</summary>
        void PlaceMaws()
        {
            ThingDef mawDef = ThingDef.Named("RR_GorgeMaw");
            var placed = new List<IntVec3>();
            for (int i = 0; i < 1200 && placed.Count < MaxMaws; i++)
            {
                IntVec3 spot = RandomCarvedCell();
                if (NearEntry(spot) || !IsSqueeze(spot) || !Rand.Chance(0.2f + 0.8f * Mathf.Max(0f, Progress(spot))))
                    continue;
                if (spot.GetFirstBuilding(map) != null || spot.GetFirstItem(map) != null || placed.Any(p => p.DistanceTo(spot) < MawSpacing))
                    continue;

                Thing maw = ThingMaker.MakeThing(mawDef);
                if (ModsConfig.AnomalyActive)
                    maw.SetFaction(Faction.OfEntities);
                GenSpawn.Spawn(maw, spot, map);
                placed.Add(spot);
            }
        }

        /// <summary>A corridor cell: open on exactly two opposite sides.</summary>
        bool IsSqueeze(IntVec3 c)
        {
            bool ns = carved.Contains(c + IntVec3.North) && carved.Contains(c + IntVec3.South);
            bool ew = carved.Contains(c + IntVec3.East) && carved.Contains(c + IntVec3.West);
            return ns != ew && (ns ? !carved.Contains(c + IntVec3.East) && !carved.Contains(c + IntVec3.West)
                                   : !carved.Contains(c + IntVec3.North) && !carved.Contains(c + IntVec3.South));
        }

        int OpenNeighbors(IntVec3 spot)
        {
            int open = 0;
            foreach (IntVec3 n in GenAdj.AdjacentCells)
                if (carved.Contains(spot + n))
                    open++;
            return open;
        }

        /// <summary>Mineables and gluttonium tucked into dead pockets away from the entry.</summary>
        void PlaceRewards()
        {
            ThingDef mineable = ThingDef.Named("RR_BlobWallMineable");
            int placed = 0;
            for (int i = 0; i < 400 && placed < MaxRewards; i++)
            {
                IntVec3 spot = RandomCarvedCell();
                if (NearEntry(spot) || !spot.Standable(map) || spot.GetFirstThing<Building>(map) != null || spot.GetFirstItem(map) != null)
                    continue;
                if (!IsDeadPocket(spot))
                    continue;

                // a mineable node is a wall, so only in true dead ends: in a 1-wide
                // stretch of the route it would plug the only way forward
                if (IsDeadEnd(spot) && Rand.Value < 0.55f)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(mineable), spot, map);
                }
                else
                {
                    Thing loot = ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidGluttonium);
                    loot.stackCount = Rand.RangeInclusive(3, 8);
                    GenSpawn.Spawn(loot, spot, map);
                }
                placed++;
            }
        }

        /// <summary>Mostly surrounded by wall: no more than 3 of the 8 neighbours are open.</summary>
        bool IsDeadPocket(IntVec3 spot) => OpenNeighbors(spot) <= 3;

        /// <summary>The closed end of a tube: open on one side only.</summary>
        bool IsDeadEnd(IntVec3 spot) => GenAdj.CardinalDirections.Count(d => carved.Contains(spot + d)) == 1;
    }
}
