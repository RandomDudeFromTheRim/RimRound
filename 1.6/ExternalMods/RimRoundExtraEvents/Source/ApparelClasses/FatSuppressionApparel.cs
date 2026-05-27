using RimRound.Comps;
using RimRoundExtraEvents.ApparelClasses;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using HediffUtility = RimRound.Utilities.HediffUtility;

namespace RimRoundExtraEvents.Apparelclasses
{
    public class FatSuppressionApparel : Apparel
    {
	    protected override void Tick()
		{
			base.Tick();

			Pawn pawn = this.Wearer;
			if (pawn == null) return;

			if (!(pawn.RaceProps.Humanlike))
				return;

			if (pawn.IsHashIntervalTick(30))
			{
				Hediff weightHediff = HediffUtility.WeightHediff(pawn);
				if (weightHediff == null) return;

				var comp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();

				if (comp is null)
					return;

				float oldSeverity = weightHediff.Severity;
				float difference = oldSeverity - this.weightSeverityReducedTo;

				if (difference > 0)
				{
					//HediffUtility.AddHediffSeverity(RimRound.Defs.HediffDefOf.RimRound_Weight, pawn, -1 * difference, true, false);

					int currentTick = Find.TickManager.TicksGame;
					float kilosToApply = HediffUtility.SeverityToKilosWithoutBaseWeight(difference);
					comp.activeWeightLossRequests.Enqueue(new WeightGainRequest(-1 * kilosToApply, currentTick, 0, true, false));

					this.storedWeightSeverity += difference;
				} else if (difference < 0 && this.storedWeightSeverity > 0)
				{
					float addDifference = Math.Min(-1 * difference, this.storedWeightSeverity);
					//HediffUtility.AddHediffSeverity(RimRound.Defs.HediffDefOf.RimRound_Weight, pawn, addDifference, true, false);

					int currentTick = Find.TickManager.TicksGame;
					float kilosToApply = HediffUtility.SeverityToKilosWithoutBaseWeight(addDifference);
					comp.activeWeightGainRequests.Enqueue(new WeightGainRequest(kilosToApply, currentTick, 0, true, false));

					this.storedWeightSeverity -= addDifference;
				}
			}

			if (pawn.IsHashIntervalTick(2000))
			{
				this.TickLongEquivalent();
			}
		}

        public virtual void TickLongEquivalent()
        {

			Pawn pawn = this.Wearer;
			if (pawn == null) return;

			Log.Message("TickLongEquivalent() Start:\n"
				+ "storedWeightSeverity: " + this.storedWeightSeverity + "\n"
				+ "maxWeightSeverity: " + this.maxWeightSeverity);

			if (this.storedWeightSeverity > this.maxWeightSeverity)
			{
				//int num = GenMath.RoundRandom(ap.def.apparel.wearPerDay);
				//if (num > 0)
				//{
				//	ap.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, (float)num, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));
				//}
				//if (ap.Destroyed && PawnUtility.ShouldSendNotificationAbout(this.pawn) && !this.pawn.Dead)
				//{
				//	Messages.Message("MessageWornApparelDeterioratedAway".Translate(GenLabel.ThingLabel(ap.def, ap.Stuff, 1), this.pawn).CapitalizeFirst(), this.pawn, MessageTypeDefOf.NegativeEvent, true);
				//}

				float severityOverCap = this.storedWeightSeverity - this.maxWeightSeverity;

				float num = GenMath.RoundRandom(HediffUtility.SeverityToKilosWithoutBaseWeight(severityOverCap) / 10);

				Log.Message("TickLongEquivalent() Mid:\n"
				+ "severityOverCap: " + severityOverCap + "\n"
				+ "num: " + num);

				this.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, (float)num, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));

				if (this.Destroyed && PawnUtility.ShouldSendNotificationAbout(pawn) && !pawn.Dead)
				{
					Messages.Message("MessageWornApparelDeterioratedAway".Translate(GenLabel.ThingLabel(this.def, this.Stuff, 1), pawn).CapitalizeFirst(), pawn, MessageTypeDefOf.NegativeEvent, true);
				}
			}
		}

        public override void Notify_Equipped(Pawn pawn)
		{
			base.Notify_Equipped(pawn);
		}

		public override void Notify_Unequipped(Pawn pawn)
		{
			base.Notify_Unequipped(pawn);
			//do weight gain stuff here

			if (pawn == null) return;

			if (!(pawn.RaceProps.Humanlike))
				return;

			if (pawn.Dead) return;

			var comp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();

			if (comp is null)
				return;

			float kilosToAdd = HediffUtility.SeverityToKilosWithoutBaseWeight(this.storedWeightSeverity);
			float kilosPerApplication = 100; //TODO change this
			int ticksPerApplication = 60;
			int numberOfApplications = (int)(kilosToAdd / kilosPerApplication);

			int currentTick = Find.TickManager.TicksGame;

			for (int i = 0; i < numberOfApplications; ++i)
			{
				comp.activeWeightGainRequests.Enqueue(new WeightGainRequest(kilosPerApplication, currentTick + i * ticksPerApplication, 0, true, false));
            }


            float leftoverKilos = kilosToAdd % kilosPerApplication;

			if (leftoverKilos > 0)
			{
				comp.activeWeightGainRequests.Enqueue(new WeightGainRequest(leftoverKilos, currentTick + numberOfApplications * ticksPerApplication, 0, true, false));
			}


			this.storedWeightSeverity = 0;
        }

		public override IEnumerable<Gizmo> GetWornGizmos()
		{
			foreach (Gizmo gizmo in base.GetWornGizmos())
			{
				yield return gizmo;
			}
			foreach (Gizmo gizmo2 in this.GetSuppressionGizmos())
			{
				yield return gizmo2;
			}
			yield break;
		}

		private IEnumerable<Gizmo> GetSuppressionGizmos()
		{
			if (this.Wearer.Faction == Faction.OfPlayer && Find.Selector.SingleSelectedThing == this.Wearer)
			{
				//yield return new Gizmo_EnergyShieldStatus
				//{
				//	shield = this
				//};
			}
			yield break;
		}

		public float weightSeverityReducedTo
		{
			get
			{
				if (this.weightSeverityReducedTo_ == -1)
				{
					foreach (var modExtension in this.def.modExtensions)
					{
						if (modExtension is FatSuppressionModExtension)
						{
							FatSuppressionModExtension fatSuppressionModExtension = (FatSuppressionModExtension)modExtension;
							this.weightSeverityReducedTo_ = fatSuppressionModExtension.weightSeverityReducedTo;
							break;
						}
					}
				}
				return this.weightSeverityReducedTo_;
			}
		}

		public float maxWeightSeverity
		{
			get
			{
				if (this.maxWeightSeverity_ == -1)
				{
					foreach (var modExtension in this.def.modExtensions)
					{
						if (modExtension is FatSuppressionModExtension)
						{
							FatSuppressionModExtension fatSuppressionModExtension = (FatSuppressionModExtension)modExtension;
							this.maxWeightSeverity_ = fatSuppressionModExtension.maxWeightSeverity;
							break;
						}
					}
				}
				return this.maxWeightSeverity_;
			}
		}

		private float weightSeverityReducedTo_ = -1;

		private float maxWeightSeverity_ = -1;

		public float storedWeightSeverity = 0;
	}
}
