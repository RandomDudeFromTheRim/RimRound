using System.Collections.Generic;
using System.Linq;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimRound.Comps
{
    public class CompProperties_RRGasDiffuser : CompProperties
    {
        public RRGasType gasType = RRGasType.fatteningGas;
        /// <summary>Gas vented into the cells around it each pulse, split between them.</summary>
        public int gasPerPulse = 90;
        public int pulseIntervalTicks = 90;
        /// <summary>In ambush mode it only vents while a hostile is this close and in sight.</summary>
        public float triggerRadius = 9.9f;
        public float fuelPerPulse = 0.1f;

        public CompProperties_RRGasDiffuser()
        {
            compClass = typeof(Comp_RRGasDiffuser);
        }
    }

    /// <summary>
    /// Gluttonium diffuser: burns gluttonium to vent fattening gas into the cells
    /// around it. Set it to vent all the time (a fattening ward) or only while
    /// hostiles are in sight (a trap on a raid path). Needs power and fuel.
    /// </summary>
    public class Comp_RRGasDiffuser : ThingComp
    {
        public enum Mode : byte { Off, Always, Ambush }

        Mode mode = Mode.Ambush;
        bool ventedLastPulse;

        CompProperties_RRGasDiffuser Props => (CompProperties_RRGasDiffuser)props;
        CompPowerTrader Power => parent.GetComp<CompPowerTrader>();
        CompRefuelable Fuel => parent.GetComp<CompRefuelable>();

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref mode, "mode", Mode.Ambush);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || !parent.IsHashIntervalTick(Props.pulseIntervalTicks))
                return;
            ventedLastPulse = ShouldVent() && Vent();
        }

        bool ShouldVent()
        {
            if (mode == Mode.Off)
                return false;
            if (Power != null && !Power.PowerOn)
                return false;
            if (Fuel != null && !Fuel.HasFuel)
                return false;
            return mode == Mode.Always || HostileInSight();
        }

        bool HostileInSight()
        {
            Map map = parent.Map;
            foreach (Pawn p in map.attackTargetsCache.TargetsHostileToColony.OfType<Pawn>())
            {
                if (!p.Spawned || p.Downed || p.Dead)
                    continue;
                if (!p.Position.InHorDistOf(parent.Position, Props.triggerRadius))
                    continue;
                if (GenSight.LineOfSight(parent.Position, p.Position, map, skipFirstCell: true))
                    return true;
            }
            return false;
        }

        bool Vent()
        {
            MapComp_RRGasGrid grid = parent.Map.GetComponent<MapComp_RRGasGrid>();
            if (grid == null)
                return false;
            List<IntVec3> cells = GenAdj.CellsAdjacent8Way(parent).Where(grid.GasCanMoveTo).ToList();
            if (cells.Count == 0)
                return false;
            int each = Mathf.Max(1, Props.gasPerPulse / cells.Count);
            foreach (IntVec3 c in cells)
                grid.AddGas(c, Props.gasType, each);
            Fuel?.ConsumeFuel(Props.fuelPerPulse);
            FleckMaker.ThrowDustPuffThick(parent.TrueCenter(), parent.Map, 1.2f, new Color(0.95f, 0.6f, 0.75f));
            return true;
        }

        static readonly string[] ModeLabels = { "Off", "Always venting", "Ambush" };
        static readonly string[] ModeDescs =
        {
            "The diffuser is shut.",
            "The diffuser vents fattening gas all the time: a fattening ward, or a haze across a doorway.",
            "The diffuser only vents while a hostile is within range and in sight: a trap for raids.",
        };

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;
            if (parent.Faction != Faction.OfPlayer)
                yield break;
            yield return new Command_Action
            {
                defaultLabel = "Mode: " + ModeLabels[(int)mode],
                defaultDesc = ModeDescs[(int)mode] + "\n\nClick to change.",
                icon = ContentFinder<Texture2D>.Get(mode == Mode.Off ? "UI/Commands/DesirePower" : "UI/Commands/Attack", false) ?? BaseContent.BadTex,
                action = delegate
                {
                    mode = (Mode)(((int)mode + 1) % 3);
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                },
            };
        }

        public override string CompInspectStringExtra()
        {
            string s = "Mode: " + ModeLabels[(int)mode];
            if (mode == Mode.Ambush)
                s += ventedLastPulse ? " (venting)" : " (waiting for hostiles)";
            return s;
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (mode == Mode.Ambush)
                GenDraw.DrawRadiusRing(parent.Position, Props.triggerRadius);
        }
    }
}
