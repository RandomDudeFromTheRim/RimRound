using RimRound.Hediffs;
using RimRound.Utilities;
using UnityEngine;
using Verse;

namespace RimRound.Rendering
{
    /// <summary>A render node drawn from a single texture, the same from every side (the default is Graphic_Multi).</summary>
    public class PawnRenderNode_RRSingle : PawnRenderNode
    {
        public PawnRenderNode_RRSingle(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree) : base(pawn, props, tree)
        {
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            string path = TexPathFor(pawn);
            Shader shader = ShaderFor(pawn);
            if (path.NullOrEmpty() || shader == null)
                return null;
            return GraphicDatabase.Get<Graphic_Single>(path, shader, Vector2.one, ColorFor(pawn));
        }
    }

    /// <summary>
    /// Where the gorge constrictor sits on its victim. RimRound scales each body's
    /// mesh by body type, and each body sprite carries its torso at a different
    /// height and width (ConstrictorBodyFit, measured off the default body set), so
    /// the coils are placed and sized per body type to wrap the belly.
    /// </summary>
    public static class ConstrictorRender
    {
        // In the coil sprite, the loops span about 62% of the canvas and sit 7% below its centre.
        const float CoilLoopSpan = 0.62f;
        const float CoilLoopDrop = 0.07f;
        const float Hug = 1.08f;

        public static Hediff_RRConstricted Hediff(PawnRenderNode node) => node.hediff as Hediff_RRConstricted;

        public static float MeshSize(Pawn pawn)
        {
            BodyTypeInfo? info = RacialBodyTypeInfoUtility.GetRacialBodyTypeInfo(pawn);
            return info.HasValue ? Mathf.Max(0.5f, info.Value.meshSize) : 1f;
        }

        /// <summary>World size of the pawn's body sprite canvas (humanlike bodies are drawn 1.5 cells across, times RimRound's mesh size).</summary>
        static float BodyCanvas(Pawn pawn) => 1.5f * MeshSize(pawn);

        static ConstrictorBodyFit.Fit Fit(Pawn pawn) => ConstrictorBodyFit.For(pawn.story?.bodyType?.defName);

        /// <summary>World size of the coil sprite's canvas, so its loops hug the torso.</summary>
        public static float CoilCanvas(Pawn pawn) => Fit(pawn).width * BodyCanvas(pawn) * Hug / CoilLoopSpan;

        /// <summary>How far up from the pawn's centre the coil's canvas centre goes, so its loops sit on the torso.</summary>
        public static float CoilLift(Pawn pawn) => Fit(pawn).z * BodyCanvas(pawn) + CoilLoopDrop * CoilCanvas(pawn);

        /// <summary>Where the coil's loops cross the belly, up from the pawn's centre.</summary>
        public static float BellyZ(Pawn pawn) => Fit(pawn).z * BodyCanvas(pawn);

        // --- the leech riding behind them: the same body as when it hunts, sized by what it still holds ---

        // In the side-on riding sprite the body spans 74% of the canvas (as on the loose
        // constrictor), the mouth sits at (0.37, 0.10) and the monitor at (0.06, -0.01)
        // (canvas fractions, x right, y down).
        const float SacBodySpan = 0.74f;
        static readonly Vector2 MawInSprite = new Vector2(0.37f, 0.10f);
        static readonly Vector2 FaceInSprite = new Vector2(0.06f, -0.01f);
        /// <summary>Where its mouth hangs, from the victim's head: behind and below it, by the shoulder.</summary>
        static readonly Vector2 MawFromHead = new Vector2(-0.5f, -0.3f);

        /// <summary>World size of the riding sprite's canvas for the kilos it still holds.</summary>
        public static float SacCanvas(Hediff_RRConstricted h) =>
            Comps.Comp_RRConstrictorHunt.WidthByLoad.Evaluate(Mathf.Max(0f, h?.Load ?? 0f)) / SacBodySpan;

        /// <summary>Behind them is to the left, unless they face west.</summary>
        static float Side(Rot4 facing) => facing == Rot4.West ? -1f : 1f;

        /// <summary>Its mouth, relative to the pawn's centre: where the proboscis starts.</summary>
        public static Vector3 Maw(Pawn pawn, Rot4 facing)
        {
            Vector3 head = pawn.Drawer.renderer.BaseHeadOffsetAt(facing);
            return new Vector3(head.x + MawFromHead.x * Side(facing), 0f, head.z + MawFromHead.y);
        }

        /// <summary>The riding sprite's centre, relative to the pawn's centre. From behind it lies on their back.</summary>
        public static Vector3 SacCenter(Pawn pawn, Hediff_RRConstricted h, Rot4 facing)
        {
            float c = SacCanvas(h);
            if (facing == Rot4.North)
                return new Vector3(0f, 0f, BellyZ(pawn) + 0.12f * c);
            Vector3 maw = Maw(pawn, facing);
            return new Vector3(maw.x - MawInSprite.x * c * Side(facing), 0f, maw.z + MawInSprite.y * c);
        }

        /// <summary>The monitor set in its brow, relative to the pawn's centre.</summary>
        public static Vector3 FaceCenter(Pawn pawn, Hediff_RRConstricted h, Rot4 facing)
        {
            float c = SacCanvas(h);
            Vector3 sac = SacCenter(pawn, h, facing);
            return new Vector3(sac.x + FaceInSprite.x * c * Side(facing), 0f, sac.z - FaceInSprite.y * c);
        }

        /// <summary>The proboscis's start, relative to the pawn's centre (for the world-space drawer).</summary>
        public static Vector3 HeadEnd(Pawn pawn, Hediff_RRConstricted h) => Maw(pawn, pawn.Rotation);

        /// <summary>A sharp squeeze right on each pump, easing off until the next.</summary>
        public static float Squeeze(Hediff_RRConstricted h) => Mathf.Exp(-h.PumpPhase * 8f);
    }

    /// <summary>
    /// The constrictor's coils round the victim's body. Swaps between the five
    /// stage sprites as it feeds, swells a little within each, and clenches on
    /// every pump.
    /// </summary>
    [StaticConstructorOnStartup]
    public class PawnRenderNodeWorker_RRConstrictorCoil : PawnRenderNodeWorker
    {
        // Loaded here, on the main thread at startup: 1.6 draws pawns off the main
        // thread, where loading a graphic fails and the node silently draws nothing.
        static readonly Graphic[] stageGraphics = new Graphic[Hediff_RRConstricted.Stages];

        static PawnRenderNodeWorker_RRConstrictorCoil()
        {
            for (int i = 0; i < stageGraphics.Length; i++)
                stageGraphics[i] = GraphicDatabase.Get<Graphic_Single>($"Things/Pawn/RR_GorgeConstrictor/RR_ConstrictorCoil_{i}", ShaderDatabase.Cutout);
        }

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            return base.CanDrawNow(node, parms) && ConstrictorRender.Hediff(node) != null;
        }

        protected override Graphic GetGraphic(PawnRenderNode node, PawnDrawParms parms)
        {
            return stageGraphics[ConstrictorRender.Hediff(node)?.Stage ?? 0];
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            offset.z += ConstrictorRender.CoilLift(parms.pawn);
            return offset;
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            // the node's mesh is 1.5 across; scale it to the coil canvas for this body
            Vector3 scale = base.ScaleFor(node, parms) * (ConstrictorRender.CoilCanvas(parms.pawn) / 1.5f);
            Hediff_RRConstricted h = ConstrictorRender.Hediff(node);
            if (h == null)
                return scale;
            float squeeze = ConstrictorRender.Squeeze(h);
            float swell = 1f + 0.1f * h.WithinStage;
            // clench: pulls in tight across, bulges up and down
            return new Vector3(scale.x * swell * (1f - 0.04f * squeeze), scale.y, scale.z * swell * (1f + 0.05f * squeeze));
        }
    }

    /// <summary>
    /// The leech itself, riding behind its victim: the same engorged body it hunts
    /// in, at the same size for the kilos it still holds, so it looms over a small
    /// victim and shrinks as it pumps itself into them. Side-on behind them (mouth
    /// by their shoulder); from behind, it lies across their back, over them.
    /// Squeezes in a little on every pump.
    /// </summary>
    [StaticConstructorOnStartup]
    public class PawnRenderNodeWorker_RRConstrictorSac : PawnRenderNodeWorker
    {
        static readonly Graphic riding = GraphicDatabase.Get<Graphic_Multi>("Things/Pawn/RR_GorgeConstrictor/RR_ConstrictorRiding", ShaderDatabase.Cutout);

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            return base.CanDrawNow(node, parms) && ConstrictorRender.Hediff(node) != null;
        }

        protected override Graphic GetGraphic(PawnRenderNode node, PawnDrawParms parms) => riding;

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            Hediff_RRConstricted h = ConstrictorRender.Hediff(node);
            if (h != null)
                offset += ConstrictorRender.SacCenter(parms.pawn, h, parms.facing);
            return offset;
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            Hediff_RRConstricted h = ConstrictorRender.Hediff(node);
            Vector3 scale = base.ScaleFor(node, parms);
            if (h == null)
                return scale;
            // the node's mesh is 1.5 across
            float k = ConstrictorRender.SacCanvas(h) / 1.5f * (1f - 0.035f * ConstrictorRender.Squeeze(h));
            return new Vector3(scale.x * k, scale.y, scale.z * k);
        }

        // behind the body, unless seen from behind: then it lies on top of them
        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms) =>
            parms.facing == Rot4.North ? 75f : -8f;
    }

    /// <summary>
    /// The bloodied monitor set in the leech's brow, its neon-blue face more content
    /// the more it has pumped into them. Drawn on the riding leech, at its size; not
    /// from behind. Can be turned off in RimRound's settings.
    /// </summary>
    [StaticConstructorOnStartup]
    public class PawnRenderNodeWorker_RRConstrictorFace : PawnRenderNodeWorker
    {
        static readonly Graphic[] faces = new Graphic[Hediff_RRConstricted.Stages];

        static PawnRenderNodeWorker_RRConstrictorFace()
        {
            for (int i = 0; i < faces.Length; i++)
                faces[i] = GraphicDatabase.Get<Graphic_Single>($"Things/Pawn/RR_GorgeConstrictor/RR_ConstrictorFace_{i}", ShaderDatabase.Cutout);
        }

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            return GlobalSettings.constrictorScreenFace && parms.facing != Rot4.North
                && base.CanDrawNow(node, parms) && ConstrictorRender.Hediff(node) != null;
        }

        protected override Graphic GetGraphic(PawnRenderNode node, PawnDrawParms parms)
        {
            return faces[ConstrictorRender.Hediff(node)?.Stage ?? 0];
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            Hediff_RRConstricted h = ConstrictorRender.Hediff(node);
            if (h != null)
                offset += ConstrictorRender.FaceCenter(parms.pawn, h, parms.facing);
            return offset;
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            Vector3 scale = base.ScaleFor(node, parms);
            Hediff_RRConstricted h = ConstrictorRender.Hediff(node);
            if (h == null)
                return scale;
            // the node's mesh is 1 across; the monitor is about a fifth of the leech's canvas
            float c = ConstrictorRender.SacCanvas(h);
            return new Vector3(scale.x * 0.18f * c, scale.y, scale.z * 0.22f * c);
        }

        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms) => -7f;
    }
}
