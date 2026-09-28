using RimWorld;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace RimRound.SweetSlime
{
    /// <summary>
    /// A coat of sweet slime drawn over a pawn, the way firefoam coats them: vanilla's
    /// foam overlay textures, masked to the body and head, tinted a glossy pink.
    /// </summary>
    public class PawnSlimeDrawer : PawnOverlayDrawer
    {
        public static readonly Color SlimeColor = new Color(0.93f, 0.52f, 0.72f, 0.95f);

        static readonly string[] TexturePaths =
        {
            "Things/Pawn/Overlays/Firefoam/FireFoamOverlayA", "Things/Pawn/Overlays/Firefoam/FireFoamOverlayB",
            "Things/Pawn/Overlays/Firefoam/FireFoamOverlayC", "Things/Pawn/Overlays/Firefoam/FireFoamOverlayD"
        };

        static readonly ConditionalWeakTable<Pawn, PawnSlimeDrawer> drawers = new ConditionalWeakTable<Pawn, PawnSlimeDrawer>();

        public bool coated;

        public PawnSlimeDrawer(Pawn pawn) : base(pawn) { }

        public static PawnSlimeDrawer For(Pawn pawn) => drawers.GetValue(pawn, p => new PawnSlimeDrawer(p));

        public static bool IsCoated(Pawn pawn) => drawers.TryGetValue(pawn, out var d) && d.coated;

        protected override void WriteCache(CacheKey key, PawnDrawParms parms, List<DrawCall> writeTarget)
        {
            Rot4 rot = key.pawnRot;
            Graphic graphic = key.layer == OverlayLayer.Body ? pawn.Drawer.renderer.BodyGraphic : pawn.Drawer.renderer.HeadGraphic;
            if (graphic == null)
                return;
            // a different seed from the firefoam drawer, so the drips don't line up with foam
            Rand.PushState(pawn.thingIDNumber * 7 + (int)key.layer * 13 + 5);
            try
            {
                bool flip = (graphic.EastFlipped && rot == Rot4.East) || (graphic.WestFlipped && rot == Rot4.West);
                int i = (Rand.Range(0, TexturePaths.Length) + rot.AsInt) % TexturePaths.Length;
                Material basis = MaterialPool.MatFrom(TexturePaths[i], ShaderDatabase.FirefoamOverlay, Color.white);
                Mesh mesh = flip ? MeshPool.GridPlaneFlip(Vector2.one * 0.25f) : MeshPool.GridPlane(Vector2.one * 0.25f);
                Vector3 size = key.bodyMesh.bounds.size;
                float scale = size.magnitude * PawnFirefoamDrawer.TextureScaleFactor;
                Material mat = MaterialPool.MatFrom(new MaterialRequest
                {
                    maskTex = (Texture2D)graphic.MatAt(rot).mainTexture,
                    mainTex = basis.mainTexture,
                    color = SlimeColor,
                    shader = basis.shader
                });
                Vector3 offset = Rand.InsideUnitCircleVec3 * PawnFirefoamDrawer.TextureOffsetVecMagnitude;
                Vector3 meshSize = mesh.bounds.size * scale;
                writeTarget.Add(new DrawCall
                {
                    overlayMat = mat,
                    matrix = Matrix4x4.Scale(Vector3.one * scale),
                    overlayMesh = mesh,
                    displayOverApparel = true,
                    colorOverride = SlimeColor,
                    maskTexScale = new Vector4(meshSize.x / size.x, meshSize.z / size.z),
                    mainTexScale = new Vector4(PawnFirefoamDrawer.TextureTiles, PawnFirefoamDrawer.TextureTiles, 1f, 1f),
                    mainTexOffset = new Vector4(offset.x, offset.z)
                });
            }
            finally
            {
                Rand.PopState();
            }
        }
    }

    /// <summary>Draws <see cref="PawnSlimeDrawer"/>: inserted next to each Firefoam overlay node.</summary>
    public class PawnRenderNodeWorker_OverlayRRSlime : PawnRenderNodeWorker_Overlay
    {
        protected override PawnOverlayDrawer OverlayDrawer(Pawn pawn) => PawnSlimeDrawer.For(pawn);

        public override bool ShouldListOnGraph(PawnRenderNode node, PawnDrawParms parms) => PawnSlimeDrawer.IsCoated(parms.pawn);

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms) =>
            base.CanDrawNow(node, parms) && parms.rotDrawMode == RotDrawMode.Fresh && PawnSlimeDrawer.IsCoated(parms.pawn);
    }

    /// <summary>
    /// Covered in sweet slime: sticky (a little slower), fireproof like foam, and drawn
    /// with a pink coat until it dries off.
    /// </summary>
    public class Hediff_RRSlimeCoated : HediffWithComps
    {
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            SetCoated(true);
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            SetCoated(pawn.health.hediffSet.HasHediff(def));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                SetCoated(true);
        }

        void SetCoated(bool on)
        {
            var d = PawnSlimeDrawer.For(pawn);
            if (d.coated == on)
                return;
            d.coated = on;
            d.ClearCache();
            // the node only joins the render tree when the tree is rebuilt
            if (pawn.Spawned)
                pawn.Drawer.renderer.SetAllGraphicsDirty();
        }
    }

    public static class SlimeCoatUtility
    {
        public static void Coat(Pawn p, int ticks = -1)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("RR_SlimeCoated");
            if (def == null || p == null || p.Dead)
                return;
            Hediff existing = p.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null)
            {
                // a fresh coat: back to the full drying time
                var dis = existing.TryGetComp<HediffComp_Disappears>();
                if (dis != null)
                {
                    if (ticks > dis.ticksToDisappear)
                        dis.SetDuration(ticks);
                    else if (ticks <= 0)
                        dis.ResetElapsedTicks();
                }
                return;
            }
            Hediff h = HediffMaker.MakeHediff(def, p);
            if (ticks > 0)
                h.TryGetComp<HediffComp_Disappears>()?.SetDuration(ticks);
            p.health.AddHediff(h);
        }
    }

    /// <summary>
    /// Sweet-slime foam: puts fires out like firefoam, coats everyone caught in it in
    /// sticky pink slime, and gives the ones who can swell a light puff of weight that
    /// wears off. Harmless otherwise.
    /// </summary>
    public class DamageWorker_RRSlimeFoam : DamageWorker
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            var result = new DamageResult();
            Fire fire = victim as Fire ?? victim?.GetAttachment(ThingDefOf.Fire) as Fire;
            if (fire != null && !fire.Destroyed)
                fire.Destroy();

            if (victim is Pawn p && !p.Dead)
            {
                SlimeCoatUtility.Coat(p, 20000);
                if (p.RaceProps.Humanlike && Utilities.HediffUtility.WeightHediff(p) != null)
                    Utilities.HediffUtility.QueueWeightGain(p, Rand.Range(3f, 6f), 30000);
            }
            return result;
        }
    }
}
