using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>
    /// RimWorld only requires pawns to be within 6 cells with line of sight for a
    /// social interaction to fire. RimRound's touch interactions (fondle, smother,
    /// explore, wet smother, tail grope) should demand actual physical contact.
    /// </summary>
    public static class CloseContactUtility
    {
        static bool IsFreeForEncounter(Pawn p)
        {
            if (p.Drafted || p.InMentalState || p.Downed || p.Dead || p.InBed())
                return false;
            // never yank someone out of an order the player gave them
            if (p.CurJob != null && p.CurJob.playerForced)
                return false;
            // already in the middle of one, on either side
            JobDef cur = p.CurJobDef;
            return cur != Defs.JobDefOf.RR_HeldInEncounter && !(p.jobs?.curDriver is JobDriver_RRCloseEncounter);
        }

        public static bool InTouchRange(Pawn a, Pawn b)
        {
            if (a == null || b == null || !a.Spawned || !b.Spawned || a.Map != b.Map)
                return false;

            return a.Position == b.Position || a.Position.AdjacentTo8Way(b.Position);
        }

        /// <summary>
        /// Gives a touch interaction a physical presence: the initiator walks up and
        /// acts it out (see <see cref="JobDriver_RRCloseEncounter"/>) while the
        /// recipient is held in place. Skips drafted/busy/mentally-broken pawns on
        /// either side, so a fondle never stops a colonist in the middle of a fight.
        /// </summary>
        public static void TryStartEncounter(Pawn initiator, Pawn recipient, JobDef act = null)
        {
            if (initiator == null || recipient == null || initiator.jobs == null || recipient.jobs == null)
                return;
            if (!IsFreeForEncounter(initiator) || !IsFreeForEncounter(recipient))
                return;
            // the job reserves the recipient; failing that inside StartJob logs an error
            if (!initiator.CanReserve(recipient))
                return;

            Job job = JobMaker.MakeJob(act ?? Defs.JobDefOf.RR_CloseEncounter, recipient);
            initiator.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        /// <summary>A spot to pin someone: a free cell against a wall near them, and the
        /// cell in front of it for the one doing the pinning.</summary>
        public static bool TryFindPinSpot(Pawn initiator, Pawn recipient, out IntVec3 holdCell, out IntVec3 wallCell, out IntVec3 standCell)
        {
            Map map = recipient.Map;
            holdCell = wallCell = standCell = IntVec3.Invalid;
            float best = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(recipient.Position, 3.9f, true))
            {
                if (!c.InBounds(map) || !c.Standable(map) || !FreeOfOthers(c, map, initiator, recipient))
                    continue;
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 w = c + GenAdj.CardinalDirections[i];
                    IntVec3 s = c - GenAdj.CardinalDirections[i];
                    if (!w.InBounds(map) || !w.Impassable(map) || !s.InBounds(map) || !s.Standable(map)
                        || !FreeOfOthers(s, map, initiator, recipient))
                        continue;
                    float d = c.DistanceToSquared(recipient.Position);
                    if (d >= best)
                        continue;
                    if (!recipient.CanReach(c, PathEndMode.OnCell, Danger.Some) || !initiator.CanReach(s, PathEndMode.OnCell, Danger.Some))
                        continue;
                    best = d;
                    holdCell = c;
                    wallCell = w;
                    standCell = s;
                }
            }
            return holdCell.IsValid;
        }

        public static bool FreeOfOthers(IntVec3 c, Map map, Pawn a, Pawn b)
        {
            foreach (Thing t in c.GetThingList(map))
                if (t is Pawn p && p != a && p != b)
                    return false;
            return true;
        }

        public static bool Welcomes(Pawn recipient) =>
            (recipient.TryGetComp<Comps.ThingComp_PawnAttitude>()?.weightOpinion ?? Utilities.WeightOpinion.Neutral)
                >= Utilities.WeightOpinion.NeutralPlus;
    }
}
