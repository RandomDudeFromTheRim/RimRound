using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimRound.Comps
{
    public class CompProperties_CorpseBloat : CompProperties
    {
        public int bloatDurationTicks = 300;
        public float blobSpawnRadius = 3f;

        public CompProperties_CorpseBloat()
        {
            compClass = typeof(Comp_CorpseBloat);
        }
    }

    public class Comp_CorpseBloat : ThingComp
    {
        public CompProperties_CorpseBloat Props => (CompProperties_CorpseBloat)props;
        public bool isBloating = false;
        int bloatTick = 0;
        Pawn victim;

        public void StartBloat(Pawn target)
        {
            if (isBloating) return;
            isBloating = true;
            bloatTick = 0;
            victim = target;

            // Try RV2 vore if available
            TryVore(target);

            Messages.Message(
                $"{parent.LabelShort} swells rapidly, its distended belly pulsing with dark energy!",
                new LookTargets(parent),
                MessageTypeDefOf.ThreatBig);
        }

        void TryVore(Pawn target)
        {
            // Check if RV2 is available via reflection
            var rv2VoreManager = AccessTools.TypeByName("RimVore2.VoreInteractionManager");
            if (rv2VoreManager == null)
                return;

            var startMethod = rv2VoreManager.GetMethod("StartVore",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { typeof(Pawn), typeof(Pawn), typeof(bool) },
                null);

            if (startMethod != null && parent is Pawn corpse)
            {
                startMethod.Invoke(null, new object[] { corpse, target, false });
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!isBloating || !parent.Spawned)
                return;

            bloatTick++;
            if (bloatTick < Props.bloatDurationTicks && parent is Pawn corpse)
            {
                // Directly pump weight into the corpse every tick for dramatic visual bloat
                if (bloatTick % 10 == 0)
                {
                    var fnd = corpse.TryGetComp<FullnessAndDietStats_ThingComp>();
                    if (fnd != null && !fnd.Disabled)
                    {
                        // 27 kg per burst, 30 bursts over 5s = 810 kg total
                        fnd.activeWeightGainRequests.Enqueue(
                            new WeightGainRequest(27f, Find.TickManager.TicksGame + 5, Props.bloatDurationTicks, false));
                    }

                    // Also apply sudden weight gain hediff for the visual swelling
                    var sudden = corpse.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_SuddenWeightGain);
                    if (sudden != null)
                        sudden.Severity += 0.1f;
                    else
                    {
                        var s = HediffMaker.MakeHediff(Defs.HediffDefOf.RimRound_SuddenWeightGain, corpse);
                        s.Severity = 0.1f;
                        corpse.health.AddHediff(s);
                    }
                }
                return;
            }

            // Bloat complete - explode
            isBloating = false;

            // Kill the victim if still alive
            if (victim != null && !victim.Dead)
                victim.Kill(new DamageInfo(DamageDefOf.Blunt, 99999f));

            // Kill the corpse
            if (parent is Pawn corpsePawn && !corpsePawn.Dead)
                corpsePawn.Kill(null);

            // Spawn bloated mass
            Map map = parent.Map;
            if (map != null)
            {
                var blob = ThingMaker.MakeThing(ThingDef.Named("RR_BloatedMass"));
                GenSpawn.Spawn(blob, parent.Position, map, Rot4.North, WipeMode.Vanish);

                // Witness thoughts
                var recAtt = victim?.TryGetComp<ThingComp_PawnAttitude>();
                foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                {
                    float dist = (p.Position - parent.Position).LengthHorizontal;
                    if (dist > 30f) continue;

                    var witnessAtt = p.TryGetComp<ThingComp_PawnAttitude>();
                    if (witnessAtt == null) continue;

                    if (witnessAtt.weightOpinion >= WeightOpinion.NeutralPlus)
                        p.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDef.Named("RR_WitnessedBloatedDeath_Aroused"));
                    else
                        p.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDef.Named("RR_WitnessedBloatedDeath_Horror"));
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isBloating, "isBloating");
            Scribe_Values.Look(ref bloatTick, "bloatTick");
            Scribe_References.Look(ref victim, "victim");
        }
    }
}
