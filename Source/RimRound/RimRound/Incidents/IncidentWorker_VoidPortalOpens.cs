using RimRound.Utilities;
using RimWorld;
using System.Linq;
using Verse;

namespace RimRound.Incidents
{
    /// <summary>
    /// The only way a void portal enters a normal game: it tears open on a home
    /// map once someone there is heavy enough to squeeze through it (Chubby+,
    /// the same threshold the portal itself checks).
    /// </summary>
    public class IncidentWorker_VoidPortalOpens : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms) || !(parms.target is Map map))
                return false;

            if (PortalExists(map))
                return false;

            return map.mapPawns.FreeColonistsSpawned.Any(p =>
                BodyTypeUtility.PawnIsOverWeightThreshold(p, Defs.BodyTypeDefOf.F_006_Chonky));
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (!RCellFinder.TryFindRandomCellNearTheCenterOfTheMapWith(c => IsGoodSpot(c, map), map, out IntVec3 cell))
                return false;

            Thing portal = OpenAt(cell, map);
            SendStandardLetter(parms, new LookTargets(portal));
            return true;
        }

        public static bool PortalExists(Map map) => map.listerThings.ThingsOfDef(Defs.ThingDefOf.RR_VoidPortal).Any();

        /// <summary>Tears a void seam open as close to the given cell as it will fit (the Feedees' seam calling), or returns null.</summary>
        public static Thing TryOpenNear(IntVec3 near, Map map)
        {
            if (!CellFinder.TryFindRandomCellNear(near, map, 8, c => IsGoodSpot(c, map) && c.DistanceTo(near) >= 3f, out IntVec3 cell) &&
                !RCellFinder.TryFindRandomCellNearTheCenterOfTheMapWith(c => IsGoodSpot(c, map), map, out cell))
                return null;
            return OpenAt(cell, map);
        }

        static Thing OpenAt(IntVec3 cell, Map map)
        {
            Thing portal = GenSpawn.Spawn(ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidPortal), cell, map);
            FleckMaker.ThrowSmoke(portal.DrawPos, map, 3f);
            return portal;
        }

        static bool IsGoodSpot(IntVec3 c, Map map)
        {
            return c.Standable(map) && !c.Fogged(map) && !c.Roofed(map) &&
                c.GetFirstBuilding(map) == null && c.GetFirstItem(map) == null &&
                c.GetFirstPawn(map) == null;
        }
    }
}
