using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using HediffUtility = RimRound.Utilities.HediffUtility;

namespace RimRound.Rendering
{
    /// <summary>
    /// Visual-only squish: a fat pawn standing against a wall, a table or a doorframe is drawn
    /// narrower and taller, as if the bulk had somewhere to go. Purely cosmetic - it touches no
    /// pathing, no hitbox and no save state.
    ///
    /// The engine draws a pawn through render nodes, and <see cref="PawnRenderNodeWorker.ScaleFor"/>
    /// returns a Vector3 per node. A non-uniform answer there is the whole feature; the head keeps
    /// its own node and is therefore unaffected.
    /// </summary>
    public static class SquishUtility
    {
        /// <summary>Below this weight (severity * 1000 kg) nothing squishes. Thin pawns stay thin.</summary>
        public const float StartSeverity = 0.05f;    // 50 kg
        /// <summary>At this weight the squish is at full strength.</summary>
        public const float FullSeverity = 0.20f;     // 200 kg
        /// <summary>How much narrower a fully squished body is drawn.</summary>
        public const float MaxCompression = 0.32f;
        /// <summary>How much taller it is drawn, to keep the mass looking constant.</summary>
        public const float MaxBulge = 0.22f;

        static readonly Dictionary<int, int> lastState = new Dictionary<int, int>();
        static bool forced;

        /// <summary>Debug switch so the effect can be looked at without waiting for a fat pawn.</summary>
        public static bool Forced
        {
            get => forced;
            set => forced = value;
        }

        /// <summary>0..1, how hard this pawn squishes. Zero means "draw normally".</summary>
        public static float StrengthFor(Pawn pawn)
        {
            if (forced && pawn != null)
                return 1f;
            if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike)
                return 0f;

            Hediff weight = HediffUtility.WeightHediff(pawn);
            if (weight == null || weight.Severity < StartSeverity)
                return 0f;

            return Mathf.Clamp01(Mathf.InverseLerp(StartSeverity, FullSeverity, weight.Severity));
        }

        /// <summary>
        /// What is in the way, as two bits: 1 = something blocks the pawn's left, 2 = its right.
        /// Uses the same grid queries the game uses for standing room.
        /// </summary>
        public static int ContactMask(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
                return 0;

            Map map = pawn.Map;
            IntVec3 cell = pawn.PositionHeld;
            int mask = 0;
            if (Blocked(map, cell + IntVec3.West))
                mask |= 1;
            if (Blocked(map, cell + IntVec3.East))
                mask |= 2;
            return mask;
        }

        static bool Blocked(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map))
                return true;
            if (map.edificeGrid[cell] != null)
                return true;
            // catches pawns, items and anything else that takes the cell's standing room
            return !cell.Standable(map);
        }

        /// <summary>
        /// Apply the squish to a node's scale. Returns false when nothing should change.
        /// </summary>
        public static bool TrySquish(Pawn pawn, ref Vector3 scale, out int state)
        {
            state = 0;
            if (pawn == null)
                return false;
            if (!pawn.Spawned || pawn.Map == null)
                return false;

            float strength = StrengthFor(pawn);
            if (strength <= 0.001f)
                return false;

            int mask = ContactMask(pawn);
            state = ((int)(strength * 10f) << 2) | mask;
            if (mask == 0)
                return false;

            // pressed from one side: compress, and give a little of it back as height
            float compression = MaxCompression * strength * ((mask == 3) ? 1.5f : 1f);
            scale.x *= 1f - Mathf.Clamp(compression, 0f, 0.55f);
            scale.z *= 1f + MaxBulge * strength * ((mask == 3) ? 1.4f : 1f);
            return true;
        }

        /// <summary>
        /// The draw is cached against facing/posture/flags, not against our state, so a change has
        /// to dirty the graphics explicitly or the pawn keeps its old silhouette.
        /// </summary>
        public static void NoteState(Pawn pawn, int state)
        {
            if (pawn == null)
                return;
            int id = pawn.thingIDNumber;
            if (lastState.TryGetValue(id, out int previous) && previous == state)
                return;
            lastState[id] = state;
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        public static void ClearCaches() => lastState.Clear();
    }
}
