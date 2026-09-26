using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimRound.Comps
{
    /// <summary>
    /// Right-click option on feeding machines: share a feeding session with a
    /// nearby friend who also appreciates the lifestyle.
    /// </summary>
    public class Comp_SharedFeedingMenu : ThingComp
    {
        const float MinPartnerOpinion = 20f;
        const float MaxPartnerDistance = 40f;

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption o in base.CompFloatMenuOptions(selPawn))
                yield return o;

            if (!selPawn.RaceProps.Humanlike || selPawn.Drafted)
                yield break;

            // the session reserves the machine for two
            if (!selPawn.CanReserveAndReach(parent, PathEndMode.ClosestTouch, Danger.Some, 2))
                yield break;

            var att = selPawn.TryGetComp<ThingComp_PawnAttitude>();
            if (att == null || att.weightOpinion < WeightOpinion.Like)
                yield break;

            Pawn partner = FindPartner(selPawn);
            if (partner == null)
                yield break;

            yield return new FloatMenuOption(
                "Share a feeding session with " + partner.LabelShort,
                delegate
                {
                    Job job = JobMaker.MakeJob(Defs.JobDefOf.RR_SharedFeeding, parent, partner);
                    selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                });
        }

        Pawn FindPartner(Pawn initiator)
        {
            if (initiator.Map == null)
                return null;

            Pawn best = null;
            float bestScore = -1f;
            // free colonists only: a prisoner can't walk out to a machine
            foreach (Pawn p in initiator.Map.mapPawns.FreeColonistsSpawned)
            {
                if (p == initiator || p.Drafted || p.InMentalState || p.Downed || !p.Awake())
                    continue;
                if (!initiator.CanReserve(p) || !p.CanReach(parent, PathEndMode.ClosestTouch, Danger.Some))
                    continue;

                var att = p.TryGetComp<ThingComp_PawnAttitude>();
                if (att == null || att.weightOpinion < WeightOpinion.NeutralPlus)
                    continue;

                float opinion = initiator.relations?.OpinionOf(p) ?? -100f;
                if (opinion < MinPartnerOpinion)
                    continue;

                float dist = p.Position.DistanceTo(initiator.Position);
                if (dist > MaxPartnerDistance)
                    continue;

                float score = opinion - dist;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }
    }
}
