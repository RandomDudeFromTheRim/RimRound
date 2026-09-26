using RimRound.Hediffs;
using RimRound.Utilities;
using System.Linq;
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

        bool MergeTargetStillReachable(Pawn pawn, Pawn target)
        {
            return target != null && target.Spawned && !target.Dead && target.Map == pawn.Map
                && (pawn.Position - target.Position).LengthHorizontal <= Props.mergeRange;
        }

        /// <summary>Fraction of the pawn's body parts not covered by any worn apparel.</summary>
        static float UncoveredBodyFraction(Pawn p)
        {
            if (p?.apparel == null || p.RaceProps?.body == null)
                return 1f;

            var parts = p.RaceProps.body.AllParts;
            if (parts.Count == 0)
                return 1f;

            int uncovered = parts.Count(part => !part.groups.Any(g => p.apparel.BodyPartGroupIsCovered(g)));
            return uncovered / (float)parts.Count;
        }

        /// <summary>Sum of every body part's max HP, scaled by the pawn's health scale.</summary>
        static float MaxBodyHitPoints(Pawn p)
        {
            return p.RaceProps.body.AllParts.Sum(part => (float)part.def.hitPoints) * p.HealthScale;
        }

        /// <summary>Stuns the pawn unless it is already stunned, so an existing longer stun isn't cut short.</summary>
        static void StunIfNotStunned(Pawn p, int ticks, Pawn instigator)
        {
            var stunner = p.stances?.stunner;
            if (stunner != null && !stunner.Stunned)
                stunner.StunFor(ticks, instigator, addBattleLog: false, showMote: false);
        }

        int tickCounter = 0;
        Pawn lastTarget = null;

        public override void CompTick()
        {
            base.CompTick();
            if (parent is not Pawn pawn || !pawn.Spawned || pawn.Dead)
                return;

            // Once a merge has begun, stay committed to that target even if the AI
            // re-targets or panic-flees, so the beast cannot wander off mid-meld.
            Pawn target;
            if (MergeTargetStillReachable(pawn, lastTarget))
            {
                target = lastTarget;
            }
            else if (pawn.Downed)
            {
                target = null;
            }
            else
            {
                target = pawn.mindState?.enemyTarget as Pawn;
            }

            if (target is null || !target.Spawned || target.Dead)
            {
                lastTarget = null;
                tickCounter = 0;
                return;
            }

            float dist = (pawn.Position - target.Position).LengthHorizontal;
            if (dist > Props.mergeRange)
            {
                lastTarget = null;
                tickCounter = 0;
                return;
            }

            tickCounter++;
            if (tickCounter < Props.tickInterval)
            {
                // Keep target immobilized between merge ticks
                StunIfNotStunned(target, Props.tickInterval + 60, pawn);
                return;
            }
            tickCounter = 0;

            // Immobilize target for merge duration
            target.stances?.stunner?.StunFor(Props.stunDurationTicks, pawn, addBattleLog: false, showMote: false);
            target.jobs?.EndCurrentJob(JobCondition.InterruptForced);

            // Apply meld growth to target — scaled by uncovered skin; a fully
            // covered pawn has nothing for the meld to sink into
            float exposure = UncoveredBodyFraction(target);
            if (exposure > 0.05f)
            {
                float dose = Props.meldPerHit * exposure;
                Hediff existing = target.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth);
                if (existing is Hediff_MeldGrowth meldGrowth)
                {
                    meldGrowth.AddContribution(dose, pawn.BodySize);
                }
                else
                {
                    Hediff_MeldGrowth meld = (Hediff_MeldGrowth)HediffMaker.MakeHediff(Defs.HediffDefOf.RR_MeldGrowth, target);
                    meld.AddContribution(dose, pawn.BodySize);
                    target.health.AddHediff(meld);
                }
            }
            else if (lastTarget != target)
            {
                Messages.Message(
                    $"{pawn.LabelShort}'s melding grasp slides off {target.LabelShort}'s coverings!",
                    new LookTargets(new Pawn[] { pawn, target }),
                    MessageTypeDefOf.SilentInput);
            }

            // Self-damage as exchange: a fixed fraction of total body HP per merge,
            // so any fleshbeast dies after roughly ten merges regardless of species
            float selfDamage = Mathf.Max(1f, MaxBodyHitPoints(pawn) * 0.10f * Props.selfDamageMultiplier);
            pawn.TakeDamage(new DamageInfo(
                DamageDefOf.Cut,
                selfDamage,
                instigator: pawn));

            // Visual
            FleckMaker.ThrowSmoke((pawn.Position + target.Position).ToVector3Shifted() / 2f, pawn.Map, 1.5f);

            // The beast braces itself against its victim for the next pulse —
            // a true grapple that only ends when one of them drops
            if (!pawn.Downed)
                StunIfNotStunned(pawn, Props.tickInterval + 60, pawn);

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
