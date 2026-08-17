using RimRound.AI;
using RimRound.Hediffs;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.Patch
{
    /// <summary>
    /// When a void echo is held on an Anomaly holding platform, adds options to
    /// feed it colonists and to release whoever it has swallowed.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Building_HoldingPlatform), nameof(Building_HoldingPlatform.GetGizmos))]
    public static class BuildingHoldingPlatform_VoidEchoGizmos
    {
        static void Postfix(Building_HoldingPlatform __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!ModsConfig.AnomalyActive || __instance.Faction != Faction.OfPlayer)
                return;

            var echo = __instance.HeldPawn;
            var vigor = echo?.health?.hediffSet?.GetFirstHediffOfDef(HediffDef.Named("RR_VoidEchoVigor")) as Hediff_VoidEchoVigor;
            if (vigor == null)
                return;

            var list = __result.ToList();
            list.Add(new Command_Action
            {
                defaultLabel = "Feed a colonist to the echo",
                defaultDesc = "Send a colonist to be swallowed whole by the void echo. They can be released again later; the echo produces voidmilk while it digests.",
                icon = ContentFinder<Texture2D>.Get("UI/Activate/Activate"),
                action = delegate
                {
                    Pawn meal = FindCandidate(__instance);
                    if (meal == null)
                    {
                        Messages.Message("No free adult colonist is nearby enough to feed to the echo.",
                            __instance, MessageTypeDefOf.RejectInput, historical: false);
                        return;
                    }

                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Allow {meal.LabelShort} to be swallowed by the echo?",
                        delegate
                        {
                            Job job = JobMaker.MakeJob(Defs.JobDefOf.RR_FeedToEcho, __instance);
                            meal.jobs.StartJob(job, JobCondition.InterruptForced);
                        }));
                }
            });

            int contained = vigor.ContainedCount;
            if (contained > 0)
            {
                list.Add(new Command_Action
                {
                    defaultLabel = $"Release the consumed ({contained})",
                    defaultDesc = "Everyone the echo has swallowed climbs back out, dazed and heavier.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ReleaseUnit"),
                    action = delegate { vigor.ReleaseAll(__instance.Position, __instance.Map); }
                });
            }

            __result = list;
        }

        static Pawn FindCandidate(Building_HoldingPlatform platform)
        {
            Map map = platform.Map;
            Pawn best = null;
            float bestDist = 1e6f;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.Drafted || p.InMentalState || p.DevelopmentalStage != DevelopmentalStage.Adult)
                    continue;
                float d = p.Position.DistanceTo(platform.Position);
                if (d < bestDist && d < 60f)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }
    }
}
