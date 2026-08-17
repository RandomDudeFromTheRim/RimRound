using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Thoughts
{
    /// <summary>
    /// Pawns who Like+ their weight gain admire the generously sized company
    /// they keep — an ambient moodlet, no interaction required.
    /// </summary>
    public class ThoughtWorker_AdmiringNearbyBulk : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!p.RaceProps.Humanlike || p.Map == null || !GlobalSettings.moodletsForWeightOpinions)
                return false;

            var att = p.TryGetComp<ThingComp_PawnAttitude>();
            if (att == null || att.weightOpinion < WeightOpinion.Like)
                return false;

            var pbt = p.TryGetComp<PawnBodyType_ThingComp>();
            if (pbt != null && (pbt.PersonallyExempt || pbt.CategoricallyExempt))
                return false;

            int bestStage = -1;
            foreach (Pawn other in p.Map.mapPawns.AllPawnsSpawned)
            {
                if (other == p || !other.RaceProps.Humanlike || other.Dead)
                    continue;
                if (other.Position.DistanceTo(p.Position) > 8f)
                    continue;

                float sev = Utilities.HediffUtility.WeightHediff(other)?.Severity ?? 0f;
                if (sev >= 0.95f) { bestStage = 2; break; }
                if (sev >= 0.44f) bestStage = Mathf.Max(bestStage, 1);
                else if (sev >= 0.15f) bestStage = Mathf.Max(bestStage, 0);
            }

            return bestStage >= 0 ? ThoughtState.ActiveAtStage(bestStage) : false;
        }
    }
}
