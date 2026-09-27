using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Hediffs
{
    /// <summary>
    /// Gained by entering the void maze. While inside, the pawn slowly but steadily
    /// gains weight; saturation builds the longer they linger. At full saturation,
    /// the accumulated excess mass tears free as a bloated "void echo" of the pawn
    /// and hunts them. Outside the maze the saturation slowly drains away.
    /// </summary>
    public class Hediff_VoidSaturation : Hediff
    {
        const int CheckIntervalTicks = 60;
        // full saturation after ~10 in-game hours: inside the 18-hour maze run, so
        // a pawn who dawdles tears an echo loose before reaching the way home
        const float SaturationPerInterval = 0.0024f;
        const float SeverityAfterEcho = 0.35f; // camping longer tears another echo free

        const int GainPulseInterval = 600;
        const float BaseKilosPerPulse = 0.03f;
        const float KilosPerPulsePerSeverity = 0.10f;

        float KilosPerPulse => BaseKilosPerPulse + Severity * KilosPerPulsePerSeverity;

        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead || !pawn.IsHashIntervalTick(CheckIntervalTicks))
                return;

            if (VoidMazeUtility.IsInVoidMaze(pawn))
            {
                Severity += SaturationPerInterval;

                if (pawn.IsHashIntervalTick(GainPulseInterval))
                    ApplyAmbientGain();

                if (Severity >= 1f)
                {
                    SpawnVoidEcho();
                    Severity = SeverityAfterEcho;
                }
            }
            else
            {
                Severity -= SaturationPerInterval;
                if (Severity <= 0f)
                    pawn.health.RemoveHediff(this);
            }
        }

        void ApplyAmbientGain()
        {
            // pawns who enjoy the growth burn with want inside the void
            var att = pawn.TryGetComp<ThingComp_PawnAttitude>();
            Need intimacy = att != null && att.weightOpinion >= WeightOpinion.Like ? pawn.IntimacyNeed() : null;
            if (intimacy != null)
                intimacy.CurLevelPercentage += 0.012f + Severity * 0.02f;

            // ambient weight gain scales with saturation, and arousal sweetens
            // the flesh — the hornier, the more it takes
            float kilos = KilosPerPulse;
            if (intimacy != null)
                kilos *= 1f + Mathf.Clamp01(intimacy.CurLevelPercentage) * 0.5f;

            Utilities.HediffUtility.QueueWeightGain(pawn, kilos);
        }

        void SpawnVoidEcho()
        {
            if (!pawn.Spawned || pawn.Map == null)
                return;
            Map map = pawn.Map;

            // Anomaly's duplicator gives an exact copy: gender, age, backstories,
            // traits, looks. Being a duplicate also lets a humanlike join the
            // entities faction without Faction.Notify_PawnJoined logging an error.
            Pawn echo = Find.PawnDuplicator.Duplicate(pawn);
            if (echo == null)
                return;

            // entities faction: makes the echo capturable on Anomaly holding platforms
            echo.SetFaction(Faction.OfEntities);
            echo.Name = new NameSingle("Echo of " + pawn.LabelShort);

            // Duplicate copies hediffs too. Drop the void ones, or the echo would
            // saturate in turn and tear echoes of its own loose.
            foreach (HediffDef def in new[] { Defs.HediffDefOf.RR_VoidSaturation, Defs.HediffDefOf.RR_VoidWarmth, Defs.HediffDefOf.RR_VoidFascination })
                Utilities.HediffUtility.RemoveHediffOfDefFrom(def, echo);

            // Downed entities die outright 50-90% of the time at typical threat
            // points; an echo should go down so it can be captured.
            echo.health.overrideDeathOnDownedChance = 0f;
            WarnIfEchoesCantBeCaptured(echo);

            // their weight, and then some — but capped so it can still move
            float original = Utilities.HediffUtility.WeightHediff(pawn)?.Severity ?? 0f;
            float target = Mathf.Clamp(original + 0.35f, 0.35f, 1.0f);
            var echoWeight = Utilities.HediffUtility.WeightHediff(echo);
            if (echoWeight != null)
                echoWeight.Severity = target;
            else if (echo.health != null)
                Utilities.HediffUtility.AddHediffWithSeverity(Defs.HediffDefOf.RimRound_Weight, echo, target);

            // void vigor: the mass is carried by something other than muscle,
            // and it wants its original back inside it
            var vigor = (Hediff_VoidEchoVigor)Utilities.HediffUtility.AddHediffWithSeverity(Defs.HediffDefOf.RR_VoidEchoVigor, echo, 1f);
            vigor.markedPrey = pawn;

            echo.TryGetComp<ThingComp_PawnAttitude>()?.SetWeightOpinion(WeightOpinion.Fanatical);

            IntVec3 cell = CellFinder.RandomClosewalkCellNear(pawn.Position, map, 6);
            GenSpawn.Spawn(echo, cell, map);
            FleckMaker.ThrowSmoke(echo.Position.ToVector3Shifted(), map, 2f);

            Messages.Message(
                $"{pawn.LabelShort}'s excess mass tears free and takes shape — a void echo stalks the flesh halls!",
                new LookTargets(echo),
                MessageTypeDefOf.ThreatBig);
        }

        static bool warnedUncapturable;

        static void WarnIfEchoesCantBeCaptured(Pawn echo)
        {
            if (warnedUncapturable || !ModsConfig.AnomalyActive || echo.TryGetComp<CompHoldingPlatformTarget>() != null)
                return;
            warnedUncapturable = true;
            Log.Warning($"[RimRound] {echo.def.defName} has no CompHoldingPlatformTarget, so void echoes can't be captured on holding platforms.");
        }

        public override string TipStringExtra
        {
            get
            {
                float kgPerDay = KilosPerPulse * (GenDate.TicksPerDay / GainPulseInterval);
                return $"Void saturation: {Severity * 100f:F0}%\nGaining ~{kgPerDay:F1} kg/day inside the maze.\nAt full saturation, something will tear loose...";
            }
        }
    }
}
