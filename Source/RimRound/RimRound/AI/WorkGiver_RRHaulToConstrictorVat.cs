using System.Linq;
using RimRound.Things;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>
    /// Hauls what a constrictor vat wants: the bound constrictor chosen for it, the
    /// ingredients of the mutation it is set to grow, and food to keep its feed
    /// stocked. Modelled on vanilla's WorkGiver_HaulToGrowthVat.
    /// </summary>
    public class WorkGiver_RRHaulToConstrictorVat : WorkGiver_Scanner
    {
        const float FeedBuffer = 10f;

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDef.Named("RR_ConstrictorVat"));

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return t is Building_RRConstrictorVat vat && Usable(pawn, vat, forced) && FindWork(pawn, vat, out _, out _);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_RRConstrictorVat vat) || !FindWork(pawn, vat, out Thing thing, out int count))
                return null;
            Job job = HaulAIUtility.HaulToContainerJob(pawn, thing, vat);
            job.count = Mathf.Min(job.count, count);
            return job;
        }

        static bool Usable(Pawn pawn, Building_RRConstrictorVat vat, bool forced)
        {
            return pawn.CanReserve(vat, 1, -1, null, forced)
                && pawn.Map.designationManager.DesignationOn(vat, DesignationDefOf.Deconstruct) == null
                && !vat.IsBurning();
        }

        static bool FindWork(Pawn pawn, Building_RRConstrictorVat vat, out Thing thing, out int count)
        {
            thing = null;
            count = 0;

            // the constrictor itself first
            Thing c = vat.selectedConstrictor;
            if (c != null && vat.Constrictor == null)
            {
                if (c.Spawned && c.Map == pawn.Map && !c.IsForbidden(pawn) && pawn.CanReserveAndReach(c, PathEndMode.ClosestTouch, Danger.Deadly))
                {
                    thing = c;
                    count = 1;
                    return true;
                }
            }

            // then the mutation's ingredients
            foreach (ThingDefCountClass need in vat.MissingIngredients())
            {
                Thing found = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(need.thingDef),
                    PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, x => !x.IsForbidden(pawn) && pawn.CanReserve(x));
                if (found != null)
                {
                    thing = found;
                    count = Mathf.Min(found.stackCount, need.count);
                    return true;
                }
            }

            // then feed, once it has room for a real delivery
            float needed = vat.FeedNeeded;
            if (needed < FeedBuffer)
                return false;
            Thing food = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                x => !x.IsForbidden(pawn) && pawn.CanReserve(x) && vat.AcceptsAsFeed(x) && x.GetStatValue(StatDefOf.Nutrition) <= needed);
            if (food == null)
            {
                JobFailReason.Is("NoFood".Translate());
                return false;
            }
            thing = food;
            count = Mathf.Min(food.stackCount, Mathf.CeilToInt(needed / food.GetStatValue(StatDefOf.Nutrition)));
            return true;
        }
    }
}
