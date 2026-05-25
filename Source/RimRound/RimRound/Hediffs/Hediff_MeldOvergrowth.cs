using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimRound.Hediffs
{
    public class Hediff_MeldOvergrowth : Hediff
    {
        // Meld flesh acts as a damage buffer. When the pawn takes blunt/sharp/cut damage,
        // the overgrowth severity drops instead of the pawn losing HP.
        // If severity reaches 0, the meld is shaken off and the pawn is safe.

        public override void Tick()
        {
            base.Tick();
            if (pawn == null || pawn.Dead)
                return;

            if (!pawn.IsHashIntervalTick(60))
                return;

            // Gradually decrease overgrowth as meld is processed
            var meldGrowth = pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RR_MeldGrowth);
            if (meldGrowth == null || meldGrowth.Severity <= 0)
            {
                Severity -= 0.05f;
                if (Severity <= 0)
                    pawn.health.RemoveHediff(this);
            }
        }

        // Called from a Harmony patch on Pawn.TakeDamage - reduces overgrowth instead of HP
        public float AbsorbDamage(float amount, DamageDef def)
        {
            if (def.isExplosive || def == DamageDefOf.Burn || def == DamageDefOf.Flame || def == DamageDefOf.SurgicalCut)
                return 0;

            float reduction = amount * 0.3f;
            Severity = System.Math.Max(0, Severity - reduction * 0.1f);
            return reduction;
        }

        public override string TipStringExtra
        {
            get
            {
                return $"Meld saturation: {Severity * 50f:F0}%\nThe melded flesh moves beneath my skin.";
            }
        }
    }
}
