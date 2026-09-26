using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Hediffs
{
    /// <summary>
    /// Gained on entering the void maze. The void's allure builds the longer a
    /// pawn lingers inside: curiosity gives way to a ravenous, insatiable hunger
    /// (hunger-rate stages come from the def) and an escalating mood those who
    /// love growing find blissful and those who hate it find frightening.
    /// Outside the maze the fascination drains away.
    /// </summary>
    public class Hediff_VoidFascination : Hediff
    {
        const int CheckIntervalTicks = 180;
        // Reaches full fascination after roughly a day of lingering.
        const float GainPerInterval = 0.00035f;
        const float DrainPerInterval = 0.0005f;

        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead || !pawn.IsHashIntervalTick(CheckIntervalTicks))
                return;

            if (VoidMazeUtility.IsInVoidMaze(pawn))
            {
                Severity = Mathf.Clamp01(Severity + GainPerInterval);
            }
            else
            {
                Severity -= DrainPerInterval;
                if (Severity <= 0f)
                    pawn.health.RemoveHediff(this);
            }
        }

        public override string TipStringExtra =>
            VoidMazeUtility.IsInVoidMaze(pawn)
                ? "The void calls to you. The longer you stay, the more it hungers."
                : "The memory of the void is fading.";
    }
}
