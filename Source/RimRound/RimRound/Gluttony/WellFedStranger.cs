using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.Gluttony
{
    /// <summary>
    /// The well-fed stranger (Anomaly creepjoiner): enormous, beaming, a wonderful cook - and
    /// always bringing something with them. Their benefit and downside never turn up on other
    /// creepjoiners (canOccurRandomly false); WellFedStrangerPatches hands them out to this form
    /// only. Every downside feeds the colony one way or another: the bottomless bowl, a seed of
    /// sweet slime, a leak of gluttonium, or a gorge constrictor nesting in their gut.
    /// </summary>
    public static class WellFedStranger
    {
        public const string FormName = "RR_WellFedStranger";
        public const string BenefitName = "RR_StrangerHearthCook";

        public static bool IsStrangerForm(CreepJoinerFormKindDef form) => form?.defName == FormName;

        public static IEnumerable<CreepJoinerDownsideDef> Downsides =>
            DefDatabase<CreepJoinerDownsideDef>.AllDefsListForReading.Where(d => d.HasModExtension<StrangerDownsideExtension>());

        public static void Assign(ref CreepJoinerBenefitDef benefit, ref CreepJoinerDownsideDef downside)
        {
            benefit = DefDatabase<CreepJoinerBenefitDef>.GetNamedSilentFail(BenefitName) ?? benefit;
            if (Downsides.TryRandomElementByWeight(d => d.Weight, out CreepJoinerDownsideDef pick))
                downside = pick;
        }
    }

    /// <summary>Marks a creepjoiner downside as one of the well-fed stranger's.</summary>
    public class StrangerDownsideExtension : DefModExtension
    {
    }

    [HarmonyPatch(typeof(CreepJoinerUtility), nameof(CreepJoinerUtility.GetCreepjoinerSpecifics))]
    static class CreepJoinerUtility_GetCreepjoinerSpecifics_WellFedStranger
    {
        static void Postfix(ref CreepJoinerFormKindDef form, ref CreepJoinerBenefitDef benefit, ref CreepJoinerDownsideDef downside)
        {
            if (WellFedStranger.IsStrangerForm(form))
                WellFedStranger.Assign(ref benefit, ref downside);
        }
    }

    [HarmonyPatch(typeof(CreepJoinerUtility), nameof(CreepJoinerUtility.GenerateAndSpawn),
        new[] { typeof(CreepJoinerFormKindDef), typeof(CreepJoinerBenefitDef), typeof(CreepJoinerDownsideDef), typeof(CreepJoinerAggressiveDef), typeof(CreepJoinerRejectionDef), typeof(Map) })]
    static class CreepJoinerUtility_GenerateAndSpawn_WellFedStranger
    {
        static void Prefix(CreepJoinerFormKindDef form, ref CreepJoinerBenefitDef benefit, ref CreepJoinerDownsideDef downside)
        {
            // the debug spawner and the plain incident roll everything themselves
            if (WellFedStranger.IsStrangerForm(form) && (benefit?.defName != WellFedStranger.BenefitName || !downside.HasModExtension<StrangerDownsideExtension>()))
                WellFedStranger.Assign(ref benefit, ref downside);
        }
    }

    // ------------------------------------------------------------------ what they bring

    /// <summary>They unpack a battered bowl they've carried all this way. It's never empty.</summary>
    public class CreepJoinerWorker_RRBowl : BaseCreepJoinerWorker
    {
        public override bool CanDoResponse() => BowlUtility.Bowl != null && Pawn.Spawned;

        public override void DoResponse(List<TargetInfo> looktargets, List<NamedArgument> namedArgs)
        {
            Thing bowl = BowlArrival.Spawn(Pawn.Position, Pawn.Map);
            if (bowl != null)
                looktargets.Add(bowl);
            if (BowlUtility.Allure != null && BowlUtility.AllureStrength(Pawn) > 0f && !Pawn.health.hediffSet.HasHediff(BowlUtility.Allure))
                Pawn.health.AddHediff(BowlUtility.Allure).Severity = 0.7f;
        }
    }

    /// <summary>The sweet warmth they carry was a seed of sweet slime, and it has started to grow.</summary>
    public class CreepJoinerWorker_RRSlimeSeed : BaseCreepJoinerWorker
    {
        static HediffDef Seed => DefDatabase<HediffDef>.GetNamedSilentFail("RR_SweetSlimeInfestation");

        public override bool CanDoResponse() => Seed != null && !Pawn.health.hediffSet.HasHediff(Seed);

        public override void DoResponse(List<TargetInfo> looktargets, List<NamedArgument> namedArgs) => Pawn.health.AddHediff(Seed);
    }

    /// <summary>They have been eating raw gluttonium for years, and now it seeps out of them.</summary>
    public class CreepJoinerWorker_RRGluttoniumSeep : BaseCreepJoinerWorker
    {
        static HediffDef Seep => DefDatabase<HediffDef>.GetNamedSilentFail("RR_GluttoniumSeep");

        public override bool CanDoResponse() => Seep != null && !Pawn.health.hediffSet.HasHediff(Seep);

        public override void DoResponse(List<TargetInfo> looktargets, List<NamedArgument> namedArgs) => Pawn.health.AddHediff(Seep);
    }

    /// <summary>
    /// Their belly was never all them: a gorge constrictor has been nesting in their gut, and
    /// it wriggles free - wild, hungry, and still carrying most of what they ate. They come out
    /// of it a good deal lighter, retching and dazed.
    /// </summary>
    public class CreepJoinerWorker_RRConstrictor : BaseCreepJoinerWorker
    {
        const float WeightKept = 0.7f;

        static PawnKindDef Kind => DefDatabase<PawnKindDef>.GetNamedSilentFail("RR_GorgeConstrictor");

        public override bool CanDoResponse() => Kind != null && Faction.OfEntities != null && Pawn.Spawned;

        public override void DoResponse(List<TargetInfo> looktargets, List<NamedArgument> namedArgs)
        {
            Map map = Pawn.Map;
            Pawn beast = PawnGenerator.GeneratePawn(Kind, Faction.OfEntities);
            GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(Pawn.Position, map, 2), map);
            looktargets.Add(beast);
            FilthMaker.TryMakeFilth(Pawn.Position, map, ThingDefOf.Filth_Vomit, 4);
            FilthMaker.TryMakeFilth(Pawn.Position, map, ThingDefOf.Filth_Blood, 2);

            if (Pawn.WeightHediff() is Hediff weight)
                Utilities.HediffUtility.QueueExactWeightChange(Pawn, -Utilities.HediffUtility.SeverityToKilosWithoutBaseWeight(weight.Severity) * (1f - WeightKept));
            Pawn.stances?.stunner?.StunFor(600, null, addBattleLog: false);
        }
    }

    // ------------------------------------------------------------------ the gluttonium seep

    /// <summary>
    /// Leaking gluttonium: everyone who spends time close to this person picks up gluttonium
    /// exposure (RR_GluttoniumExposure), which fattens them until it wears off.
    /// </summary>
    public class Hediff_GluttoniumSeep : HediffWithComps
    {
        const int Interval = 250;
        const float Radius = 4.9f;
        const float ExposurePerPulse = 0.0025f;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!pawn.Spawned || BowlUtility.Exposure == null || !pawn.IsHashIntervalTick(Interval, delta))
                return;
            foreach (Pawn other in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (other == pawn || other.Dead || !other.RaceProps.Humanlike || !other.Position.InHorDistOf(pawn.Position, Radius))
                    continue;
                HealthUtility.AdjustSeverity(other, BowlUtility.Exposure, ExposurePerPulse);
            }
        }
    }
}
