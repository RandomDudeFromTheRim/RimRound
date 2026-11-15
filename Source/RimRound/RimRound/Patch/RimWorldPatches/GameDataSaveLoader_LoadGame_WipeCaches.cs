using HarmonyLib;
using RimRound.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// Wipes all static caches on load.
    /// </summary>
    [HarmonyPatch(typeof(GameDataSaveLoader))]
    [HarmonyPatch(nameof(GameDataSaveLoader.LoadGame), new Type[] { typeof(string) })]
    public class GameDataSaveLoader_LoadGame_WipeCaches
    {
        public static void Prefix() => ClearTransientPawnState();

        internal static void ClearTransientPawnState()
        {
            SoundUtility.ClearPawnSoundCaches();
            Hediffs.Hediff_RRConstricted.Victims.Clear();
        }

        public static void Postfix()
        {
            BodyTypeUtility.InvalidateCorpseCache();
            RacialBodyTypeInfoUtility.InvalidateCaches();
            PawnRenderer_GetBodyPos_HideBlankets.InvalidateCaches();
            Corpse_CurRotDrawMode_Get_ChangeDessicatedCorpseBodyTypeToNonRR.InvalidateCache();
            ApparelGraphicRecordGetter_TryGetGraphicApparel_UseTransparentImagesForBadTex.InvalidateCache();
        }
    }

    [HarmonyPatch(typeof(Game), "InitNewGame")]
    internal static class Game_InitNewGame_WipePawnCaches
    {
        static void Prefix() => GameDataSaveLoader_LoadGame_WipeCaches.ClearTransientPawnState();
    }
}
