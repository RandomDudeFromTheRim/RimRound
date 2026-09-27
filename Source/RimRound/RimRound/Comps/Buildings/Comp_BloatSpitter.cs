using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.Comps
{
    public class CompProperties_BloatSpitter : CompProperties
    {
        public IntRange spitIntervalTicks = new IntRange(2500, 4000);

        public CompProperties_BloatSpitter()
        {
            compClass = typeof(Comp_BloatSpitter);
        }
    }

    /// <summary>
    /// A void maze organ that spits globs of gut-bile at pawns it can see. Modeled
    /// on Anomaly's CompFleshmassSpitter, minus two things that break it in the
    /// maze: it needs no fleshmass heart, and it doesn't skip roofed targets
    /// (the whole maze is under thick roof). For the same reason its verb is a
    /// direct lob, not a mortar: thick roof swallows overhead shells.
    /// </summary>
    public class Comp_BloatSpitter : ThingComp, IAttackTargetSearcher, IVerbOwner
    {
        const int SpitCheckIntervalTicks = 180;

        public CompProperties_BloatSpitter Props => (CompProperties_BloatSpitter)props;

        int lastSpitTick = -99999;
        int nextSpitDelay = -99999;
        LocalTargetInfo lastAttackedTarget;
        VerbTracker verbTracker;

        Verb AttackVerb => AllVerbs[0];

        int TicksToNextSpit => lastSpitTick + nextSpitDelay - Find.TickManager.TicksGame;

        public Thing Thing => parent;
        public Verb CurrentEffectiveVerb => AttackVerb;
        public LocalTargetInfo LastAttackedTarget => lastAttackedTarget;
        public int LastAttackTargetTick => lastSpitTick;

        public VerbTracker VerbTracker => verbTracker ?? (verbTracker = new VerbTracker(this));
        public List<VerbProperties> VerbProperties => parent.def.Verbs;
        public List<Tool> Tools => parent.def.tools;
        public ImplementOwnerTypeDef ImplementOwnerTypeDef => ImplementOwnerTypeDefOf.NativeVerb;
        public Thing ConstantCaster => parent;
        public List<Verb> AllVerbs => VerbTracker.AllVerbs;

        public string UniqueVerbOwnerID() => "Comp_BloatSpitter_" + parent.ThingID;

        public bool VerbsStillUsableBy(Pawn p) => false;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            foreach (Verb verb in AllVerbs)
                verb.caster = parent;

            if (!respawningAfterLoad)
            {
                lastSpitTick = Find.TickManager.TicksGame;
                nextSpitDelay = Props.spitIntervalTicks.RandomInRange;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextSpitDelay, "nextSpitDelay", 0);
            Scribe_Values.Look(ref lastSpitTick, "lastSpitTick", 0);
            Scribe_TargetInfo.Look(ref lastAttackedTarget, "lastAttackedTarget");
            Scribe_Deep.Look(ref verbTracker, "verbTracker", this);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned)
                return;

            // Anomaly's spitter sprite sheet: 0 = swollen and ready, 1 = spent
            parent.overrideGraphicIndex = TicksToNextSpit > 0 ? 1 : 0;

            if (TicksToNextSpit > 0 || !parent.IsHashIntervalTick(SpitCheckIntervalTicks))
                return;

            Thing target = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(
                this, TargetScanFlags.NeedThreat, t => t is Pawn p && p.RaceProps.Humanlike);
            if (target == null)
                return;

            AttackVerb.TryStartCastOn(target);
            lastAttackedTarget = target;
            lastSpitTick = Find.TickManager.TicksGame;
            nextSpitDelay = Props.spitIntervalTicks.RandomInRange;
        }

        public override string CompInspectStringExtra()
        {
            return TicksToNextSpit > 0
                ? "Swelling with bile: " + TicksToNextSpit.ToStringTicksToPeriod()
                : "Full of bile, ready to spit.";
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;

            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Ready to spit",
                    action = () => nextSpitDelay = lastSpitTick = -99999
                };
            }
        }
    }
}
