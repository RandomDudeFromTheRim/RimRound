using HarmonyLib;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.FeedingTube;
using RimRound.FeedingTube.Patches;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    [StaticConstructorOnStartup]
    public static class FeedOtherBootstrap
    {
        private static bool? appliedFoodNetworkV2Mode;

        static FeedOtherBootstrap()
        {
            Harmony harmony = new Harmony("RimRound.FeedOther");

            RemoveFaultyBaseHoverchairPatches(harmony);
            RemoveFaultyBaseNotRegalBedPatches(harmony);
            RemoveLegacyFoodNetworkPatches(harmony);
            // Static-constructor ordering inside a multi-assembly mod is not a
            // reliable contract. Repeat the targeted unpatch after startup so
            // RRHarmony cannot re-install the old versions after this assembly.
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                RemoveFaultyBaseHoverchairPatches(harmony);
                RemoveFaultyBaseNotRegalBedPatches(harmony);
                ApplyFoodNetworkPatchMode(
                    FeedOtherMod.Settings.foodNetworkV2Enabled);
            });

            harmony.PatchAll(Assembly.GetExecutingAssembly());
            InstallHoverchairRenderPatch(harmony);

            // RimRound machines without a dedicated v2 patch (the food converter)
            // reach the liquid food network through FoodNetworkAccess.
            FoodNetworkAccess.Current = new FoodNetworkV2Access(FoodNetworkAccess.Current);

            // Install beta generation probabilities before any new pawn can
            // be generated. The category finalizer itself is Harmony-patched
            // after vanilla and RimRound trait generation.
            RimRoundTraitGenerationBeta.InitializeBetaRules();

            // Vanilla uses a fixed 1.5x chew duration for every patient. Replace
            // only the untouched vanilla driver so ordinary patient/prisoner
            // feeding respects the recipient's Eating Speed and our modest
            // assisted-feeding speed bonus without clobbering another mod's
            // custom FeedPatient driver.
            if (JobDefOf.FeedPatient != null &&
                JobDefOf.FeedPatient.driverClass ==
                    typeof(JobDriver_FoodFeedPatient))
            {
                JobDefOf.FeedPatient.driverClass =
                    typeof(JobDriver_FoodFeedPatientEatingSpeed);
            }
        }

        private static void RemoveFaultyBaseHoverchairPatches(Harmony harmony)
        {
            MethodInfo capacityMethod = AccessTools.Method(typeof(PawnCapacityUtility),
                nameof(PawnCapacityUtility.CalculateCapacityLevel));
            MethodInfo downedMethod = AccessTools.Method(typeof(Pawn_HealthTracker),
                "MakeDowned");
            MethodInfo postureMethod = AccessTools.Method(typeof(PawnUtility),
                nameof(PawnUtility.GetPosture));

            // Only the hoverchair parts: RimRound's perk capacity postfix and the
            // heavy-pawn downed pose postfix live on the same methods and stay.
            UnpatchRimRound(harmony, capacityMethod,
                "PawnCapacityUtility_CalculateCapacityLevel_AlterForPerksAndSooter",
                HarmonyPatchType.Prefix);
            UnpatchRimRound(harmony, downedMethod,
                "Pawn_HealthTracker_MakeDowned_DropHoverChair");
            UnpatchRimRound(harmony, postureMethod,
                "PawnUtility_GetPosture_AlterPostureIfWearingScooter");
        }

        /// <summary>
        /// Removes one of RimRound's own (RRHarmony) patches from original,
        /// identified by its patch class, leaving any other RimRound patches on
        /// the same method in place. Unpatch(original, type, "RRHarmony") would
        /// strip all of them.
        /// </summary>
        private static void UnpatchRimRound(
            Harmony harmony,
            MethodBase original,
            string patchClassName,
            HarmonyPatchType? onlyType = null)
        {
            if (original == null)
                return;

            HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
            if (info == null)
                return;

            List<HarmonyLib.Patch> matching = new List<HarmonyLib.Patch>();
            void Collect(IEnumerable<HarmonyLib.Patch> patches, HarmonyPatchType type)
            {
                if (onlyType.HasValue && onlyType.Value != type)
                    return;
                matching.AddRange(patches.Where(delegate(HarmonyLib.Patch patch)
                {
                    return patch.owner == "RRHarmony" &&
                        patch.PatchMethod != null &&
                        patch.PatchMethod.DeclaringType != null &&
                        patch.PatchMethod.DeclaringType.Name == patchClassName;
                }));
            }
            Collect(info.Prefixes, HarmonyPatchType.Prefix);
            Collect(info.Postfixes, HarmonyPatchType.Postfix);
            Collect(info.Transpilers, HarmonyPatchType.Transpiler);
            Collect(info.Finalizers, HarmonyPatchType.Finalizer);

            foreach (HarmonyLib.Patch patch in matching)
                harmony.Unpatch(original, patch.PatchMethod);
        }

        private static void RemoveFaultyBaseNotRegalBedPatches(Harmony harmony)
        {
            // RimRound's original 3x3-bed patches force one slot correctly,
            // but both position postfixes always move the result east. Remove
            // those three partial fixes before installing the complete,
            // rotation-aware implementation from this assembly.
            MethodInfo slotCount = AccessTools.PropertyGetter(
                typeof(Building_Bed),
                nameof(Building_Bed.SleepingSlotsCount));
            MethodInfo sleepingSlot = AccessTools.Method(
                typeof(Building_Bed),
                nameof(Building_Bed.GetSleepingSlotPos),
                new Type[] { typeof(int) });
            MethodInfo footSlot = AccessTools.Method(
                typeof(Building_Bed),
                nameof(Building_Bed.GetFootSlotPos),
                new Type[] { typeof(int) });

            UnpatchRimRound(harmony, slotCount,
                "Building_Bed_SleepingSlotsCount_AdjustForRRBed");
            UnpatchRimRound(harmony, sleepingSlot,
                "Building_Bed_GetSleepingSlotPos_AdjustForRRBed");
            UnpatchRimRound(harmony, footSlot,
                "Building_Bed_GetFootSlotPos_AdjustForRRBed");
        }

        private static void InstallHoverchairRenderPatch(Harmony harmony)
        {
            // RimWorld 1.6 moved apparel-node creation out of PawnRenderTree and
            // into DynamicPawnRenderNodeSetup_Apparel. Patch the small Boolean
            // gate instead of the iterator so rejecting a lying hoverchair cannot
            // leave the caller with a null enumerable.
            MethodInfo target = AccessTools.Method(
                typeof(DynamicPawnRenderNodeSetup_Apparel),
                "ShouldAddApparelNode",
                new Type[] { typeof(Apparel) });
            MethodInfo prefix = AccessTools.Method(
                typeof(HoverchairLyingRenderFix),
                nameof(HoverchairLyingRenderFix.Prefix));

            if (target == null || target.ReturnType != typeof(bool) || prefix == null)
            {
                Log.Warning("[RimRound Feed Other] RimWorld's apparel render gate " +
                    "could not be found; hoverchair lying-render suppression was skipped.");
                return;
            }

            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            }
            catch (Exception exception)
            {
                Log.Warning("[RimRound Feed Other] Hoverchair lying-render suppression " +
                    "could not be installed and was skipped: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static void ApplyFoodNetworkPatchMode(bool enabled)
        {
            if (appliedFoodNetworkV2Mode.HasValue &&
                appliedFoodNetworkV2Mode.Value == enabled)
            {
                return;
            }

            Harmony feedOtherHarmony = new Harmony("RimRound.FeedOther");
            bool switchedFromLegacy =
                appliedFoodNetworkV2Mode.HasValue &&
                !appliedFoodNetworkV2Mode.Value && enabled;
            bool switchedToLegacy =
                (!appliedFoodNetworkV2Mode.HasValue ||
                 appliedFoodNetworkV2Mode.Value) && !enabled;

            if (enabled)
            {
                if (switchedFromLegacy)
                {
                    // The classic network may have changed tank volume while
                    // v2 was disabled. Import that live state once before the
                    // old network objects are detached.
                    FoodNetworkV2MapComponent.ImportLegacyStorageFromAllMaps();
                    FoodNetworkV2AutoFeederUtility.ImportLegacyLinksFromAllMaps();
                }

                RemoveLegacyFoodNetworkPatches(feedOtherHarmony);
                FoodNetworkV2FaucetListerUtility.SyncAllMaps(false);
                ResetLegacyManagers();
                FoodNetworkV2MapComponent.MarkAllMapsDirty();
            }
            else
            {
                if (switchedToLegacy)
                {
                    FoodNetworkV2MapComponent.ExportSavedStorageToLegacyAllMaps();
                    FoodNetworkV2AutoFeederUtility.PrepareForLegacyModeAllMaps();
                }

                RestoreLegacyFoodNetworkPatches();
                FoodNetworkV2FaucetListerUtility.SyncAllMaps(true);
                ResetLegacyManagers();
            }

            appliedFoodNetworkV2Mode = enabled;
        }

        private static void ResetLegacyManagers()
        {
            if (Find.Maps == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                FoodTransmitter_NetManager manager =
                    FoodTransmitter_NetManager.For(map);
                if (manager != null)
                {
                    manager.ResetNetManager();
                }
            }
        }

        private static void RestoreLegacyFoodNetworkPatches()
        {
            Harmony legacyHarmony = new Harmony("RRHarmony");
            Harmony remover = new Harmony("RimRound.FeedOther");
            RemoveLegacyFoodNetworkPatches(remover);

            PatchLegacyPrefix(
                legacyHarmony,
                AccessTools.Method(typeof(FoodUtility), "SpawnedFoodSearchInnerScan"),
                AccessTools.Method(
                    typeof(FoodUtility_SpawnedFoodSearchInnerScan_ChangeValidatorToAccountForFoodFaucet),
                    nameof(FoodUtility_SpawnedFoodSearchInnerScan_ChangeValidatorToAccountForFoodFaucet.Prefix)));
            PatchLegacyPrefix(
                legacyHarmony,
                AccessTools.Method(typeof(JobDriver_Ingest), "MakeNewToils"),
                AccessTools.Method(
                    typeof(JobDriver_Ingest_MakeNewToils_AddExceptionForFaucet),
                    nameof(JobDriver_Ingest_MakeNewToils_AddExceptionForFaucet.Prefix)));
            PatchLegacyPrefix(
                legacyHarmony,
                AccessTools.Method(typeof(JobDriver_Ingest), "PrepareToIngestToils"),
                AccessTools.Method(
                    typeof(JobDriver_Ingest_PrepareToIngestToils_AddFaucetSupport),
                    nameof(JobDriver_Ingest_PrepareToIngestToils_AddFaucetSupport.Prefix)));
            PatchLegacyPrefix(
                legacyHarmony,
                AccessTools.Method(
                    typeof(JobDriver_Ingest),
                    nameof(JobDriver_Ingest.TryMakePreToilReservations)),
                AccessTools.Method(
                    typeof(JobDriver_Ingest_TryMakePreToilReservations_AddExceptionForFaucet),
                    nameof(JobDriver_Ingest_TryMakePreToilReservations_AddExceptionForFaucet.Prefix)));
            PatchLegacyPrefix(
                legacyHarmony,
                AccessTools.Method(
                    typeof(FoodUtility),
                    nameof(FoodUtility.GetFinalIngestibleDef)),
                AccessTools.Method(
                    typeof(FoodUtility_GetFinalIngestibleDef_AddFoodFaucetSupport),
                    nameof(FoodUtility_GetFinalIngestibleDef_AddFoodFaucetSupport.Prefix)));

            MethodInfo includes = AccessTools.Method(
                typeof(ThingListGroupHelper),
                nameof(ThingListGroupHelper.Includes),
                new Type[] { typeof(ThingRequestGroup), typeof(ThingDef) });
            MethodInfo includesPostfix = AccessTools.Method(
                typeof(ThingListGroupHelper_Includes_AddSupportForFaucet),
                nameof(ThingListGroupHelper_Includes_AddSupportForFaucet.Postfix));
            if (includes != null && includesPostfix != null)
            {
                legacyHarmony.Patch(
                    includes,
                    postfix: new HarmonyMethod(includesPostfix));
            }
        }

        private static void PatchLegacyPrefix(
            Harmony harmony,
            MethodInfo original,
            MethodInfo prefix)
        {
            if (original != null && prefix != null)
            {
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));
            }
        }

        private static void RemoveLegacyFoodNetworkPatches(Harmony harmony)
        {
            // Food Network v2 performs a targeted faucet candidate pass and a
            // one-serving ingest job. Remove only RimRound's old global faucet
            // hooks, which replaced the private food-search validator and
            // bypassed normal reservations for every dispenser job.
            MethodInfo spawnedFoodScan = AccessTools.Method(
                typeof(FoodUtility),
                "SpawnedFoodSearchInnerScan");
            MethodInfo makeNewToils = AccessTools.Method(
                typeof(JobDriver_Ingest),
                "MakeNewToils");
            MethodInfo prepareToIngest = AccessTools.Method(
                typeof(JobDriver_Ingest),
                "PrepareToIngestToils");
            MethodInfo reserve = AccessTools.Method(
                typeof(JobDriver_Ingest),
                nameof(JobDriver_Ingest.TryMakePreToilReservations));
            MethodInfo getFinalIngestibleDef = AccessTools.Method(
                typeof(FoodUtility),
                nameof(FoodUtility.GetFinalIngestibleDef));
            MethodInfo includes = AccessTools.Method(
                typeof(ThingListGroupHelper),
                nameof(ThingListGroupHelper.Includes),
                new Type[] { typeof(ThingRequestGroup), typeof(ThingDef) });

            UnpatchRimRound(harmony, spawnedFoodScan,
                nameof(FoodUtility_SpawnedFoodSearchInnerScan_ChangeValidatorToAccountForFoodFaucet));
            UnpatchRimRound(harmony, makeNewToils,
                nameof(JobDriver_Ingest_MakeNewToils_AddExceptionForFaucet));
            UnpatchRimRound(harmony, prepareToIngest,
                nameof(JobDriver_Ingest_PrepareToIngestToils_AddFaucetSupport));
            UnpatchRimRound(harmony, reserve,
                nameof(JobDriver_Ingest_TryMakePreToilReservations_AddExceptionForFaucet));
            UnpatchRimRound(harmony, getFinalIngestibleDef,
                nameof(FoodUtility_GetFinalIngestibleDef_AddFoodFaucetSupport));
            UnpatchRimRound(harmony, includes,
                nameof(ThingListGroupHelper_Includes_AddSupportForFaucet));
        }
    }
}
