using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimRound.Hediffs
{
    /// <summary>
    /// The last few seconds before a pawn bursts (meld aerosol, gorge constrictor):
    /// they swell and tremble (RR_BurstSwell animation) while the gurgling and the
    /// ground-shaking build, then burst in a spray of flesh. Added by
    /// MeldBurstUtility.BeginBurst.
    /// </summary>
    public class Hediff_RRBursting : Hediff
    {
        public const int BuildUpTicks = 180;

        public string message;
        public int bonusGluttonium;
        int startTick = -1;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref message, "message");
            Scribe_Values.Look(ref bonusGluttonium, "bonusGluttonium");
            Scribe_Values.Look(ref startTick, "startTick", -1);
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            startTick = Find.TickManager.TicksGame;
            pawn.Drawer?.renderer?.SetAnimation(Defs.RRAnimationDefOf.RR_BurstSwell);
        }

        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead)
                return;

            int elapsed = Find.TickManager.TicksGame - startTick;
            float t = Mathf.Clamp01((float)elapsed / BuildUpTicks);

            // held rigid and trembling while it happens
            if (pawn.IsHashIntervalTick(30))
                pawn.stances?.stunner?.StunFor(45, pawn, addBattleLog: false, showMote: false);
            if (pawn.Drawer?.renderer != null && pawn.Drawer.renderer.CurAnimation != Defs.RRAnimationDefOf.RR_BurstSwell)
                pawn.Drawer.renderer.SetAnimation(Defs.RRAnimationDefOf.RR_BurstSwell);

            // the gurgling and the shaking build up
            if (pawn.Spawned && elapsed % Mathf.RoundToInt(Mathf.Lerp(45, 12, t)) == 0)
            {
                SoundDef.Named("RR_StomachGurgles_Heavy").PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
                if (pawn.Map == Find.CurrentMap)
                    Find.CameraDriver.shaker.DoShake(0.15f + 0.6f * t);
                FleckMaker.ThrowDustPuffThick(pawn.DrawPos, pawn.Map, 0.8f + 1.2f * t, new Color(0.7f, 0.42f, 0.45f));
            }

            if (elapsed >= BuildUpTicks)
            {
                pawn.health.hediffSet.GetFirstHediff<Hediff_RRConstricted>()?.ConsumeBeast();
                pawn.Drawer?.renderer?.SetAnimation(null);
                MeldBurstUtility.Burst(pawn, message, bonusGluttonium);
            }
        }

        public override string TipStringExtra => "About to burst!";
    }
}
