using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimRound.Rituals
{
    /// <summary>
    /// Constrictor binding: a psychic ritual over a constrictor lure that calls a
    /// gorge constrictor up out of the ground and binds it to the colony. A good
    /// ritual leaves a bound constrictor to use on anyone; a poor one lets a wild,
    /// hostile one loose.
    /// </summary>
    public class PsychicRitualDef_RRBindConstrictor : PsychicRitualDef_InvocationCircle
    {
        public SimpleCurve wildChanceFromQualityCurve;

        public override List<PsychicRitualToil> CreateToils(PsychicRitual psychicRitual, PsychicRitualGraph graph)
        {
            List<PsychicRitualToil> list = base.CreateToils(psychicRitual, graph);
            list.Add(new PsychicRitualToil_RRBindConstrictor(InvokerRole));
            return list;
        }

        public override TaggedString OutcomeDescription(FloatRange qualityRange, string qualityNumber, PsychicRitualRoleAssignments assignments)
        {
            return outcomeDescription.Formatted(wildChanceFromQualityCurve.Evaluate(qualityRange.min).ToStringPercent());
        }
    }

    public class PsychicRitualToil_RRBindConstrictor : PsychicRitualToil
    {
        PsychicRitualRoleDef invokerRole;

        protected PsychicRitualToil_RRBindConstrictor()
        {
        }

        public PsychicRitualToil_RRBindConstrictor(PsychicRitualRoleDef invokerRole)
        {
            this.invokerRole = invokerRole;
        }

        public override void Start(PsychicRitual psychicRitual, PsychicRitualGraph parent)
        {
            base.Start(psychicRitual, parent);
            Pawn invoker = psychicRitual.assignments.FirstAssignedPawn(invokerRole);
            float wildChance = ((PsychicRitualDef_RRBindConstrictor)psychicRitual.def).wildChanceFromQualityCurve.Evaluate(psychicRitual.PowerPercent);
            psychicRitual.ReleaseAllPawnsAndBuildings();
            if (invoker != null)
                ApplyOutcome(psychicRitual, invoker, wildChance);
        }

        void ApplyOutcome(PsychicRitual psychicRitual, Pawn invoker, float wildChance)
        {
            Map map = psychicRitual.Map;
            IntVec3 at = psychicRitual.assignments.Target.Cell;
            FleshbeastUtility.MeatSplatter(3, at, map, FleshbeastUtility.MeatExplosionSize.Normal);
            FilthMaker.TryMakeFilth(at, map, ThingDefOf.Filth_Blood, 3);

            string title = "PsychicRitualCompleteLabel".Translate(psychicRitual.def.label);
            if (Rand.Chance(wildChance))
            {
                Pawn beast = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("RR_GorgeConstrictor"), Faction.OfEntities);
                GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(at, map, 2), map);
                Find.LetterStack.ReceiveLetter(title,
                    $"The binding slipped. A gorge constrictor heaves itself up out of the ground where the lure lay, but nothing holds it - it is wild, hungry, and already looking for someone to wrap itself around.\n\nKill it or tear it off whoever it catches before it pumps them until they burst.",
                    LetterDefOf.ThreatBig, new LookTargets(beast));
                return;
            }

            Thing bound = ThingMaker.MakeThing(ThingDef.Named("RR_BoundConstrictor"));
            GenPlace.TryPlaceThing(bound, at, map, ThingPlaceMode.Near);
            Find.LetterStack.ReceiveLetter(title,
                $"A gorge constrictor heaves itself up out of the ground where the lure lay and coils tight around the shard. {invoker.LabelShort}'s will holds it: the beast is bound.\n\nA colonist can set it on anyone. It wraps around them and pumps them full, but it is leashed - it will never make anyone burst. When they can't take any more, it lets go, sated, ready to be used again.",
                LetterDefOf.PositiveEvent, new LookTargets(bound));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref invokerRole, "invokerRole");
        }
    }
}
