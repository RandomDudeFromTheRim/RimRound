using RimRound.Hediffs;
using RimRound.Utilities;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.Comps
{
    public class CompProperties_MeldFleshbeast : CompProperties
    {
        public float meldPerHit = 0.15f;
        public float selfDamageMultiplier = 1f;
        public int tickInterval = 180;
        public int stunDurationTicks = 90;
        public float mergeRange = 3f;

        public CompProperties_MeldFleshbeast()
        {
            compClass = typeof(Comp_MeldFleshbeast);
        }
    }

    public class Comp_MeldFleshbeast : ThingComp
    {
        public CompProperties_MeldFleshbeast Props => (CompProperties_MeldFleshbeast)props;

        int tickCounter = 0;
        Pawn lastTarget = null;

        public override void CompTick()
        {
            base.CompTick();
            if (parent is not Pawn pawn || !pawn.Spawned || pawn.Dead)
                return;

            // When downed, continue merging with last target instead of stopping
            Pawn target;
            if (pawn.Downed)
            {
                target = lastTarget;
            }
            else
            {
                target = pawn.mindState?.enemyTarget as Pawn;
            }

            if (target is null || !target.Spawned || target.Dead)
            {
                lastTarget = null;
                return;
            }

            float dist = (pawn.Position - target.Position).LengthHorizontal;
            if (dist > Props.mergeRange && !pawn.Downed)
            {
                lastTarget = null;
                return;
            }

            tickCounter++;
            if (tickCounter < Props.tickInterval)
            {
                // Keep target immobilized between merge ticks
                if (!target.stances.stunner.Stunned)
                    target.stances.stunner.StunFor(Props.tickInterval + 60, pawn, addBattleLog: false, showMote: false);
                return;
            }
            tickCounter = 0;

            // Immobilize target for merge duration
            target.stances.stunner.StunFor(Props.stunDurationTicks, pawn, addBattleLog: false, showMote: false);
            target.jobs.EndCurrentJob(JobCondition.InterruptForced);

            // Apply meld growth to target — pooled by contribution body size
            Hediff existing = target.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth);
            if (existing is Hediff_MeldGrowth meldGrowth)
            {
                meldGrowth.AddContribution(Props.meldPerHit, pawn.BodySize);
            }
            else
            {
                Hediff_MeldGrowth meld = (Hediff_MeldGrowth)HediffMaker.MakeHediff(Defs.HediffDefOf.RR_MeldGrowth, target);
                meld.AddContribution(Props.meldPerHit, pawn.BodySize);
                target.health.AddHediff(meld);
            }

            // Self-damage as exchange
            int selfDamage = Mathf.Max(1, (int)(Props.meldPerHit * Props.selfDamageMultiplier * 5f));
            pawn.TakeDamage(new DamageInfo(
                DamageDefOf.Cut,
                selfDamage,
                instigator: pawn));

            // Visual
            FleckMaker.ThrowSmoke((pawn.Position + target.Position).ToVector3Shifted() / 2f, pawn.Map, 1.5f);

            if (lastTarget != target)
            {
                Messages.Message(
                    $"{pawn.LabelShort} presses its pulsing body against {target.LabelShort}, " +
                    $"flesh beginning to MERGE!",
                    new LookTargets(new Pawn[] { pawn, target }),
                    MessageTypeDefOf.NegativeEvent);
                lastTarget = target;
            }
        }
    }
}
