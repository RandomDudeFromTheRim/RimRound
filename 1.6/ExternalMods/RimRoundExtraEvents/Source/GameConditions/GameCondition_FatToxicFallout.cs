using RimWorld;
using Verse;

namespace RimRoundExtraEvents.GameConditions
{
    public class GameCondition_FatToxicFallout : GameCondition
    {
        private const int CheckInterval = 200;
        private const float BaseSeverityPerDay = 0.12f;
        private const float PlantGrowthFactor = 1.8f;

        public override void GameConditionTick()
        {
            if (Find.TickManager.TicksGame % CheckInterval != 0)
                return;

            Map map = SingleMap;
            if (map == null)
                return;

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!pawn.RaceProps.Humanlike || pawn.Dead || pawn.Suspended)
                    continue;

                if (pawn.Position.UsesOutdoorTemperature(map) && !pawn.Position.Roofed(map))
                {
                    Hediff hediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDef.Named("FatToxicBuildup"));
                    if (hediff == null)
                    {
                        hediff = HediffMaker.MakeHediff(HediffDef.Named("FatToxicBuildup"), pawn);
                        hediff.Severity = 0.01f;
                        pawn.health.AddHediff(hediff);
                    }
                    else
                    {
                        float sevPerCheck = BaseSeverityPerDay / 60000f * CheckInterval;
                        hediff.Severity += sevPerCheck;
                    }
                }
            }

            foreach (Plant plant in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
            {
                if (plant.Spawned && !plant.Blighted && plant.Position.UsesOutdoorTemperature(map))
                {
                    plant.Growth += (PlantGrowthFactor - 1f) * 0.0002f;
                }
            }
        }

        public override void Init()
        {
            base.Init();
            Find.LetterStack.ReceiveLetter(
                "Mutagenic Enbiggener Fallout",
                "A distant chemical explosion has released a plume of mutagenic smoke over this entire region.\n\n"
                + "Any person not under a roof will absorb the substance settling out of the atmosphere and slowly grow fatter.\n\n"
                + "It will last for anywhere between a few days to over a quadrum.",
                LetterDefOf.NegativeEvent
            );
        }

        public override void End()
        {
            Find.LetterStack.ReceiveLetter(
                "Fallout Settled",
                "The worst of the growth dust has settled. The mutagenic fallout has passed.",
                LetterDefOf.PositiveEvent
            );
        }
    }
}
