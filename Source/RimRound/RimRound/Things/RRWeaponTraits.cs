using HarmonyLib;
using RimRound.AI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Things
{
    /// <summary>Settings for <see cref="DamageWorker_RRGluttoniumPuff"/>.</summary>
    public class RRGluttoniumPuffExtension : DefModExtension
    {
        /// <summary>How long the swelling lasts before it wears off; 0 keeps it for good.</summary>
        public int durationTicks = 30000;
        public float permanentFraction = 0.1f;
        public Color dropletColor = new Color(0.95f, 0.55f, 0.75f);
    }

    /// <summary>
    /// A gluttonium puff: no harm, just a sudden swell of weight (the damage amount, in
    /// kilos) that mostly wears off, with a little that stays. Used as an extra damage by
    /// the gluttonium-rounds weapon traits, so it rides along with an ordinary bullet.
    /// </summary>
    public class DamageWorker_RRGluttoniumPuff : DamageWorker
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            var result = new DamageResult();
            if (!(victim is Pawn p) || p.Dead || !p.RaceProps.Humanlike || Utilities.HediffUtility.WeightHediff(p) == null)
                return result;

            var ext = def.GetModExtension<RRGluttoniumPuffExtension>() ?? new RRGluttoniumPuffExtension();
            float kilos = dinfo.Amount;
            float kept = kilos * ext.permanentFraction;
            Utilities.HediffUtility.QueueWeightGain(p, kilos - kept, ext.durationTicks);
            if (kept > 0f)
                Utilities.HediffUtility.QueueWeightGain(p, kept);

            if (p.Spawned)
            {
                for (int i = 0; i < 3; i++)
                    CloseContactFlecks.Droplet(p.DrawPos + new Vector3(0f, 0f, 0.3f), p.Map, ext.dropletColor);
                FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 0.7f, ext.dropletColor);
            }
            return result;
        }
    }

    /// <summary>
    /// A persona that gorges on violence and passes the meal on: whenever its wielder
    /// kills or downs someone with it, the wielder is fed - nutrition, a few kilos, and a
    /// warm, sated feeling.
    /// </summary>
    public class WeaponTraitWorker_RRGorging : WeaponTraitWorker
    {
        public override void Notify_KilledPawn(Pawn pawn)
        {
            base.Notify_KilledPawn(pawn);
            Feed(pawn, 1f);
        }

        public static void Feed(Pawn wielder, float portion)
        {
            if (wielder == null || wielder.Dead)
                return;
            if (wielder.needs?.food != null)
                wielder.needs.food.CurLevel += wielder.needs.food.MaxLevel * 0.35f * portion;
            Utilities.HediffUtility.QueueWeightGain(wielder, Rand.Range(1.5f, 3f) * portion);

            ThoughtDef fed = DefDatabase<ThoughtDef>.GetNamedSilentFail("RR_PersonaFed");
            if (fed != null)
                wielder.needs?.mood?.thoughts?.memories?.TryGainMemory(fed);
            if (wielder.Spawned)
                FleckMaker.ThrowMetaIcon(wielder.Position, wielder.Map, FleckDefOf.Heart);
        }

        /// <summary>The wielder's primary weapon, if it carries a gorging persona.</summary>
        public static bool WieldsGorging(Pawn p, DamageInfo dinfo)
        {
            ThingWithComps weapon = p?.equipment?.Primary;
            var bladelink = weapon?.TryGetComp<CompBladelinkWeapon>();
            if (bladelink == null || (dinfo.Weapon != null && dinfo.Weapon != weapon.def))
                return false;
            var traits = bladelink.TraitsListForReading;
            for (int i = 0; i < traits.Count; i++)
                if (traits[i].workerClass == typeof(WeaponTraitWorker_RRGorging))
                    return true;
            return false;
        }
    }

    /// <summary>A gorging persona feeds its wielder for downing someone too, not only killing.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Pawn_HealthTracker_MakeDowned_RRGorging
    {
        public static void Postfix(DamageInfo? dinfo)
        {
            if (dinfo?.Instigator is Pawn attacker && WeaponTraitWorker_RRGorging.WieldsGorging(attacker, dinfo.Value))
                WeaponTraitWorker_RRGorging.Feed(attacker, 0.6f);
        }
    }
}
