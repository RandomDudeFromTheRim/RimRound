using RimRound.Hediffs;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Rendering
{
    /// <summary>
    /// Draws the gorge constrictor's proboscis: a tube from its head end, over the
    /// victim's shoulder, into their mouth - in world space, like the auto-feeder's
    /// feeding tube - with a gob of slurry travelling up it on every pump. The mouth
    /// is found through the renderer's head offset, which RimRound adjusts for big
    /// bodies, so it lands on the face at any size. Not drawn facing away, or while
    /// the victim is lying down.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapComponent_RRConstrictorDraw : MapComponent
    {
        static readonly Material TubeMat = MaterialPool.MatFrom("Things/Pawn/RR_GorgeConstrictor/RR_ConstrictorProboscis", ShaderDatabase.Cutout);
        static readonly Material BolusMat = MaterialPool.MatFrom("Things/Pawn/RR_GorgeConstrictor/RR_ConstrictorBolus", ShaderDatabase.Cutout);

        public MapComponent_RRConstrictorDraw(Map map) : base(map)
        {
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            if (map != Find.CurrentMap || Hediff_RRConstricted.Victims.Count == 0)
                return;

            // a burst kills without removing the hediff, and old games leave pawns behind
            if (Time.frameCount % 300 == 0)
                Hediff_RRConstricted.Victims.RemoveWhere(p => p == null || p.Dead || p.Destroyed || p.Discarded);

            foreach (Pawn pawn in Hediff_RRConstricted.Victims)
            {
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map)
                    continue;
                // skip only if the body is really drawn tipped over: RimRound draws heavy
                // downed pawns upright, so posture alone would hide the tube on them
                if (pawn.Rotation == Rot4.North || Mathf.Abs(Mathf.DeltaAngle(pawn.Drawer.renderer.BodyAngle(PawnRenderFlags.None), 0f)) > 10f)
                    continue;
                var h = pawn.health.hediffSet.GetFirstHediff<Hediff_RRConstricted>();
                if (h == null)
                    continue;
                DrawProboscis(pawn, h);
            }
        }

        static void DrawProboscis(Pawn pawn, Hediff_RRConstricted h)
        {
            Vector3 center = pawn.DrawPos;
            float y = center.y + 0.05f;                            // over the body and the face

            Vector3 from = center + ConstrictorRender.HeadEnd(pawn, h);
            Vector3 head = center + pawn.Drawer.renderer.BaseHeadOffsetAt(pawn.Rotation);
            Vector3 mouth = head + (pawn.Rotation == Rot4.South ? new Vector3(0f, 0f, -0.13f)
                : new Vector3(pawn.Rotation == Rot4.East ? 0.16f : -0.16f, 0f, -0.1f));
            from.y = mouth.y = y;

            // widens as each gulp reaches the mouth; GenDraw puts the texture's top end at the first point
            float squeeze = ConstrictorRender.Squeeze(h);
            GenDraw.DrawLineBetween(mouth, from, TubeMat, 0.11f * (1f + 0.35f * squeeze));

            // the gob: from the coils up to the mouth over each pump
            Vector3 bolus = Vector3.Lerp(from, mouth, h.PumpPhase);
            bolus.y = y + 0.001f;
            float size = 0.2f * (0.85f + 0.3f * Mathf.Sin(h.PumpPhase * Mathf.PI));
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(bolus, Quaternion.identity, new Vector3(size, 1f, size)), BolusMat, 0);
        }
    }
}
