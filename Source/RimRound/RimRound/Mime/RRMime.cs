using System.Linq;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace RimRound.Mime
{
    /// <summary>
    /// Alpha Animals' mime, the RimRound way (replaces its HediffComp_Mime on AA_MimeHediff;
    /// 1.6/ExternalMods/Mime, loaded with Alpha Animals and Biotech).
    ///
    /// A mime is a psionic parasite passing as a wanderer. Underneath it's Animalistic
    /// (WeightOpinion.Extreme), but it passes for Fanatical, and after a while it turns
    /// ravenous. Fed until it reaches Gelatinous I it stops hiding - it becomes a mime
    /// (the RR_Mime xenotype) and stays, happy, as long as it stays that big. Starved
    /// while hiding, or shrunk back below Gelatinous I after showing itself, it turns
    /// feral: a superpowered, permanently hostile mime.
    /// </summary>
    public class HediffCompProperties_RRMime : HediffCompProperties
    {
        /// <summary>Checks (every 250 ticks) spent malnourished before a hiding mime turns.</summary>
        public int malnutritionTrigger = 30;
        public int minToGetHungry = 60000;
        public int maxToGetHungry = 1800000;

        public HediffCompProperties_RRMime()
        {
            compClass = typeof(HediffComp_RRMime);
        }
    }

    public enum MimeState : byte
    {
        Concealed,
        Revealed,
        Feral,
    }

    public class HediffComp_RRMime : HediffComp
    {
        const int CheckTicks = 250;

        public MimeState state = MimeState.Concealed;
        int ticks;
        int hungerTicks;
        int ticksToGetHungry = -1;
        int malnutritionLeft = -1;

        public HediffCompProperties_RRMime Props => (HediffCompProperties_RRMime)props;

        public override void CompPostMake()
        {
            base.CompPostMake();
            malnutritionLeft = Props.malnutritionTrigger;
            ticksToGetHungry = Rand.RangeInclusive(Props.minToGetHungry, Props.maxToGetHungry);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref state, "state", MimeState.Concealed);
            Scribe_Values.Look(ref hungerTicks, "hungerTicks");
            Scribe_Values.Look(ref ticksToGetHungry, "ticksToGetHungry", -1);
            Scribe_Values.Look(ref malnutritionLeft, "malnutritionLeft", -1);
        }

        public override string CompLabelInBracketsExtra => state == MimeState.Concealed ? null : state.ToString().ToLower();

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            Pawn p = Pawn;
            if (p == null || p.Dead)
                return;
            ticks += delta;
            if (ticks < CheckTicks)
                return;
            int elapsed = ticks;
            ticks = 0;
            if (ticksToGetHungry < 0)
                CompPostMake();

            switch (state)
            {
                case MimeState.Concealed:
                    RRMimeUtility.KeepDisguise(p);
                    hungerTicks += elapsed;
                    if (hungerTicks >= ticksToGetHungry)
                        RRMimeUtility.SetHunger(p, revealed: false);
                    if (RRMimeUtility.BigEnough(p))
                    {
                        RRMimeUtility.Reveal(p, this);
                        return;
                    }
                    if (p.health.hediffSet.HasHediff(RimWorld.HediffDefOf.Malnutrition) && !p.Downed && p.Awake() && --malnutritionLeft <= 0)
                        RRMimeUtility.GoFeral(p, this, starved: true);
                    break;

                case MimeState.Revealed:
                    RRMimeUtility.KeepAnimalistic(p);
                    if (!RRMimeUtility.BigEnough(p))
                        RRMimeUtility.GoFeral(p, this, starved: false);
                    break;

                case MimeState.Feral:
                    // carried off the map when it turned: finish turning once it's back on one
                    if (p.Spawned && p.Faction == Faction.OfPlayer)
                        RRMimeUtility.GoFeral(p, this, starved: false);
                    break;
            }
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            // died still hiding: the body gives it away
            if (state == MimeState.Concealed && Pawn.Corpse?.Spawned == true)
            {
                RRMimeUtility.ApplyXenotype(Pawn);
                Messages.Message($"{Pawn.LabelShort}'s body sloughs its human face as it dies. It was a mime.", Pawn.Corpse, MessageTypeDefOf.NeutralEvent);
            }
        }
    }

    public static class RRMimeUtility
    {
        static HediffDef mimeHediff;
        static bool looked;

        static HediffDef MimeHediff
        {
            get
            {
                if (!looked)
                {
                    looked = true;
                    mimeHediff = DefDatabase<HediffDef>.GetNamedSilentFail("AA_MimeHediff");
                }
                return mimeHediff;
            }
        }

        public static XenotypeDef Xenotype => DefDatabase<XenotypeDef>.GetNamedSilentFail("RR_Mime");

        public static HediffComp_RRMime CompOf(Pawn p)
        {
            if (p?.health?.hediffSet == null || MimeHediff == null)
                return null;
            return (p.health.hediffSet.GetFirstHediffOfDef(MimeHediff) as HediffWithComps)?.GetComp<HediffComp_RRMime>();
        }

        public static bool IsConcealed(Pawn p) => CompOf(p)?.state == MimeState.Concealed;

        /// <summary>Gelatinous I or bigger: the size a mime stops hiding at, and must stay at.</summary>
        public static bool BigEnough(Pawn p) => BodyTypeUtility.PawnIsOverWeightThreshold(p, Defs.BodyTypeDefOf.F_090_Titanic);

        /// <summary>Animalistic underneath, Fanatical on the surface.</summary>
        public static void KeepDisguise(Pawn p)
        {
            var att = p.TryGetComp<ThingComp_PawnAttitude>();
            if (att == null || p.story?.traits == null)
                return;
            att.weightOpinion = WeightOpinion.Extreme;
            TraitDef fanatical = Defs.TraitDefOf.RR_WeightOpinion_Fanatical_Trait;
            if (!p.story.traits.HasTrait(fanatical))
            {
                WeightOpinionUtility.RemoveWeightOpinionTraits(p);
                p.story.traits.GainTrait(new Trait(fanatical));
            }
        }

        public static void KeepAnimalistic(Pawn p)
        {
            var att = p.TryGetComp<ThingComp_PawnAttitude>();
            if (att == null || p.story?.traits == null)
                return;
            if (att.weightOpinion != WeightOpinion.Extreme || !p.story.traits.HasTrait(Defs.TraitDefOf.RR_WeightOpinion_Animalistic_Trait))
                att.SetWeightOpinion(WeightOpinion.Extreme);
        }

        /// <summary>Ravenous: hidden while it hides, shown once it doesn't.</summary>
        public static void SetHunger(Pawn p, bool revealed)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("RR_MimeHunger");
            if (def == null)
                return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(def) ?? p.health.AddHediff(def);
            h.Severity = revealed ? 1f : 0.5f;
        }

        /// <summary>Its true genes and face (the RR_Mime xenotype).</summary>
        public static void ApplyXenotype(Pawn p)
        {
            XenotypeDef x = Xenotype;
            if (x == null || p.genes == null || p.genes.Xenotype == x)
                return;
            p.genes.SetXenotype(x);
            p.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        /// <summary>Fed to Gelatinous I: it shows itself, and it's happy.</summary>
        public static void Reveal(Pawn p, HediffComp_RRMime comp)
        {
            comp.state = MimeState.Revealed;
            ApplyXenotype(p);
            KeepAnimalistic(p);
            SetHunger(p, revealed: true);
            if (p.Spawned)
                FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 2f, new Color(0.25f, 0.25f, 0.28f));
            Find.LetterStack.ReceiveLetter("Mime revealed",
                $"{p.LabelShort} was never quite human. As {p.ProSubj()} swelled past Gelatinous, the face {p.ProSubj()} wore sloughed away like a shed skin: {p.LabelShort} is a mime - a psionic parasite that passes for one of us and lives off our food.\n\nFed this full, it has nothing left to hide and no reason to. It is the happiest creature in the colony, and it intends to stay that way.\n\nKeep it that way. If {p.LabelShort} ever shrinks back below Gelatinous I, the hunger will win - and a mime that has stopped pretending will turn on us for good.",
                LetterDefOf.NeutralEvent, new LookTargets(p));
        }

        /// <summary>Starved while hiding, or shrunk after showing itself: a feral mime, for good.</summary>
        public static void GoFeral(Pawn p, HediffComp_RRMime comp, bool starved, bool quiet = false)
        {
            comp.state = MimeState.Feral;
            ApplyXenotype(p);
            KeepAnimalistic(p);
            SetHunger(p, revealed: true);
            HediffDef feral = DefDatabase<HediffDef>.GetNamedSilentFail("RR_MimeFeral");
            if (feral != null && !p.health.hediffSet.HasHediff(feral))
                p.health.AddHediff(feral);
            if (p.guest != null)
                p.guest.Recruitable = false;
            if (!p.Spawned)
                return;   // finishes turning once it's back on a map (HediffComp_RRMime)

            Map map = p.Map;
            Faction hostile = Find.FactionManager.FirstFactionOfDef(FactionDefOf.AncientsHostile);
            if (hostile != null && p.Faction != hostile)
            {
                p.jobs?.StopAll();
                if (p.IsPrisoner)
                    p.guest.SetGuestStatus(null);
                p.SetFaction(hostile);
                p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ChangedFaction);
                LordMaker.MakeNewLord(hostile, new LordJob_AssaultColony(hostile, canKidnap: false, canTimeoutOrFlee: false, sappers: false, useAvoidGridSmart: false, canSteal: false), map, new[] { p });
            }
            for (int i = 0; i < 12; i++)
                if (CellFinder.TryFindRandomReachableNearbyCell(p.Position, map, 2f, TraverseParms.For(TraverseMode.NoPassClosedDoors), null, null, out IntVec3 c))
                    FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Blood);
            DefDatabase<SoundDef>.GetNamedSilentFail("Hive_Spawn")?.PlayOneShot(new TargetInfo(p.Position, map));
            if (quiet)
                return;
            Find.TickManager.slower.SignalForceNormalSpeedShort();
            Find.LetterStack.ReceiveLetter("Mime!",
                starved
                    ? $"{p.LabelShort} has gone hungry for too long. The face {p.ProSubj()} wore splits and peels away: {p.LabelShort} is a mime - a psionic parasite that was living off our food - and with the food gone, it has come for us.\n\nIt will never be one of us again."
                    : $"{p.LabelShort} has shrunk back below Gelatinous I, and the hunger has won. The mime that was so content among us is gone; what's left is starving, furious and far stronger than any of us.\n\nIt will never be one of us again.",
                LetterDefOf.ThreatBig, new LookTargets(p));
        }
    }
}
