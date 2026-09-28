using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    public enum RRCloseAct { Press, Fondle, Pin, Explore, Nurse, TailGrope }

    /// <summary>Tells <see cref="JobDriver_RRCloseEncounter"/> which act a JobDef plays out.</summary>
    public class RRCloseActExtension : DefModExtension
    {
        public RRCloseAct act = RRCloseAct.Press;
        public int duration = 360;
    }

    /// <summary>
    /// The physical layer for RimRound's touch interactions. The initiator walks up,
    /// the recipient is held in place (<see cref="JobDriver_RRHeldInEncounter"/>), and
    /// the act plays out as what it is:
    /// fondle - leaning in and kneading, the recipient jiggling under their hands;
    /// pin - the recipient is backed into the nearest wall and the initiator leans
    ///   their whole bulk into them;
    /// explore - the initiator circles the recipient, stopping to feel each side;
    /// nurse - the recipient is pulled face-first into the initiator's chest, gulping;
    /// tail grope - the initiator steps behind the recipient and strokes along the tail.
    /// The social/weight effects still fire from the InteractionWorker.
    /// </summary>
    public class JobDriver_RRCloseEncounter : JobDriver
    {
        const int ExploreRounds = 3;

        int touchStartTick = -1;
        bool seized, touching, welcomed;
        IntVec3 holdCell = IntVec3.Invalid;
        IntVec3 wallCell = IntVec3.Invalid;

        RRCloseActExtension Ext => job.def.GetModExtension<RRCloseActExtension>();
        public RRCloseAct Act => Ext?.act ?? RRCloseAct.Press;
        int Duration => Ext?.duration ?? 360;
        public Pawn Partner => job.targetA.Thing as Pawn;
        public IntVec3 HoldCell => holdCell;

        /// <summary>Touching right now: offsets and flecks only play while standing at the partner.</summary>
        bool Touching => touching && !pawn.pather.Moving;
        int TouchTicks => Find.TickManager.TicksGame - touchStartTick;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref touchStartTick, "touchStartTick", -1);
            Scribe_Values.Look(ref seized, "seized");
            Scribe_Values.Look(ref touching, "touching");
            Scribe_Values.Look(ref welcomed, "welcomed");
            Scribe_Values.Look(ref holdCell, "holdCell", IntVec3.Invalid);
            Scribe_Values.Look(ref wallCell, "wallCell", IntVec3.Invalid);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !(TargetThingA is Pawn p) || p.Dead || p.Downed || p.Drafted || p.InAggroMentalState);
            // once the partner is held, they stay held for as long as this lasts
            this.FailOn(() => seized && !(Partner.jobs?.curDriver is JobDriver_RRHeldInEncounter held && held.Holder == pawn));
            AddFinishAction(delegate { ReleasePartner(); });

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(Seize);

            Toil press = MakeTouchToil(Duration);

            switch (Act)
            {
                case RRCloseAct.Pin:
                    // step in front of the spot, then wait for them to be backed up against it
                    yield return Toils_Jump.JumpIf(press, () => !job.targetB.IsValid);
                    yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
                    yield return WaitForPartnerAt();
                    break;
                case RRCloseAct.TailGrope:
                    yield return Toils_Jump.JumpIf(press, () => !job.targetB.IsValid);
                    yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
                    break;
                case RRCloseAct.Explore:
                    // feel them from one side, then work around to the next
                    for (int i = 0; i < ExploreRounds - 1; i++)
                    {
                        Toil touch = MakeTouchToil(Duration);
                        Toil pick = Toils_General.Do(PickNextExploreCell);
                        yield return touch;
                        yield return pick;
                        yield return Toils_Jump.JumpIf(press, () => !job.targetB.IsValid);
                        yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
                    }
                    break;
            }
            yield return press;
        }

        void Seize()
        {
            Pawn p = Partner;
            welcomed = CloseContactUtility.Welcomes(p);
            job.targetB = LocalTargetInfo.Invalid;

            if (Act == RRCloseAct.Pin && CloseContactUtility.TryFindPinSpot(pawn, p, out IntVec3 hold, out IntVec3 wall, out IntVec3 stand))
            {
                holdCell = hold;
                wallCell = wall;
                job.targetB = stand;
            }
            else if (Act == RRCloseAct.TailGrope)
            {
                IntVec3 behind = p.Position - p.Rotation.FacingCell;
                if (behind.InBounds(p.Map) && behind.Standable(p.Map) && CloseContactUtility.FreeOfOthers(behind, p.Map, pawn, p)
                    && pawn.CanReach(behind, PathEndMode.OnCell, Danger.Some))
                    job.targetB = behind;
            }

            Job held = JobMaker.MakeJob(Defs.JobDefOf.RR_HeldInEncounter, pawn, holdCell.IsValid ? (LocalTargetInfo)holdCell : LocalTargetInfo.Invalid);
            p.jobs.StartJob(held, JobCondition.InterruptForced);
            // from here on the partner must stay held (see the FailOn above)
            seized = true;
        }

        Toil WaitForPartnerAt()
        {
            Toil wait = ToilMaker.MakeToil("RR_WaitForPinned");
            wait.defaultCompleteMode = ToilCompleteMode.Delay;
            wait.defaultDuration = 300;
            wait.handlingFacing = true;
            wait.tickAction = delegate
            {
                pawn.rotationTracker.Face(Partner.DrawPos);
                if (Partner.Position == holdCell && !Partner.pather.Moving)
                    ReadyForNextToil();
            };
            return wait;
        }

        Toil MakeTouchToil(int duration)
        {
            Toil touch = ToilMaker.MakeToil("RR_Touch");
            touch.defaultCompleteMode = ToilCompleteMode.Delay;
            touch.defaultDuration = duration;
            touch.handlingFacing = true;
            touch.socialMode = RandomSocialMode.Off;
            touch.initAction = delegate
            {
                touchStartTick = Find.TickManager.TicksGame;
                touching = true;
                pawn.rotationTracker.Face(Partner.DrawPos);
                if (Act == RRCloseAct.Pin && wallCell.IsValid)
                {
                    // the thump of them hitting the wall
                    Vector3 contact = (holdCell.ToVector3Shifted() + wallCell.ToVector3Shifted()) / 2f;
                    for (int i = 0; i < 3; i++)
                        FleckMaker.ThrowDustPuffThick(contact, pawn.Map, 1.1f, new Color(0.8f, 0.78f, 0.72f));
                }
            };
            touch.tickAction = delegate
            {
                if (!Partner.Spawned)
                    return;
                pawn.rotationTracker.Face(Partner.DrawPos);
                TouchFlecks();
            };
            return touch;
        }

        void TouchFlecks()
        {
            Map map = pawn.Map;
            Pawn p = Partner;
            int t = TouchTicks;
            switch (Act)
            {
                case RRCloseAct.Fondle:
                case RRCloseAct.TailGrope:
                    if (welcomed && t % 90 == 45)
                        FleckMaker.ThrowMetaIcon(p.Position, map, FleckDefOf.Heart);
                    break;
                case RRCloseAct.Explore:
                    if (t % 75 == 40)
                        FleckMaker.ThrowMetaIcon(pawn.Position, map, FleckDefOf.Heart);
                    break;
                case RRCloseAct.Pin:
                    if (t % 120 == 60)
                        FleckMaker.ThrowDustPuff(p.DrawPos, map, 0.6f);
                    if (welcomed && t % 150 == 100)
                        FleckMaker.ThrowMetaIcon(p.Position, map, FleckDefOf.Heart);
                    break;
                case RRCloseAct.Nurse:
                    if (t % 45 == 20)
                        CloseContactFlecks.Droplet(Vector3.Lerp(pawn.DrawPos, p.DrawPos, 0.5f) + new Vector3(0f, 0f, 0.2f), map, Color.white);
                    if (welcomed && t % 160 == 80)
                        FleckMaker.ThrowMetaIcon(p.Position, map, FleckDefOf.Heart);
                    break;
                default:
                    if (t % 120 == 60)
                        FleckMaker.ThrowSmoke(p.DrawPos, map, 0.8f);
                    break;
            }
        }

        void PickNextExploreCell()
        {
            touching = false;
            job.targetB = LocalTargetInfo.Invalid;
            Pawn p = Partner;
            Map map = pawn.Map;
            float here = (pawn.Position - p.Position).AngleFlat;
            float bestTurn = float.MaxValue;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(p))
            {
                if (c == pawn.Position || !c.InBounds(map) || !c.Standable(map) || !CloseContactUtility.FreeOfOthers(c, map, pawn, p))
                    continue;
                // clockwise, at least a quarter of the way round, so each stop is a new side
                float turn = Mathf.Repeat((c - p.Position).AngleFlat - here, 360f);
                if (turn < 80f || turn >= bestTurn || !pawn.CanReach(c, PathEndMode.OnCell, Danger.Some))
                    continue;
                bestTurn = turn;
                job.targetB = c;
            }
        }

        void ReleasePartner()
        {
            Pawn p = Partner;
            if (p?.jobs?.curDriver is JobDriver_RRHeldInEncounter held && held.Holder == pawn)
                p.jobs.EndCurrentJob(JobCondition.Succeeded);
        }

        /// <summary>Unit vector on the ground from this pawn toward the partner.</summary>
        Vector3 TowardPartner()
        {
            Pawn p = Partner;
            if (p == null)
                return Vector3.zero;
            Vector3 d = p.Position.ToVector3Shifted() - pawn.Position.ToVector3Shifted();
            d.y = 0f;
            return d.sqrMagnitude < 0.01f ? Vector3.zero : d.normalized;
        }

        // Called from the render thread: read-only.
        public override Vector3 ForcedBodyOffset
        {
            get
            {
                if (!Touching)
                    return Vector3.zero;
                Vector3 d = TowardPartner();
                Vector3 side = new Vector3(d.z, 0f, -d.x);
                float t = TouchTicks;
                switch (Act)
                {
                    case RRCloseAct.Fondle:
                        // leaning in, hands kneading in a slow rhythm
                        return d * (0.2f + 0.06f * Mathf.Sin(t * 0.22f));
                    case RRCloseAct.Pin:
                        // their whole bulk pressed in, heaving with each breath; drawn over the one pinned
                        return d * (0.42f + 0.04f * Mathf.Sin(t * 0.08f)) + new Vector3(0f, 0.012f, 0f);
                    case RRCloseAct.Explore:
                        return d * (0.26f + 0.04f * Mathf.Sin(t * 0.2f)) + side * (0.08f * Mathf.Sin(t * 0.07f));
                    case RRCloseAct.Nurse:
                        return d * (0.3f + 0.02f * Mathf.Sin(t * 0.05f)) + new Vector3(0f, 0.012f, 0f);
                    case RRCloseAct.TailGrope:
                        // stroking along the length of the tail
                        return d * 0.24f + side * (0.1f * Mathf.Sin(t * 0.12f));
                    default:
                        return d * 0.3f;
                }
            }
        }

        /// <summary>How the one being held moves: pressed into the wall, jiggling, gulping...
        /// Called from the render thread: read-only.</summary>
        public Vector3 PartnerOffset
        {
            get
            {
                if (!Touching)
                    return Vector3.zero;
                Vector3 d = TowardPartner();
                Vector3 side = new Vector3(d.z, 0f, -d.x);
                float t = TouchTicks;
                switch (Act)
                {
                    case RRCloseAct.Fondle:
                    case RRCloseAct.Explore:
                        // a soft jiggle each time the hands squeeze
                        return new Vector3(0f, 0f, 0.025f * Mathf.Abs(Mathf.Sin(t * 0.22f)));
                    case RRCloseAct.Pin:
                        // squashed back against the wall
                        return d * (0.16f + 0.02f * Mathf.Sin(t * 0.08f));
                    case RRCloseAct.Nurse:
                        // face buried, a little gulp every second
                        return -d * 0.22f + new Vector3(0f, 0f, t % 60 < 8 ? -0.03f : 0f);
                    case RRCloseAct.TailGrope:
                        // the tail flicks
                        return side * (0.03f * Mathf.Sin(t * 0.3f));
                    default:
                        return Vector3.zero;
                }
            }
        }

        /// <summary>Whether the one held should keep facing their own way (explored from all
        /// sides, or groped from behind) instead of turning to face the initiator.</summary>
        public bool PartnerKeepsFacing => Act == RRCloseAct.Explore || Act == RRCloseAct.TailGrope;

        public string PartnerReport(Pawn held)
        {
            string who = pawn.LabelShort;
            switch (Act)
            {
                case RRCloseAct.Fondle: return $"being fondled by {who}.";
                case RRCloseAct.Pin: return wallCell.IsValid ? $"pinned against the wall by {who}." : $"pinned under {who}'s bulk.";
                case RRCloseAct.Explore: return $"having their curves explored by {who}.";
                case RRCloseAct.Nurse: return $"held to {who}'s chest.";
                case RRCloseAct.TailGrope: return $"having their tail stroked by {who}.";
                default: return $"held close by {who}.";
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class CloseContactFlecks
    {
        static readonly FleckDef droplet = DefDatabase<FleckDef>.GetNamedSilentFail("RR_Fleck_Droplet");

        public static void Droplet(Vector3 at, Map map, Color color)
        {
            if (droplet == null || map == null || !at.ShouldSpawnMotesAt(map))
                return;
            FleckCreationData data = FleckMaker.GetDataStatic(at + new Vector3(Rand.Range(-0.1f, 0.1f), 0f, 0f), map, droplet, Rand.Range(0.5f, 0.8f));
            data.instanceColor = color;
            data.rotationRate = 0f;
            data.velocityAngle = Rand.Range(160f, 200f);
            data.velocitySpeed = Rand.Range(0.25f, 0.45f);
            map.flecks.CreateFleck(data);
        }
    }
}
