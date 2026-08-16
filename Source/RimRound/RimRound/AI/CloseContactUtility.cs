using Verse;

namespace RimRound.AI
{
    /// <summary>
    /// RimWorld only requires pawns to be within 6 cells with line of sight for a
    /// social interaction to fire. RimRound's touch interactions (fondle, smother,
    /// explore, wet smother, tail grope) should demand actual physical contact.
    /// </summary>
    public static class CloseContactUtility
    {
        public static bool InTouchRange(Pawn a, Pawn b)
        {
            if (a == null || b == null || !a.Spawned || !b.Spawned || a.Map != b.Map)
                return false;

            return a.Position == b.Position || a.Position.AdjacentTo8Way(b.Position);
        }
    }
}
