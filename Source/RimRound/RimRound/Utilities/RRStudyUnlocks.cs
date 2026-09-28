using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimRound.Utilities
{
    /// <summary>
    /// What the colony has learned from studying RimRound's anomalies, as keys like
    /// "constrictor_2" or "seam_1". Saved with the game; unlocked by
    /// CompRRStudyPayoffs as study notes come in, read by whatever the knowledge
    /// improves (the constrictor vat's mutations, the void seam).
    /// </summary>
    public class GameComponent_RRStudyUnlocks : GameComponent
    {
        HashSet<string> unlocked = new HashSet<string>();

        public GameComponent_RRStudyUnlocks(Game game) { }

        static GameComponent_RRStudyUnlocks Instance => Current.Game?.GetComponent<GameComponent_RRStudyUnlocks>();

        public static bool Has(string key) => string.IsNullOrEmpty(key) || (Instance?.unlocked.Contains(key) ?? false);

        public static void Unlock(string key)
        {
            if (!string.IsNullOrEmpty(key))
                Instance?.unlocked.Add(key);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref unlocked, "unlocked", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && unlocked == null)
                unlocked = new HashSet<string>();
        }
    }

    public class CompProperties_RRStudyPayoffs : CompProperties_StudyUnlocks
    {
        /// <summary>The key each study note unlocks, by the note's index.</summary>
        public List<string> unlockKeys = new List<string>();

        public CompProperties_RRStudyPayoffs()
        {
            compClass = typeof(CompRRStudyPayoffs);
        }
    }

    /// <summary>Vanilla's study notes, plus a lasting payoff with each one.</summary>
    public class CompRRStudyPayoffs : CompStudyUnlocks
    {
        protected override void Notify_StudyLevelChanged(ChoiceLetter keptLetter)
        {
            base.Notify_StudyLevelChanged(keptLetter);
            var keys = ((CompProperties_RRStudyPayoffs)props).unlockKeys;
            int index = studyProgress - 1;
            if (index >= 0 && index < keys.Count)
                GameComponent_RRStudyUnlocks.Unlock(keys[index]);
        }
    }
}
