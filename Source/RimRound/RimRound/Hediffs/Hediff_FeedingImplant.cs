using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Text;
using Verse;

namespace RimRound.Hediffs
{
    public class Hediff_FeedingImplant : Hediff_AddedPart
    {
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            StatChangeUtility.ChangeRimRoundStats(this.pawn, new RimRoundStatBonuses()
            {
                digestionRateMultiplier = 0.5f,
                weightGainMultiplier = 0.3f
            });
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            StatChangeUtility.ChangeRimRoundStats(this.pawn, new RimRoundStatBonuses()
            {
                digestionRateMultiplier = -0.5f,
                weightGainMultiplier = -0.3f
            });
        }

        public override string TipStringExtra
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(base.TipStringExtra);
                sb.AppendLine("Metabolism efficiency +100% (half the hunger rate)");
                sb.AppendLine("Digestion rate +50%");
                sb.AppendLine("Weight gain rate +30%");
                return sb.ToString();
            }
        }
    }
}
