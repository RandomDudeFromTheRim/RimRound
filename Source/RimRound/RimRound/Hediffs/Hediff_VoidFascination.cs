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
        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead || !pawn.IsHashIntervalTick(180))
                return;

            // The same in-maze marker used by VoidSaturation: present only while
            // the pawn is inside the flesh dimension / void maze.
            bool inMaze =
                pawn.health?.hediffSet?.GetFirstHediffOfDef(
                    Defs.HediffDefOf.RR_VoidWarmth) != null;

            if (inMaze)
            {
                // Reaches full fascination after roughly a day of lingering.
                Severity += 0.00035f;
                Severity = Mathf.Clamp(Severity, 0f, 1f);
            }
            else
            {
                Severity -= 0.0005f;
                if (Severity <= 0f)
                    pawn.health.RemoveHediff(this);
            }
        }

        public override string TipStringExtra
        {
            get
            {
                if (pawn.health?.hediffSet?.GetFirstHediffOfDef(
                        Defs.HediffDefOf.RR_VoidWarmth) != null)
                {
                    return "The void calls to you. The longer you stay, the more it hungers.";
                }
                return "The memory of the void is fading.";
            }
        }
    }
}
