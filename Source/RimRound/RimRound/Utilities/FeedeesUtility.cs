using System.Linq;
using RimWorld;
using Verse;

namespace RimRound.Utilities
{
    /// <summary>
    /// The Feedees meme (defName RR_FoodSupremacy, from when it was "Food
    /// Indifference") gates content: a held void echo only gives voidmilk to a
    /// colony that follows it. Without the Ideology DLC nothing is gated.
    /// </summary>
    public static class FeedeesUtility
    {
        public const string MemeDefName = "RR_FoodSupremacy";

        static MemeDef meme;
        static MemeDef Meme => meme ?? (meme = DefDatabase<MemeDef>.GetNamedSilentFail(MemeDefName));

        /// <summary>True if any of the player's ideoligions holds Feedees (or Ideology isn't active).</summary>
        public static bool PlayerFollowsFeedees()
        {
            if (!ModsConfig.IdeologyActive || Meme == null)
                return true;
            return Faction.OfPlayer?.ideos?.AllIdeos.Any(ideo => ideo.HasMeme(Meme)) ?? false;
        }
    }
}
