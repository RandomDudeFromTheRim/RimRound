using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimRound.Buildings
{
    /// <summary>
    /// A flesh trap in the void maze's narrow squeezes. When a humanlike steps on
    /// it, it swallows them whole and swells into a sealed, twitching sack sized
    /// to whoever is inside. It pumps them full, then spits them back out heavier
    /// and dazed. Destroying it frees whoever is inside.
    /// A plain Building rather than a Building_Trap: vanilla traps send a letter
    /// saying the pawn "took damage", which a maw never does.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Building_RRGorgeMaw : Building, IThingHolder
    {
        const int CheckIntervalTicks = 10;
        const int PumpIntervalTicks = 60;
        static readonly IntRange HoldTicksRange = new IntRange(900, 1500);
        static readonly FloatRange KilosRange = new FloatRange(15f, 30f);
        static readonly IntRange RearmTicksRange = new IntRange(2500, 5000);
        const int DazedTicks = 240;

        static readonly Material FullMat = MaterialPool.MatFrom("Things/Building/FleshDimension/RR_GorgeMaw_Full", ShaderDatabase.Cutout);
        static readonly Material BulgeMat = MaterialPool.MatFrom("Things/Building/FleshDimension/RR_GorgeMaw_Bulge", ShaderDatabase.Cutout);

        ThingOwner<Pawn> innerContainer;
        int swallowTick = -1;
        int holdTicks;
        float kilosToPump;
        float heldMeshSize = 1f;
        int rearmTick = -1;

        public Building_RRGorgeMaw()
        {
            innerContainer = new ThingOwner<Pawn>(this, oneStackOnly: true);
        }

        public Pawn HeldPawn => innerContainer.Count > 0 ? innerContainer[0] : null;

        bool Digesting => rearmTick > Find.TickManager.TicksGame;

        float Progress => HeldPawn == null ? 0f : Mathf.Clamp01((float)(Find.TickManager.TicksGame - swallowTick) / holdTicks);

        public ThingOwner GetDirectlyHeldThings() => innerContainer;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref swallowTick, "swallowTick", -1);
            Scribe_Values.Look(ref holdTicks, "holdTicks");
            Scribe_Values.Look(ref kilosToPump, "kilosToPump");
            Scribe_Values.Look(ref heldMeshSize, "heldMeshSize", 1f);
            Scribe_Values.Look(ref rearmTick, "rearmTick", -1);
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned)
                return;

            if (HeldPawn != null)
            {
                if (this.IsHashIntervalTick(PumpIntervalTicks))
                    Utilities.HediffUtility.QueueWeightGain(HeldPawn, kilosToPump * PumpIntervalTicks / holdTicks);
                if (Find.TickManager.TicksGame - swallowTick >= holdTicks)
                    SpitOut();
                return;
            }

            if (Digesting || !this.IsHashIntervalTick(CheckIntervalTicks))
                return;

            foreach (Thing t in Position.GetThingList(Map))
            {
                if (t is Pawn p && CanSwallow(p))
                {
                    Swallow(p);
                    return;
                }
            }
        }

        bool CanSwallow(Pawn p)
        {
            // its own kind (echoes, fleshbeasts) know where the maws are; a downed
            // pawn can't step in, it only lies where it was dropped
            return !p.Dead && !p.Downed && !p.Flying && p.RaceProps.Humanlike && p.Faction != Faction;
        }

        /// <summary>How big the pawn is drawn: RimRound scales the body mesh by body type (1 = vanilla size).</summary>
        static float MeshSizeOf(Pawn p)
        {
            BodyTypeInfo? info = RacialBodyTypeInfoUtility.GetRacialBodyTypeInfo(p);
            return info.HasValue ? Mathf.Max(0.5f, info.Value.meshSize) : 1f;
        }

        void Swallow(Pawn p)
        {
            heldMeshSize = MeshSizeOf(p);
            p.DeSpawnOrDeselect();
            if (!innerContainer.TryAdd(p))
            {
                GenSpawn.Spawn(p, Position, Map);
                return;
            }

            // bigger pawns are more to work down: they take longer, and take more
            swallowTick = Find.TickManager.TicksGame;
            holdTicks = Mathf.RoundToInt(HoldTicksRange.RandomInRange * (0.6f + 0.4f * Mathf.Pow(heldMeshSize, 1.5f)));
            kilosToPump = KilosRange.RandomInRange * (0.7f + 0.3f * heldMeshSize);
            SoundDef.Named("RR_StomachGurgles_Heavy").PlayOneShot(new TargetInfo(Position, Map));

            Messages.Message(
                $"A gorge maw swallows {p.LabelShort} whole! Destroy it to cut {p.ProObj()} free.",
                new LookTargets(this),
                MessageTypeDefOf.NegativeEvent);
        }

        /// <summary>Spit the pawn back out: heavier, stuffed, dazed and slimed.</summary>
        public void SpitOut()
        {
            Pawn p = HeldPawn;
            if (p == null || !Spawned)
                return;

            // onto a free neighbouring tile, never back onto the maw itself
            IntVec3 dropAt = GenAdj.CellsAdjacent8Way(this)
                .Where(c => c.InBounds(Map) && c.Standable(Map) && c.GetFirstBuilding(Map) == null)
                .RandomElementWithFallback(Position);
            if (!innerContainer.TryDrop(p, dropAt, Map, ThingPlaceMode.Near, out _))
                return;

            // the weight went in pulse by pulse while held, so cutting a pawn free
            // early spares them the rest
            var fnd = p.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fnd != null && !fnd.Disabled)
                fnd.CurrentFullness = Mathf.Max(fnd.CurrentFullness, fnd.SoftLimit * 1.1f);

            p.stances?.stunner?.StunFor(DazedTicks, this, addBattleLog: false, showMote: true);
            p.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDef.Named("RR_WasForceFed"));
            FilthMaker.TryMakeFilth(p.Position, Map, ThingDefOf.Filth_Vomit, 3);
            SoundDef.Named("RR_StomachBurp_Heavy").PlayOneShot(new TargetInfo(Position, Map));

            Messages.Message(
                $"The gorge maw spits {p.LabelShort} back out, bigger than before.",
                new LookTargets(p),
                MessageTypeDefOf.NeutralEvent);

            swallowTick = -1;
            rearmTick = Find.TickManager.TicksGame + RearmTicksRange.RandomInRange;
        }

        /// <summary>Makes every maw on the map spit out what it holds, before the maze closes or times out.</summary>
        public static void SpitOutAll(Map map)
        {
            if (map == null)
                return;
            foreach (Building_RRGorgeMaw maw in map.listerThings.AllThings.OfType<Building_RRGorgeMaw>().ToList())
                maw.SpitOut();
        }

        public static bool AnyHolding(Map map)
        {
            return map != null && map.listerThings.AllThings.OfType<Building_RRGorgeMaw>().Any(m => m.HeldPawn != null);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // cut open: whoever is inside tumbles out
            SpitOut();
            base.DeSpawn(mode);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Pawn p = HeldPawn;
            if (p == null)
            {
                base.DrawAt(drawLoc, flip);
                return;
            }

            // A vanilla-size body is drawn about 1.5 tiles across and RimRound scales
            // that mesh by body type, so the sack starts wrapped snugly around its
            // meal (a little inside its outline) and distends as it pumps them.
            float size = 1.3f * heldMeshSize * (0.85f + 0.35f * Progress);

            // bigger meals struggle harder: the hard twitches come more often
            int t = Find.TickManager.TicksGame + thingIDNumber * 37;
            int twitchPeriod = Mathf.RoundToInt(Mathf.Lerp(110f, 45f, Mathf.InverseLerp(1f, 2.5f, heldMeshSize)));
            int twitchIndex = t / twitchPeriod;
            bool twitching = t % twitchPeriod < 10;
            float kick = twitching ? Rand.RangeSeeded(-1f, 1f, twitchIndex + thingIDNumber) : 0f;
            float heave = 1f + 0.05f * Mathf.Sin(t * 0.07f);
            float sx = heave + 0.1f * Mathf.Abs(kick);
            float sz = 2f - heave - 0.06f * Mathf.Abs(kick);

            // drawn over the floor clutter, not under it
            Vector3 center = drawLoc;
            center.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            center.z += 0.1f * (size - 1f);

            // limbs straining against the sack: lumps that push out past its edge,
            // drifting around it, one jabbing hard with each twitch
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 40f * Mathf.Sin(t * 0.013f + i * 1.7f) + thingIDNumber % 90;
                float push = 0.42f + 0.06f * Mathf.Sin(t * 0.05f + i * 2.3f);
                if (twitching && twitchIndex % 4 == i)
                    push += 0.12f;
                float lump = size * (0.32f + 0.05f * Mathf.Sin(t * 0.09f + i));
                Vector3 off = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * size * sx * push, -0.01f, Mathf.Sin(a * Mathf.Deg2Rad) * size * sz * push);
                Graphics.DrawMesh(MeshPool.plane10,
                    Matrix4x4.TRS(center + off, Quaternion.AngleAxis(-a, Vector3.up), new Vector3(lump, 1f, lump * 0.85f)),
                    BulgeMat, 0);
            }

            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(center, Quaternion.AngleAxis(6f * kick, Vector3.up), new Vector3(size * sx, 1f, size * sz)),
                FullMat, 0);
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            string state;
            if (HeldPawn != null)
                state = $"Holding {HeldPawn.LabelShort}. Spits {HeldPawn.ProObj()} out in {(swallowTick + holdTicks - Find.TickManager.TicksGame).ToStringTicksToPeriod()}.";
            else if (Digesting)
                state = "Closed, digesting. Opens again in " + (rearmTick - Find.TickManager.TicksGame).ToStringTicksToPeriod() + ".";
            else
                state = "Open and waiting.";
            return text.NullOrEmpty() ? state : text + "\n" + state;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;

            if (HeldPawn != null)
            {
                Gizmo select = SelectContainedItemGizmo(this, HeldPawn);
                if (select != null)
                    yield return select;
            }

            if (DebugSettings.ShowDevGizmos && HeldPawn != null)
                yield return new Command_Action { defaultLabel = "DEV: Spit out now", action = SpitOut };
        }
    }
}
