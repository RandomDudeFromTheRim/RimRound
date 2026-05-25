using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimRound.Comps
{
    public class MapComp_GluttoniumRadiation : MapComponent
    {
        private struct RadSource
        {
            public Thing thing;
            public float radius;
            public float exposure;
            public int tickInterval;
        }

        List<RadSource> sources = new List<RadSource>();
        int ticksUntilRebuild = 1;

        static ThingDef gluttoniumDef;
        static ThingDef glutBrickDef;
        static ThingDef voidGluttoniumDef;

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
        static ThingDef VoidGluttoniumDef
        {
            get
            {
                if (voidGluttoniumDef == null)
                    voidGluttoniumDef = ThingDef.Named("RR_VoidGluttonium");
                return voidGluttoniumDef;
            }
        }

        public MapComp_GluttoniumRadiation(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (ticksUntilRebuild > 0)
            {
                ticksUntilRebuild--;
                if (ticksUntilRebuild <= 0)
                {
                    RebuildSources();
                    ticksUntilRebuild = 600;
                }
            }

            int curTick = Find.TickManager.TicksGame;

            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var src = sources[i];
                if (src.thing == null || !src.thing.Spawned || src.thing.Destroyed)
                {
                    sources.RemoveAt(i);
                    continue;
                }

                if (curTick % src.tickInterval != 0)
                    continue;

                RadiateFrom(src);
            }
        }

        void RebuildSources()
        {
            sources.Clear();

            for (int i = 0; i < map.listerBuildings.allBuildingsColonist.Count; i++)
            {
                Building b = map.listerBuildings.allBuildingsColonist[i];
                if (b.Stuff == null)
                    continue;

                if (b.Stuff == GluttoniumDef)
                {
                    sources.Add(new RadSource
                    {
                        thing = b,
                        radius = 3f,
                        exposure = 0.002f,
                        tickInterval = 450
                    });
                }
                else if (b.Stuff == GlutBrickDef)
                {
                    sources.Add(new RadSource
                    {
                        thing = b,
                        radius = 2f,
                        exposure = 0.0008f,
                        tickInterval = 600
                    });
                }
                else if (b.Stuff == VoidGluttoniumDef)
                {
                    sources.Add(new RadSource
                    {
                        thing = b,
                        radius = 4f,
                        exposure = 0.006f,
                        tickInterval = 350
                    });
                }
            }

            var allThings = map.listerThings.AllThings;
            if (allThings == null) return;

            for (int i = 0; i < allThings.Count; i++)
            {
                Thing t = allThings[i];
                if (t == null || !t.Spawned) continue;

                switch (t.def.defName)
                {
                    case "RR_GluttoniumOre":
                        sources.Add(new RadSource { thing = t, radius = 4f, exposure = 0.005f, tickInterval = 250 });
                        break;
                    case "RR_VoidGluttoniumOre":
                        sources.Add(new RadSource { thing = t, radius = 5f, exposure = 0.015f, tickInterval = 200 });
                        break;
                    case "RR_BlobWall":
                        sources.Add(new RadSource { thing = t, radius = 1.5f, exposure = 0.002f, tickInterval = 500 });
                        break;
                    case "RR_BlobWallMineable":
                        sources.Add(new RadSource { thing = t, radius = 3f, exposure = 0.008f, tickInterval = 250 });
                        break;
                }
            }
        }

        void RadiateFrom(RadSource src)
        {
            List<Thing> pawns = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
            if (pawns == null) return;

            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i] is not Pawn pawn || !pawn.RaceProps.Humanlike || pawn.Dead)
                    continue;

                float dist = (pawn.Position - src.thing.Position).LengthHorizontal;
                if (dist > src.radius) continue;

                float attenuation = 1f - (dist / src.radius);
                if (attenuation <= 0) continue;

                float protection = GetGluttoniumProtection(pawn);
                if (protection >= 1f) continue;

                float exposure = src.exposure * attenuation * (1f - protection);

                var h = RimRound.Utilities.HediffUtility.GetHediffOfDefFrom(Defs.HediffDefOf.RR_GluttoniumExposure, pawn);
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
            if (pawn.apparel == null) return 0;
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
