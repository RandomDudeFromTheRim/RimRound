using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.AI
{
    /// <summary>
    /// Weight-opinion scaled mood for the flesh dimension. Pawns who love
    /// growing feel a creeping, pleasurable bliss from the void's call; those
    /// who hate it feel growing unease. The severity of the RR_VoidFascination
    /// hediff (which ramps the longer a pawn lingers inside the maze) drives the
    /// thought stage.
    /// </summary>
    public class ThoughtWorker_VoidFascination : ThoughtWorker
    {
        static string DefNameForOpinion(WeightOpinion opinion)
        {
            switch (opinion)
            {
                case WeightOpinion.Hate: return "RR_VoidFascination_Hate";
                case WeightOpinion.Dislike: return "RR_VoidFascination_Dislike";
                case WeightOpinion.NeutralMinus: return "RR_VoidFascination_NeutralMinus";
                case WeightOpinion.Neutral: return "RR_VoidFascination_Neutral";
                case WeightOpinion.NeutralPlus: return "RR_VoidFascination_NeutralPlus";
                case WeightOpinion.Like: return "RR_VoidFascination_Like";
                case WeightOpinion.Love: return "RR_VoidFascination_Love";
                case WeightOpinion.Fanatical: return "RR_VoidFascination_Fanatical";
                default: return "RR_VoidFascination_Neutral";
            }
        }

        static int StageForSeverity(float severity)
        {
            if (severity < 0.30f) return 0;   // curious
            if (severity < 0.55f) return 1;   // hungry
            if (severity < 0.80f) return 2;   // ravenous
            return 3;                          // insatiable
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

            var fascination = p.health.hediffSet.GetFirstHediffOfDef(Defs.HediffDefOf.RR_VoidFascination);
            if (fascination is null || fascination.Severity < 0.01f)
                return false;

            return ThoughtState.ActiveAtStage(StageForSeverity(fascination.Severity));
        }
    }
}
