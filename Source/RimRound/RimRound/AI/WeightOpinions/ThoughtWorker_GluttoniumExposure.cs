using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.AI
{
    public class ThoughtWorker_GluttoniumExposure : ThoughtWorker
    {
        static string DefNameForOpinion(WeightOpinion opinion)
        {
            switch (opinion)
            {
                case WeightOpinion.Hate: return "RR_GluttoniumExposure_Hate";
                case WeightOpinion.Dislike: return "RR_GluttoniumExposure_Dislike";
                case WeightOpinion.NeutralMinus: return "RR_GluttoniumExposure_NeutralMinus";
                case WeightOpinion.Neutral: return "RR_GluttoniumExposure_Neutral";
                case WeightOpinion.NeutralPlus: return "RR_GluttoniumExposure_NeutralPlus";
                case WeightOpinion.Like: return "RR_GluttoniumExposure_Like";
                case WeightOpinion.Love: return "RR_GluttoniumExposure_Love";
                case WeightOpinion.Fanatical: return "RR_GluttoniumExposure_Fanatical";
                default: return "RR_GluttoniumExposure_Neutral";
            }
        }

        static int StageForSeverity(float severity)
        {
            if (severity < 0.05f) return 0;
            if (severity < 0.15f) return 1;
            if (severity < 0.30f) return 2;
            if (severity < 0.50f) return 3;
            return 4;
        }

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!p.RaceProps.Humanlike || p.Dead)
                return false;

            var attitude = p.TryGetComp<ThingComp_PawnAttitude>();
            if (attitude is null)
                return false;

            if (def.defName != DefNameForOpinion(attitude.weightOpinion))
                return false;

            var exposure = p.health.hediffSet.GetFirstHediffOfDef(Defs.HediffDefOf.RR_GluttoniumExposure);
            if (exposure is null || exposure.Severity < 0.01f)
                return false;

            return ThoughtState.ActiveAtStage(StageForSeverity(exposure.Severity));
        }
    }
}
