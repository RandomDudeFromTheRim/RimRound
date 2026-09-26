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

            if (map.listerThings.ThingsOfDef(Defs.ThingDefOf.RR_VoidPortal).Any())
                return false;

            return map.mapPawns.FreeColonistsSpawned.Any(p =>
                BodyTypeUtility.PawnIsOverWeightThreshold(p, Defs.BodyTypeDefOf.F_006_Chonky));
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (!RCellFinder.TryFindRandomCellNearTheCenterOfTheMapWith(c => IsGoodSpot(c, map), map, out IntVec3 cell))
                return false;

            Thing portal = GenSpawn.Spawn(ThingMaker.MakeThing(Defs.ThingDefOf.RR_VoidPortal), cell, map);
            FleckMaker.ThrowSmoke(portal.DrawPos, map, 3f);
            SendStandardLetter(parms, new LookTargets(portal));
            return true;
        }

        static bool IsGoodSpot(IntVec3 c, Map map)
        {
            return c.Standable(map) && !c.Fogged(map) && !c.Roofed(map) &&
                c.GetFirstBuilding(map) == null && c.GetFirstItem(map) == null &&
                c.GetFirstPawn(map) == null;
        }
    }
}
