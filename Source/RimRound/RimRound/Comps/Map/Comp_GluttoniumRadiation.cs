using RimRound.Hediffs;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimRound.Comps
{
    public class Comp_GluttoniumRadiation : ThingComp
    {
        public CompProperties_GluttoniumRadiation Props => (CompProperties_GluttoniumRadiation)props;

        int tickCounter = 0;

        static ThingDef gluttoniumDef;
        static ThingDef glutBrickDef;
        static ThingDef GluttoniumDef
        {
            get
            {
                if (gluttoniumDef == null)
                    gluttoniumDef = ThingDef.Named("RR_Gluttonium");
                return gluttoniumDef;
            }
        }
        static ThingDef GlutBrickDef
        {
            get
            {
                if (glutBrickDef == null)
                    glutBrickDef = ThingDef.Named("RR_GlutBrick");
                return glutBrickDef;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            tickCounter++;
            if (tickCounter < Props.tickInterval)
                return;
            tickCounter = 0;

            if (!parent.Spawned)
                return;

            // Buildings made of gluttonium or glut brick stuff radiate weakly;
            // ore (non-stuff buildings with the comp) always radiate
            if (parent is Building b && b.Stuff != null && b.Stuff != GluttoniumDef && b.Stuff != GlutBrickDef)
                return;

            RadiateWeightGain();
        }

        void RadiateWeightGain()
        {
            List<Thing> things = parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
            if (things == null)
                return;

            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is not Pawn pawn || !pawn.RaceProps.Humanlike || pawn.Dead)
                    continue;

                float dist = (pawn.Position - parent.Position).LengthHorizontal;
                if (dist > Props.radius)
                    continue;

                float attenuation = 1f - (dist / Props.radius);
                if (attenuation <= 0)
                    continue;

                float protection = GetGluttoniumProtection(pawn);
                if (protection >= 1f)
                    continue;

                float exposure = Props.exposurePerTick * attenuation * (1f - protection);

                Hediff h = RimRound.Utilities.HediffUtility.GetHediffOfDefFrom(Defs.HediffDefOf.RR_GluttoniumExposure, pawn);
                if (h == null)
                {
                    h = HediffMaker.MakeHediff(Defs.HediffDefOf.RR_GluttoniumExposure, pawn);
                    h.Severity = exposure;
                    pawn.health.AddHediff(h);
                }
                else
                {
                    h.Severity += exposure;
                }
            }
        }

        static float GetGluttoniumProtection(Pawn pawn)
        {
            if (pawn.apparel == null)
                return 0;

            float best = 0;
            foreach (Apparel a in pawn.apparel.WornApparel)
            {
                float p = a.GetStatValue(StatDef.Named("RR_GluttoniumResistance"));
                if (p > best) best = p;
            }
            return best;
        }
    }
}
