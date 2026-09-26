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
            return !p.Drafted && !p.InMentalState && !p.Downed && !p.Dead;
        }

        public static bool InTouchRange(Pawn a, Pawn b)
        {
            if (a == null || b == null || !a.Spawned || !b.Spawned || a.Map != b.Map)
                return false;

            return a.Position == b.Position || a.Position.AdjacentTo8Way(b.Position);
        }

        /// <summary>
        /// Gives a touch interaction a physical presence: the initiator walks up and
        /// pins the recipient for a few seconds instead of the interaction being pure
        /// flavor text. Skips drafted/busy/mentally-broken pawns on either side, so a
        /// fondle never stuns a colonist in the middle of a fight.
        /// </summary>
        public static void TryStartEncounter(Pawn initiator, Pawn recipient)
        {
            if (initiator == null || recipient == null || initiator.jobs == null)
                return;
            if (!IsFreeForEncounter(initiator) || !IsFreeForEncounter(recipient))
                return;
            if (initiator.CurJobDef == Defs.JobDefOf.RR_CloseEncounter)
                return;
            // the job reserves the recipient; failing that inside StartJob logs an error
            if (!initiator.CanReserve(recipient))
                return;

            Job job = JobMaker.MakeJob(Defs.JobDefOf.RR_CloseEncounter, recipient);
            initiator.jobs.StartJob(job, JobCondition.InterruptForced);
        }
    }
}
