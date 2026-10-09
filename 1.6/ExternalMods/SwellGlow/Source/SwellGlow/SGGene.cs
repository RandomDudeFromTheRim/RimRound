using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using RimRound;
using RimRound.Utilities;
using RimRound.Comps;
using HarmonyLib;

namespace SwellGlow
{
    public class SGTraitExtension : DefModExtension
    {
        public WeightOpinion opinionTrait;
    }

    public class SGGene : Gene
    {
        public override void PostAdd()
        {
            SGTraitExtension weightTrait = def.GetModExtension<SGTraitExtension>();
            if (weightTrait != null)
            {
                var attitudeComp = pawn.TryGetComp<ThingComp_PawnAttitude>();
                WeightOpinion? pawnOpinion = pawn.story?.traits?.allTraits
                                .Select(t => t.def.GetModExtension<SGTraitExtension>())
                                .Where(ext => ext != null)
                                .Select(ext => (WeightOpinion?)ext.opinionTrait)
                                .FirstOrDefault(o => o != null);
                // pawns of races RimRound doesn't patch have no attitude to set
                if (pawnOpinion == null && attitudeComp != null)
                    attitudeComp.SetWeightOpinion(weightTrait.opinionTrait);
            }
            base.PostAdd();
        }
    }
}
