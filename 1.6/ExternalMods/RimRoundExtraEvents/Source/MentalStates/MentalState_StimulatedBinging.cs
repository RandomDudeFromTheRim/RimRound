using RimRound.Comps;
using RimRound.Utilities;
using RimRoundExtraEvents.Hediffs;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace RimRoundExtraEvents.MentalStates
{
    public class MentalState_AppetiteStimulatedBinging : MentalState_Binging
    {
	    public void MentalStateTick()
		{
			if (this.pawn.IsHashIntervalTick(30))
			{

				if (!this.pawn.health.hediffSet.HasHediff(Hediffs.HediffDefOf.RREE_AppetitePillHigh))
				{
					return;
				}
					
				Hediff hediff = this.pawn.health.hediffSet.GetFirstHediffOfDef(Hediffs.HediffDefOf.RREE_AppetitePillHigh);

				if (hediff is null)
				{
					this.RecoverFromState();
				}

				switch (hediff.CurStageIndex)
				{
					case 3:
						break;
					case 2:

						var comp = this.pawn.TryGetComp<FullnessAndDietStats_ThingComp>();

						if (comp is null)
						{
							break;
						}

						float fullnessRatio = comp.CurrentFullness / comp.SoftLimit;

						if (fullnessRatio > 1.0)
						{
							this.RecoverFromState();
						}

						break;
					default:
						this.RecoverFromState();
						break;
				}
			}
		}
	}
}
