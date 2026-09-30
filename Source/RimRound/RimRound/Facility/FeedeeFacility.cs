using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace RimRound.Facility
{
    /// <summary>
    /// The derelict feedee facility (Odyssey + Anomaly; defs in 1.6/ExternalMods/FeedeeFacility):
    /// a gravcore signal from an ancient feeding facility whose bound gorge constrictors got
    /// loose, bred, and overran it. Its gravcore powers the feeding reactor at its heart.
    /// </summary>
    public class QuestNode_Root_Gravcore_RRFeedeeFacility : QuestNode_Root_Gravcore
    {
        const string SitePartName = "RR_DerelictFeedeeFacility";

        protected override bool TestRunInt(Slate slate) =>
            ModsConfig.AnomalyActive && DefDatabase<SitePartDef>.GetNamedSilentFail(SitePartName) != null && base.TestRunInt(slate);

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;
            if (!TryFindSiteTile(out PlanetTile tile))
            {
                Log.Error("[RimRound] Could not find a site tile for the derelict feedee facility.");
                return;
            }
            string generated = QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated");
            string removed = QuestGenUtility.HardcodedSignalWithQuestID("site.MapRemoved");
            float points = slate.Get("points", 0f);
            Site site = QuestGen_Sites.GenerateSite(new[]
            {
                new SitePartDefWithParams(DefDatabase<SitePartDef>.GetNamed(SitePartName), new SitePartParams { points = points, threatPoints = points })
            }, tile, null, hiddenSitePartsPossible: false, null, WorldObjectDefOf.ClaimableSite);
            slate.Set("site", site);
            quest.SpawnWorldObject(site);

            QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice();
            choice.rewards.Add(new Reward_DefinedThingDef(ThingDefOf.Gravcore));
            choice.rewards.Add(new Reward_DefinedThingDef(ThingDefOf.GravlitePanel));
            quest.RewardChoice().choices.Add(choice);

            quest.Letter(LetterDefOf.NeutralEvent, generated, null, null, null, useColonistsFromCaravanArg: false, QuestPart.SignalListenMode.OngoingOnly,
                label: "Derelict feedee facility",
                text: "The facility's walls are slick with dried slurry, and something heavy is sliding around behind them.\n\nIts feeding reactor still runs on a gravcore, and still leaks: a void surge hangs over the whole site - anyone outdoors swells slowly. Hack the reactor or burst it to take the gravcore and end the surge.\n\nThe gorge constrictors the facility once kept have bred. Their broods nest in its rooms, slow and swollen with slurry: keep your people together and tear off anything that latches on. Destroy the nests to stop them hatching more. Meld hunter drones still guard the halls.",
                lookTargets: Gen.YieldSingle(site.Map));
            quest.End(QuestEndOutcome.Success, 0, null, generated);
            quest.End(QuestEndOutcome.Unknown, 0, null, removed);
        }
    }

    /// <summary>
    /// Lays out the facility like Odyssey's ancient reactor site: one main complex holding the
    /// feeding reactor (the gravcore), and ruined outbuildings around it. Once the map exists,
    /// each brood nest hatches its first brood, scaled by the site's threat points.
    /// </summary>
    public class GenStep_RRFeedeeFacility : GenStep_LargeRuins
    {
        const string MainLayout = "RR_FeedeeFacility_Main";
        const string OutbuildingLayout = "RR_FeedeeFacility_Outbuildings";
        public const string NestDef = "RR_ConstrictorBroodNest";

        bool placedMain;

        public override int SeedPart => 480113207;
        protected override int RegionSize => 45;
        protected override FloatRange DefaultMapFillPercentRange => new FloatRange(0.45f, 0.6f);
        protected override FloatRange MergeRange => new FloatRange(1f, 1f);
        protected override int MoveRangeLimit => 6;
        protected override int ContractLimit => 6;
        protected override int MinRegionSize => 15;
        protected override IntRange RuinsMinMaxRange => new IntRange(2, 5);
        protected override LayoutDef LayoutDef => DefDatabase<LayoutDef>.GetNamed(OutbuildingLayout);
        protected override Faction Faction => Faction.OfAncientsHostile;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!ModsConfig.OdysseyActive || !ModsConfig.AnomalyActive)
                return;
            placedMain = false;
            base.Generate(map, parms);
        }

        protected override LayoutStructureSketch GenerateAndSpawn(CellRect rect, Map map, GenStepParams parms, LayoutDef layoutDef)
        {
            if (!placedMain)
            {
                placedMain = true;
                layoutDef = DefDatabase<LayoutDef>.GetNamed(MainLayout);
                MapGenerator.SetVar("SpawnRect", rect.ExpandedBy(1));
            }
            return base.GenerateAndSpawn(rect, map, parms, layoutDef);
        }

        public override void PostMapInitialized(Map map, GenStepParams parms)
        {
            ThingDef nestDef = DefDatabase<ThingDef>.GetNamedSilentFail(NestDef);
            if (nestDef == null)
                return;
            List<Thing> nests = map.listerThings.ThingsOfDef(nestDef).ToList();
            if (nests.Count == 0)
                return;
            float points = parms.sitePart?.parms?.threatPoints ?? 500f;
            // an infestation, not a carpet: keep a few nests, more at higher threat
            int keep = Mathf.Clamp(3 + Mathf.RoundToInt(points / 250f), 4, 10);
            foreach (Thing extra in nests.InRandomOrder().Skip(keep).ToList())
            {
                nests.Remove(extra);
                extra.Destroy();
            }
            // most of the threat is the brood, split between the nests: at least one each
            float perNest = Mathf.Max(35f, points * 0.6f / nests.Count);
            foreach (Thing nest in nests)
                nest.TryGetComp<CompSpawnerPawn>()?.SpawnPawnsUntilPoints(perNest);
        }
    }

    /// <summary>The facility's heart: the feeding reactor, which holds the gravcore.</summary>
    public class RoomContents_RRFeedingReactor : RoomContentsWorker
    {
        public const string ReactorDef = "RR_FeedingReactor";

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            CellRect rect = room.rects.Where(r => r.Width >= 6 && r.Height >= 6).OrderByDescending(r => r.Area).FirstOrDefault();
            if (rect == default(CellRect))
            {
                Log.Error("[RimRound] No room large enough for the feeding reactor.");
                return;
            }
            IntVec3 shift = new IntVec3(rect.Width % 2 == 0 ? 1 : 0, 0, rect.Height % 2 == 0 ? 1 : 0);
            Thing reactor = ThingMaker.MakeThing(ThingDef.Named(ReactorDef));
            reactor.SetFaction(faction ?? Faction.OfAncientsHostile);
            GenSpawn.Spawn(reactor, rect.CenterCell - shift, map, Rot4.North);
            base.FillRoom(map, room, faction, threatPoints);
            // years of leaks
            foreach (IntVec3 c in rect.Cells.InRandomOrder().Take(rect.Area / 6))
                if (c.Standable(map))
                    FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Vomit);
        }
    }

    /// <summary>A room the brood took over: one or two nests, and the mess they make.</summary>
    public class RoomContents_RRBroodNest : RoomContentsWorker
    {
        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            base.FillRoom(map, room, faction, threatPoints);
            ThingDef nestDef = ThingDef.Named(GenStep_RRFeedeeFacility.NestDef);
            int want = room.Area >= 100 ? 2 : 1;
            int placed = 0;
            foreach (CellRect rect in room.rects.OrderByDescending(r => r.Area))
            {
                foreach (IntVec3 c in rect.ContractedBy(1).Cells.InRandomOrder())
                {
                    if (placed >= want)
                        break;
                    if (!FacilityGen.Fits(nestDef, c, Rot4.North, map))
                        continue;
                    Thing nest = ThingMaker.MakeThing(nestDef);
                    nest.SetFaction(Faction.OfEntities);
                    GenSpawn.Spawn(nest, c, map, Rot4.North);
                    placed++;
                }
            }
            foreach (IntVec3 c in room.rects.SelectMany(r => r.Cells).InRandomOrder().Take(room.Area / 5))
                if (c.Standable(map))
                    FilthMaker.TryMakeFilth(c, map, Rand.Chance(0.6f) ? ThingDefOf.Filth_Vomit : ThingDefOf.Filth_TwistedFlesh);
        }
    }

    /// <summary>
    /// The facility's residents, still sealed in their cryptosleep pods: kept asleep and fed
    /// for centuries. Opened, they're friendly ancients - and far too heavy to walk away.
    /// </summary>
    public class RoomContents_RRFeedeePods : RoomContentsWorker
    {
        static readonly IntRange PodCount = new IntRange(1, 3);
        static readonly FloatRange ResidentKilos = new FloatRange(300f, 1900f);

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            base.FillRoom(map, room, faction, threatPoints);
            ThingDef casket = ThingDefOf.AncientCryptosleepCasket;
            int want = PodCount.RandomInRange;
            int groupID = Find.UniqueIDsManager.GetNextAncientCryptosleepCasketGroupID();
            var cells = room.rects.SelectMany(r => r.ContractedBy(1).Cells).InRandomOrder().ToList();
            int placed = 0;
            foreach (IntVec3 c in cells)
            {
                if (placed >= want)
                    break;
                Rot4 rot = Rand.Bool ? Rot4.North : Rot4.East;
                if (!FacilityGen.Fits(casket, c, rot, map))
                    continue;
                Building_AncientCryptosleepCasket pod = RoomGenUtility.SpawnCryptoCasket(c, map, rot, groupID, PodContentsType.AncientFriendly, ThingSetMakerDefOf.MapGen_AncientPodContents);
                if (pod == null)
                    continue;
                placed++;
                foreach (Pawn resident in pod.GetDirectlyHeldThings().OfType<Pawn>())
                    Fatten(resident);
            }
        }

        static void Fatten(Pawn p)
        {
            if (p?.RaceProps?.Humanlike != true)
                return;
            float kilos = ResidentKilos.RandomInRange;
            Utilities.HediffUtility.SetHediffSeverity(Defs.HediffDefOf.RimRound_Weight, p, Utilities.HediffUtility.KilosToSeverityWithBaseWeight(kilos));
        }
    }

    static class FacilityGen
    {
        /// <summary>Room for a thing here: every cell of it free, standable, and not in a doorway's way.</summary>
        public static bool Fits(ThingDef def, IntVec3 at, Rot4 rot, Map map)
        {
            foreach (IntVec3 c in GenAdj.OccupiedRect(at, rot, def.size).ExpandedBy(1))
            {
                if (!c.InBounds(map) || c.GetDoor(map) != null)
                    return false;
            }
            foreach (IntVec3 c in GenAdj.OccupiedRect(at, rot, def.size))
            {
                if (!c.Standable(map) || c.GetEdifice(map) != null || c.GetFirstPawn(map) != null)
                    return false;
            }
            return true;
        }
    }

    /// <summary>A hunter trap that releases a meld hunter drone instead of a hunter drone.</summary>
    public class Building_RRTrapReleaseMeldHunter : Building_TrapReleaseEntity
    {
        static PawnKindDef drone;

        protected override int CountToSpawn => 1;

        protected override PawnKindDef PawnToSpawn => drone ??= DefDatabase<PawnKindDef>.GetNamed("RR_Drone_MeldHunter");
    }
}
