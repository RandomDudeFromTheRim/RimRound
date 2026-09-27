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

        /// <summary>The constrictor's head end, up over the victim's right shoulder, relative to the pawn's centre.</summary>
        public static Vector3 HeadEnd(Pawn pawn, Hediff_RRConstricted h)
        {
            float c = CoilCanvas(pawn);
            float s = h?.Stage ?? 0;
            return new Vector3((0.235f + 0.008f * s) * c, 0f, CoilLift(pawn) + (0.1f + 0.008f * s) * c);
        }

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
    /// The bloodied monitor embedded in the constrictor's flesh, its neon-blue face
    /// getting more flushed each stage. It is a fixed-size object: the same size on
    /// every pawn and at every stage, so the flesh swelling round it buries it.
    /// Shown facing the viewer only; can be turned off in RimRound's settings.
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
            return GlobalSettings.constrictorScreenFace && parms.facing == Rot4.South
                && base.CanDrawNow(node, parms) && ConstrictorRender.Hediff(node) != null;
        }

        protected override Graphic GetGraphic(PawnRenderNode node, PawnDrawParms parms)
        {
            return faces[ConstrictorRender.Hediff(node)?.Stage ?? 0];
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            offset.z += ConstrictorRender.BellyZ(parms.pawn);
            return offset;
        }

        // world size of the monitor, in cells: the node's mesh is 1 across
        const float MonitorSize = 0.42f;

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            return base.ScaleFor(node, parms) * MonitorSize;
        }
    }
}
