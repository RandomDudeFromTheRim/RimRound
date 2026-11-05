using HarmonyLib;
using System;
using System.Reflection;
using Verse;

namespace RimRound.Patch
{
    /// <summary>
    /// Intimacy - Friends n' Lovers ships its debug logging switch ON
    /// (<c>IntimacySettings.logging = true</c>), so every failed job-giver pass writes a
    /// line to Player.log: hundreds of them a session, for a mod that is working fine.
    /// Its own setting is the right owner, so this turns that setting off once the game is
    /// up, and does nothing at all if the mod isn't installed.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class IntimacyLoggingOff
    {
        static IntimacyLoggingOff()
        {
            LongEventHandler.ExecuteWhenFinished(TryDisable);
        }

        static void TryDisable()
        {
            try
            {
                Type holder = AccessTools.TypeByName("LoveyDoveySexWithEuterpe.IntimacyALovinExpansion");
                if (holder == null)
                    return;

                object settings = AccessTools.Field(holder, "settings")?.GetValue(null);
                if (settings == null)
                    return;

                FieldInfo logging = AccessTools.Field(settings.GetType(), "logging");
                if (logging == null || logging.FieldType != typeof(bool))
                    return;

                if (logging.GetValue(settings) is bool on && on)
                {
                    logging.SetValue(settings, false);
                    Log.Message("[RimRound] Intimacy's debug logging was on; turned it off so it stops flooding Player.log.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[RimRound] Could not turn off Intimacy's debug logging: " + e.Message);
            }
        }
    }
}
