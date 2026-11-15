using System.Collections.Concurrent;
using RimWorld;
using UnityEngine;
using Verse;
using HediffUtility = RimRound.Utilities.HediffUtility;
using RimRound.Comps;

namespace RimRound.Rendering
{
    /// <summary>
    /// Visual-only squish: a heavy pawn pressed against a wall or a table is drawn narrower and
    /// taller, and it settles into that shape with a damped spring instead of snapping, so the
    /// fat reads as soft rather than as a scale factor.
    ///
    /// Everything here that touches engine state runs on the main thread from Pawn.Tick. The
    /// render path only ever READS a float: calling SetAllGraphicsDirty from inside
    /// PawnRenderTree.ParallelPreDraw mutates the graphics list while the job is walking it, which
    /// throws ArgumentOutOfRangeException and faults the render job.
    /// </summary>
    public static class SquishUtility
    {
        /// <summary>Nothing squishes below this weight (severity * 1000 kg).</summary>
        public const float StartSeverity = 0.05f;    // 50 kg
        /// <summary>Full strength at this weight.</summary>
        public const float FullSeverity = 0.20f;     // 200 kg
        public const float MaxCompression = 0.32f;
        public const float MaxBulge = 0.22f;

        // spring constants: soft, slightly underdamped so it wobbles once before settling
        const float Stiffness = 165f;
        const float Damping = 0.74f;
        const float Dt = 1f / 60f;

        class State
        {
            public float current;      // current compression 0..1, read by the render path
            public float velocity;
            public float target;
            public int mask;
        }

        static readonly ConcurrentDictionary<int, State> states = new ConcurrentDictionary<int, State>();
        static readonly ConcurrentDictionary<int, bool> forcedPawns = new ConcurrentDictionary<int, bool>();

        public static void ToggleForced(Pawn pawn)
        {
            if (pawn == null) return;
            int id = pawn.thingIDNumber;
            bool now = !forcedPawns.ContainsKey(id);
            if (now) forcedPawns[id] = true;
            else forcedPawns.TryRemove(id, out _);
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        /// <summary>Called from Pawn.Tick (main thread). Decides the target and springs toward it.</summary>
        public static void Tick(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null || !pawn.RaceProps.Humanlike)
                return;

            State st = states.GetOrAdd(pawn.thingIDNumber, _ => new State());

            if (!st.current.Equals(0f) || !st.velocity.Equals(0f) || pawn.IsHashIntervalTick(15))
                st.target = TargetFor(pawn);

            int mask = ContactMask(pawn);
            if (mask != st.mask)
            {
                st.mask = mask;
                st.target = TargetFor(pawn, mask);
                // safe here: this is the main thread, not the render job
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }

            // damped spring toward the target
            if (!Mathf.Approximately(st.current, st.target) || Mathf.Abs(st.velocity) > 0.0005f)
            {
                st.velocity += (st.target - st.current) * Stiffness * Dt;
                st.velocity *= Damping;
                st.current += st.velocity * Dt;
                st.current = Mathf.Clamp(st.current, 0f, 1f);
            }
        }

        /// <summary>0..1 compression to draw at, and which sides are blocked. Read-only.</summary>
        public static bool TryRead(Pawn pawn, out float compression, out int mask)
        {
            compression = 0f;
            mask = 0;
            if (pawn == null) return false;
            if (!states.TryGetValue(pawn.thingIDNumber, out State st)) return false;
            mask = st.mask;
            if (mask == 0 || st.current <= 0.001f) return false;
            compression = st.current;
            return true;
        }

        static float TargetFor(Pawn pawn, int mask = -1)
        {
            if (forcedPawns.ContainsKey(pawn.thingIDNumber))
                return (mask < 0 ? ContactMask(pawn) : mask) == 0 ? 0f : 1f;

            if (!RendersFat(pawn))
                return 0f;

            if (mask < 0) mask = ContactMask(pawn);
            if (mask == 0)
                return 0f;

            Hediff weight = HediffUtility.WeightHediff(pawn);
            if (weight == null || weight.Severity < StartSeverity)
                return 0f;

            return Mathf.Clamp01(Mathf.InverseLerp(StartSeverity, FullSeverity, weight.Severity));
        }

        /// <summary>A pawn whose bulk is actually being drawn: has the comp, and is not exempt.</summary>
        static bool RendersFat(Pawn pawn)
        {
            PawnBodyType_ThingComp pbt = pawn.TryGetComp<PawnBodyType_ThingComp>();
            if (pbt == null)
                return false;
            if (pbt.PersonallyExempt || pbt.CategoricallyExempt)
                return false;
            return true;
        }

        public static int ContactMask(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
                return 0;
            Map map = pawn.Map;
            IntVec3 cell = pawn.PositionHeld;
            int mask = 0;
            if (Blocked(map, cell + IntVec3.West)) mask |= 1;
            if (Blocked(map, cell + IntVec3.East)) mask |= 2;
            return mask;
        }

        static bool Blocked(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return true;
            if (map.edificeGrid[cell] != null) return true;
            return !cell.Standable(map);
        }

        public static void ClearCaches()
        {
            states.Clear();
            forcedPawns.Clear();
        }
    }
}
