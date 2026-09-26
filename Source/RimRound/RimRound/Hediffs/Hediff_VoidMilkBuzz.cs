using RimWorld;
using Verse;

namespace RimRound.Hediffs
{
    /// <summary>
    /// The warm afterglow of voidmilk: a slow, gentle thickening while it works
    /// through the drinker. The def's severity decay sets how long it lasts, and
    /// drinking more tops it back up, so every drink adds more weight.
    /// </summary>
    public class Hediff_VoidMilkBuzz : HediffWithComps
    {
        const int PulseIntervalTicks = GenDate.TicksPerHour;
        // one full dose lasts ~0.75 days (see the def) and adds ~4 kg over that time
        const float KilosPerFullDose = 4f;
        const int FullDoseTicks = 45000;
        const float KilosPerPulse = KilosPerFullDose * PulseIntervalTicks / FullDoseTicks;

        public override void Tick()
        {
            base.Tick();
            if (pawn != null && !pawn.Dead && pawn.IsHashIntervalTick(PulseIntervalTicks))
                Utilities.HediffUtility.QueueWeightGain(pawn, KilosPerPulse);
        }
    }
}
