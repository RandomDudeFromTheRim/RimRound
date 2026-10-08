using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimRound.Comps
{
    public class CompProperties_BloatGeyser : CompProperties
    {
        /// <summary>Ticks for pressure to build from empty to eruption with nobody around.</summary>
        public IntRange ticksToEruption = new IntRange(5000, 10000);
        /// <summary>Pressure builds this much faster while a pawn is within preyRadius.</summary>
        public float preyPressureFactor = 2.5f;
        public float preyRadius = 7f;
        /// <summary>Last stretch of pressure where it hisses and gurgles as a warning.</summary>
        public float warningPressure = 0.9f;

        public float gasRadius = 4.9f;
        // Bloatgas is the same fattening gas as the grenades, whose sudden weight gain
        // runs 10-20 kg/s at high severity. A dense eruption added hundreds of kilos,
        // so the geyser vents a thin cloud: tens of kilos for a pawn caught in it.
        public int gasPerCell = 70;
        /// <summary>Gas leaked at the vent every check while it hisses its warning.</summary>
        public int hissGasPerPulse = 10;
        /// <summary>Pawns this close take the eruption head-on.</summary>
        public float blastRadius = 2.5f;
        public float saturationPerBlast = 0.06f;
        public float intimacyPerBlast = 0.1f;

        public SoundDef warningSound;
        public SoundDef eruptSound;

        public CompProperties_BloatGeyser()
        {
            compClass = typeof(Comp_BloatGeyser);
        }
    }

    /// <summary>
    /// A vent in the flesh dimension that builds up bloatgas (RimRound's fattening
    /// gas) and erupts. It swells faster with prey nearby, hisses as a warning, and
    /// a direct blast also feeds void saturation and, for pawns who enjoy growing,
    /// arousal.
    /// </summary>
    public class Comp_BloatGeyser : ThingComp
    {
        const int CheckIntervalTicks = 30;

        public CompProperties_BloatGeyser Props => (CompProperties_BloatGeyser)props;

        float pressure;
        int ticksToFill = -1;
        bool warned;
        bool preyNearby;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref pressure, "pressure");
            Scribe_Values.Look(ref ticksToFill, "ticksToFill", -1);
            Scribe_Values.Look(ref warned, "warned");
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || !parent.IsHashIntervalTick(CheckIntervalTicks))
                return;

            if (ticksToFill <= 0)
                ticksToFill = Props.ticksToEruption.RandomInRange;

            preyNearby = AnyPawnWithin(Props.preyRadius);
            pressure += (float)CheckIntervalTicks / ticksToFill * (preyNearby ? Props.preyPressureFactor : 1f)
                * GorgeWorld.GameCondition_FeedingSeason.PressureFactor(parent.Map);

            if (pressure >= 1f)
            {
                Erupt();
                return;
            }

            if (pressure >= Props.warningPressure)
                Hiss();
        }

        bool AnyPawnWithin(float radius)
        {
            foreach (Pawn p in parent.Map.mapPawns.AllPawnsSpawned)
            {
                if (!p.Dead && p.RaceProps.IsFlesh && p.Position.InHorDistOf(parent.Position, radius))
                    return true;
            }
            return false;
        }

        void Hiss()
        {
            Map map = parent.Map;
            if (!warned)
            {
                warned = true;
                Props.warningSound?.PlayOneShot(new TargetInfo(parent.Position, map));
            }
            FleckMaker.ThrowAirPuffUp(parent.TrueCenter(), map);
            map.GetComponent<MapComp_RRGasGrid>()?.AddGas(parent.Position, RRGasType.fatteningGas, Props.hissGasPerPulse);
        }

        public void Erupt()
        {
            Map map = parent.Map;
            IntVec3 center = parent.Position;
            var gasGrid = map.GetComponent<MapComp_RRGasGrid>();

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, Props.gasRadius, useCenter: true))
            {
                if (!cell.InBounds(map))
                    continue;
                float falloff = 1f - 0.6f * cell.DistanceTo(center) / Props.gasRadius;
                gasGrid?.AddGas(cell, RRGasType.fatteningGas, Mathf.RoundToInt(Props.gasPerCell * falloff));
            }

            for (int i = 0; i < 6; i++)
                FleckMaker.ThrowSmoke(parent.TrueCenter() + Gen.RandomHorizontalVector(1.2f), map, Rand.Range(1.5f, 2.5f));
            Props.eruptSound?.PlayOneShot(new TargetInfo(center, map));

            BlastNearbyPawns(map, center);

            pressure = 0f;
            warned = false;
            ticksToFill = Props.ticksToEruption.RandomInRange;
        }

        void BlastNearbyPawns(Map map, IntVec3 center)
        {
            List<Pawn> caught = new List<Pawn>();
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || !p.RaceProps.Humanlike || !p.Position.InHorDistOf(center, Props.blastRadius))
                    continue;
                caught.Add(p);

                // a direct blast packs in void saturation (and with it, echoes)
                var saturation = Utilities.HediffUtility.GetHediffOfDefFrom(Defs.HediffDefOf.RR_VoidSaturation, p);
                if (saturation != null)
                    saturation.Severity += Props.saturationPerBlast;

                var att = p.TryGetComp<ThingComp_PawnAttitude>();
                var intimacy = p.IntimacyNeed();
                if (intimacy != null && att != null && att.weightOpinion >= WeightOpinion.Like)
                    intimacy.CurLevelPercentage += Props.intimacyPerBlast;
            }

            if (caught.Count > 0 && caught.Exists(p => p.Faction == Faction.OfPlayer))
            {
                Messages.Message(
                    caught.Count == 1
                        ? $"{caught[0].LabelShort} is caught in a bloatgas eruption!"
                        : $"{caught.Count} pawns are caught in a bloatgas eruption!",
                    new LookTargets(caught),
                    MessageTypeDefOf.NeutralEvent);
            }
        }

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder($"Pressure: {pressure.ToStringPercent()}");
            if (pressure >= Props.warningPressure)
                sb.Append(" - about to erupt!");
            else if (preyNearby)
                sb.Append(" - swelling fast, something is close");
            return sb.ToString();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;

            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Erupt now",
                    action = Erupt
                };
            }
        }
    }
}
