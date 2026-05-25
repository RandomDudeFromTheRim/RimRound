using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace RimRound.Comps
{
    public static class Comp_GluttoniumForceFeed
    {
        public static void PatchConversation(HarmonyLib.Harmony harmony)
        {
            ModCompatibilityUtility.TryPatch(
                harmony,
                new ModPatchInfo("Intimacy - Socio Butterfly", "RecreationalSexWithEuterpe.InteractionWorker_StartConversation", "RandomSelectionWeight", MethodType.Normal),
                new PatchCollection
                {
                    postfix = typeof(Comp_GluttoniumForceFeed).GetMethod(nameof(Postfix_SelectionWeight), BindingFlags.Static | BindingFlags.NonPublic)
                });
        }

        static void Postfix_SelectionWeight(ref float __result, Pawn initiator, Pawn recipient)
        {
            if (__result <= 0 || initiator is null || recipient is null)
                return;

            if (!CanForceFeed(initiator, recipient))
                return;

            __result *= 3f;
        }

        static bool CanForceFeed(Pawn initiator, Pawn recipient)
        {
            var initAtt = initiator.TryGetComp<ThingComp_PawnAttitude>();
            if (initAtt is null)
                return false;

            if (initAtt.weightOpinion < WeightOpinion.Like)
                return false;

            var recFN = recipient.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (recFN is null || recFN.Disabled)
                return false;

            return recFN.CurrentFullness < recFN.SoftLimit * 0.8f;
        }

        public static void Postfix_ConverseStart(Pawn initiator, Pawn recipient)
        {
            if (initiator is null || recipient is null)
                return;

            var initAtt = initiator.TryGetComp<ThingComp_PawnAttitude>();
            if (initAtt is null || initAtt.weightOpinion < WeightOpinion.Like)
                return;

            var recFN = recipient.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (recFN is null || recFN.Disabled)
                return;

            if (recFN.CurrentFullness < recFN.SoftLimit * 0.8f)
                TryStartForceFeed(initiator, recipient, recFN);
        }

        static void TryStartForceFeed(Pawn initiator, Pawn recipient, FullnessAndDietStats_ThingComp recFN)
        {
            Thing food = FindFoodForForceFeed(initiator);
            if (food is null)
                return;

            float nutrition = food.GetStatValue(StatDefOf.Nutrition);
            float kilos = nutrition * 0.5f;

            recFN.activeWeightGainRequests.Enqueue(
                new WeightGainRequest(kilos, Find.TickManager.TicksGame + 5, 6000, false));

            food.Destroy(DestroyMode.Vanish);

            var initAtt = initiator.TryGetComp<ThingComp_PawnAttitude>();
            var initThought = initiator.needs?.mood?.thoughts;
            if (initThought != null && initAtt.weightOpinion >= WeightOpinion.Fanatical)
                initThought.memories.TryGainMemory(ThoughtDef.Named("RR_ForceFed"), recipient);

            var recThought = recipient.needs?.mood?.thoughts;
            if (recThought != null)
                recThought.memories.TryGainMemory(ThoughtDef.Named("RR_WasForceFed"), initiator);

            Messages.Message(
                $"{initiator.LabelShort} force-fed {recipient.LabelShort} some {food.def.label}!",
                new LookTargets(new Pawn[] { initiator, recipient }),
                MessageTypeDefOf.PositiveEvent);
        }

        static Thing FindFoodForForceFeed(Pawn pawn)
        {
            if (pawn.carryTracker?.CarriedThing is Thing carried && carried.def.IsNutritionGivingIngestible)
                return carried;

            var inv = pawn.inventory?.innerContainer;
            if (inv != null)
            {
                for (int i = 0; i < inv.Count; i++)
                {
                    if (inv[i].def.IsNutritionGivingIngestible)
                        return inv[i];
                }
            }

            return null;
        }
    }
}
