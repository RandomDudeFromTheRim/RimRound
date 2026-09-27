using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimRound.Comps
{
    /// <summary>
    /// Remembers the void maze's route: its chamber centres in order, from the
    /// entry to the way home. Straight-line distance is useless for "how far in"
    /// because the gut snakes back on itself. Empty on every other map.
    /// </summary>
    public class MapComponent_RRVoidMaze : MapComponent
    {
        List<IntVec3> route = new List<IntVec3>();

        public MapComponent_RRVoidMaze(Map map) : base(map)
        {
        }

        public bool HasRoute => route.Count > 1;

        public void SetRoute(List<IntVec3> chamberCentres)
        {
            route = new List<IntVec3>(chamberCentres);
        }

        /// <summary>How far along the route a cell is: 0 at the entry, 1 at the way home.</summary>
        public float ProgressAt(IntVec3 cell)
        {
            if (!HasRoute)
                return 0.5f;

            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < route.Count; i++)
            {
                float d = route[i].DistanceToSquared(cell);
                if (d < best)
                {
                    best = d;
                    nearest = i;
                }
            }
            return Mathf.Clamp01((float)nearest / (route.Count - 1));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref route, "route", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && route == null)
                route = new List<IntVec3>();
        }
    }
}
