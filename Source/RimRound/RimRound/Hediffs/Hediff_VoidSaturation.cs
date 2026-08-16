using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Linq;
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
        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead || !pawn.IsHashIntervalTick(60))
                return;

            bool inMaze = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_VoidWarmth) != null;

            if (inMaze)
            {
                // full saturation after roughly half a day of lingering
                Severity += 0.0016f;

                if (pawn.IsHashIntervalTick(600))
                {
                    // ambient weight gain: kilograms per day, scaling with saturation
                    float kilos = 0.03f + Severity * 0.10f;
                    var fnd = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
                    if (fnd != null && !fnd.Disabled)
                        fnd.activeWeightGainRequests.Enqueue(
                            new WeightGainRequest(kilos, Find.TickManager.TicksGame + 5, 6000, false));

                    // pawns who enjoy the growth burn with want inside the void
                    var att = pawn.TryGetComp<ThingComp_PawnAttitude>();
                    if (att != null && att.weightOpinion >= WeightOpinion.Like)
                    {
                        var intimacy = pawn.needs?.AllNeeds?.FirstOrDefault(n => n.def.defName == "SEX_Intimacy");
                        if (intimacy != null)
                            intimacy.CurLevelPercentage += 0.012f + Severity * 0.02f;
                    }
                }

                if (Severity >= 1f)
                {
                    SpawnVoidEcho();
                    Severity = 0.35f; // camping longer tears another echo free
                }
            }
            else
            {
                Severity -= 0.001f;
                if (Severity <= 0f)
                    pawn.health.RemoveHediff(this);
            }
        }

        void SpawnVoidEcho()
        {
            if (!pawn.Spawned || pawn.Map == null)
                return;
            Map map = pawn.Map;

            // a real hostile faction when one exists; entities as fallback
            Faction echoFaction = Find.FactionManager.AllFactionsVisible
                .Where(f => f.def.permanentEnemy && !f.def.hidden && f != Faction.OfEntities && f.def.humanlikeFaction)
                .FirstOrDefault()
                ?? Faction.OfEntities;

            Pawn echo = PawnGenerator.GeneratePawn(pawn.kindDef, echoFaction);
            if (echo == null)
                return;

            echo.Name = new NameSingle("Echo of " + pawn.LabelShort);

            // the echo wears the pawn's own age — no fountain of youth in the void
            echo.ageTracker.AgeBiologicalTicks = pawn.ageTracker.AgeBiologicalTicks;
            echo.ageTracker.AgeChronologicalTicks = pawn.ageTracker.AgeChronologicalTicks;

            // their weight, and then some — but capped so it can still move
            float original = Utilities.HediffUtility.WeightHediff(pawn)?.Severity ?? 0f;
            float target = Mathf.Clamp(original + 0.35f, 0.35f, 1.0f);
            var echoWeight = echo.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
            if (echoWeight != null)
            {
                echoWeight.Severity = target;
            }
            else if (echo.health != null)
            {
                var h = HediffMaker.MakeHediff(Defs.HediffDefOf.RimRound_Weight, echo);
                h.Severity = target;
                echo.health.AddHediff(h);
            }

            // void vigor: the mass is carried by something other than muscle
            var vigor = HediffMaker.MakeHediff(HediffDef.Named("RR_VoidEchoVigor"), echo);
            vigor.Severity = 1f;
            if (vigor is Hediff_VoidEchoVigor vigorTyped)
                vigorTyped.markedPrey = pawn; // it wants its original back inside it
            echo.health.AddHediff(vigor);

            var att = echo.TryGetComp<ThingComp_PawnAttitude>();
            if (att != null)
                att.SetWeightOpinion(WeightOpinion.Fanatical);

            IntVec3 cell = CellFinder.RandomClosewalkCellNear(pawn.Position, map, 6);
            GenSpawn.Spawn(echo, cell, map);
            FleckMaker.ThrowSmoke(echo.Position.ToVector3Shifted(), map, 2f);

            Messages.Message(
                $"{pawn.LabelShort}'s excess mass tears free and takes shape — a void echo stalks the flesh halls!",
                new LookTargets(echo),
                MessageTypeDefOf.ThreatBig);
        }

        public override string TipStringExtra
        {
            get
            {
                float kgPerDay = (0.02f + Severity * 0.08f) * 100f;
                return $"Void saturation: {Severity * 100f:F0}%\nGaining ~{kgPerDay:F1} kg/day inside the maze.\nAt full saturation, something will tear loose...";
            }
        }
    }
}
