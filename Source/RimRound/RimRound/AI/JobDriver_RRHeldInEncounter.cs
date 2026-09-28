using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    /// <summary>
    /// The recipient's side of a touch act: walked back to where they're wanted (a
    /// wall, for a pin), then held there - facing the initiator, or keeping their own
    /// facing when explored or groped from behind - until the initiator lets go.
    /// Their body moves with the act (see <see cref="JobDriver_RRCloseEncounter.PartnerOffset"/>).
    /// </summary>
    public class JobDriver_RRHeldInEncounter : JobDriver
    {
        Rot4 heldFacing = Rot4.South;

        public Pawn Holder => job.targetA.Thing as Pawn;

        JobDriver_RRCloseEncounter HolderDriver =>
            Holder?.jobs?.curDriver is JobDriver_RRCloseEncounter d && d.Partner == pawn ? d : null;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref heldFacing, "heldFacing", Rot4.South);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            heldFacing = pawn.Rotation;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override string GetReport() => HolderDriver?.PartnerReport(pawn) ?? base.GetReport();

        // Called from the render thread: read-only.
        public override Vector3 ForcedBodyOffset => HolderDriver?.PartnerOffset ?? Vector3.zero;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => HolderDriver == null);

            Toil hold = ToilMaker.MakeToil("RR_Held");
            if (job.targetB.IsValid)
            {
                // backed up against the wall
                Toil back = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
                back.AddPreInitAction(() => pawn.pather.StopDead());
                yield return back;
            }

            hold.defaultCompleteMode = ToilCompleteMode.Never;
            hold.handlingFacing = true;
            hold.socialMode = RandomSocialMode.Off;
            hold.initAction = () => pawn.pather.StopDead();
            hold.tickAction = delegate
            {
                var d = HolderDriver;
                if (d == null)
                    return;
                if (d.PartnerKeepsFacing)
                    pawn.Rotation = heldFacing;
                else
                    pawn.rotationTracker.Face(Holder.DrawPos);
            };
            yield return hold;
        }
    }
}
