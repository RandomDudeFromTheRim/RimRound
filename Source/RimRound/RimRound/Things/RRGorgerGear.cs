using System.Linq;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimRound.Things
{
    /// <summary>
    /// On a PawnKindDef: how heavy its pawns are generated (kilos over base weight,
    /// instead of the faction-wide random distribution) and the least they think of
    /// weight. Used by the gorger horde.
    /// </summary>
    public class RRPawnKindWeightExtension : DefModExtension
    {
        public FloatRange kilos = new FloatRange(150f, 250f);
        public WeightOpinion minOpinion = WeightOpinion.None;
    }

    /// <summary>
    /// The bloatpop pack's pop: a burst of fattening gas around the wearer (the
    /// temporary kind, like a flab grenade's - most of it wears off). Friend and foe
    /// alike; the gorgers who carry these don't mind.
    /// </summary>
    public static class BloatpopUtility
    {
        public const float Radius = 3.9f;
        const int GasPerCell = 220;

        public static void Pop(Pawn wearer)
        {
            if (wearer == null || !wearer.Spawned)
                return;
            Map map = wearer.Map;
            var grid = map.GetComponent<MapComp_RRGasGrid>();
            if (grid == null)
                return;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(wearer.Position, Radius, true))
            {
                if (c.InBounds(map) && grid.GasCanMoveTo(c) && GenSight.LineOfSight(wearer.Position, c, map, skipFirstCell: true))
                    grid.AddGas(c, RRGasType.temporaryFatteningGas, GasPerCell);
            }
            FleckMaker.ThrowDustPuffThick(wearer.DrawPos, map, 2.5f, new Color(1f, 0.62f, 0.8f));
            DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_Smoke")?.PlayOneShot(new TargetInfo(wearer.Position, map));
        }
    }

    /// <summary>Pops the pack by hand (drafted, like a smokepop pack).</summary>
    public class Verb_RRBloatpop : Verb
    {
        protected override bool TryCastShot()
        {
            BloatpopUtility.Pop(CasterPawn);
            (ReloadableCompSource as CompApparelReloadable)?.UsedOnce();
            return true;
        }
    }

    /// <summary>
    /// AI use: a non-player wearer pops it when an enemy gets close - the gorgers'
    /// way of saying hello.
    /// </summary>
    public class CompRRBloatpopPack : CompAIUsablePack
    {
        protected override float ChanceToUse(Pawn wearer)
        {
            if (wearer.Faction == Faction.OfPlayer || wearer.Downed || !wearer.Spawned)
                return 0f;
            var reload = parent.TryGetComp<CompApparelReloadable>();
            if (reload != null && reload.RemainingCharges <= 0)
                return 0f;
            bool enemyClose = wearer.Map.mapPawns.AllPawnsSpawned.Any(p =>
                !p.Downed && p.HostileTo(wearer) && p.RaceProps.Humanlike && p.Position.InHorDistOf(wearer.Position, BloatpopUtility.Radius));
            return enemyClose ? 0.5f : 0f;
        }

        protected override void UsePack(Pawn wearer)
        {
            BloatpopUtility.Pop(wearer);
            parent.TryGetComp<CompApparelReloadable>()?.UsedOnce();
        }
    }
}
