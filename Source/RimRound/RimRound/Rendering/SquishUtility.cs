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

        // spring constants: omega ~9.5 rad/s, zeta ~0.58 - one visible wobble, settles in ~0.5 s.
        // (the first attempt used 165/0.74 which converged in two ticks, so it looked like a snap)
        const float Stiffness = 90f;
        const float Damping = 11f;
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

            int mask = ContactMask(pawn);
            float target = TargetFor(pawn, mask);

            if (mask != st.mask)
            {
                st.mask = mask;
                // safe here: this is the main thread, not the render job
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
            st.target = target;

            // damped spring: accel = k*(target-current) - c*velocity
            if (!Mathf.Approximately(st.current, st.target) || Mathf.Abs(st.velocity) > 0.0005f)
            {
                float accel = (st.target - st.current) * Stiffness - st.velocity * Damping;
                st.velocity += accel * Dt;
                st.current = Mathf.Clamp(st.current + st.velocity * Dt, 0f, 1f);
                if (st.current <= 0f && st.target <= 0f)
                    st.velocity = 0f;
            }
        }

        /// <summary>
        /// How far to shift the body, in cells, so the side that is blocked stays against what is
        /// blocking it. Scaling shrinks toward the node centre, which makes a one-sided squeeze
        /// look like a two-sided one; this puts the blocked edge back where it was.
        /// </summary>
        public static float AnchorShift(Pawn pawn, float drawSizeX)
        {
            if (pawn == null || !states.TryGetValue(pawn.thingIDNumber, out State st))
                return 0f;
            if (st.mask == 0 || st.mask == 3 || st.current <= 0.001f)
                return 0f;                       // both sides: a centred squeeze is correct

            float lost = drawSizeX * MaxCompression * st.current * 0.5f;
            return (st.mask == 1) ? -lost : lost; // west blocked -> shift west, east -> east
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
