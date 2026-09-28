using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimRound.FeedingTube;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimRound.Things
{
    /// <summary>
    /// Constrictor vat: a home for a bound gorge constrictor. It stores feed -
    /// hauled food, nutrient paste from the paste pipes, or liquid food from the
    /// feed lines - and, while powered, feeds it to the constrictor so it reswells
    /// after emptying itself into someone. It can also grow mutations on it: pick
    /// one, haulers bring the ingredients, the vat spends feed and grows it for a
    /// few days.
    ///
    /// The constrictor and everything hauled in live in innerContainer; food there
    /// is absorbed into storedFeed on the next check, except what a pending
    /// mutation still needs as an ingredient.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Building_RRConstrictorVat : Building, IThingHolder, IStoreSettingsParent
    {
        public const float FeedCapacity = 150f;
        const int CheckInterval = 250;
        const float FeedToConstrictorPerDay = 40f;
        const float PasteNutrition = 0.9f;
        const float PipeDrawPerCheck = 2f;       // nutrition

        public ThingOwner<Thing> innerContainer;
        float storedFeed;
        StorageSettings allowedFeedSettings;

        /// <summary>A bound constrictor item waiting to be hauled in.</summary>
        public Thing selectedConstrictor;

        RRConstrictorMutationDef pendingMutation;
        bool mutationPaid;
        int mutationProgressTicks;

        [Unsaved] CompPowerTrader powerComp;
        [Unsaved] PipeSystem.CompResource pasteComp;
        [Unsaved] Graphic topGraphic;

        public Building_RRConstrictorVat()
        {
            innerContainer = new ThingOwner<Thing>(this);
        }

        public bool PowerOn => powerComp == null || powerComp.PowerOn;

        public Thing Constrictor => innerContainer.FirstOrDefault(t => t.def.defName == "RR_BoundConstrictor");

        BoundConstrictorData Data => Constrictor?.TryGetComp<CompRRBoundConstrictor>()?.data;

        public float StoredFeed => storedFeed;

        /// <summary>Feed still wanted, counting food already hauled in but not yet absorbed.</summary>
        public float FeedNeeded
        {
            get
            {
                float waiting = 0f;
                foreach (Thing t in innerContainer)
                {
                    if (IsFood(t) && !IsIngredient(t))
                        waiting += t.stackCount * t.GetStatValue(StatDefOf.Nutrition);
                }
                return Mathf.Max(0f, FeedCapacity - storedFeed - waiting);
            }
        }

        public RRConstrictorMutationDef PendingMutation => pendingMutation;

        // ------------------------------------------------------------ lifecycle

        public override void PostMake()
        {
            base.PostMake();
            allowedFeedSettings = new StorageSettings(this);
            if (def.building.defaultStorageSettings != null)
                allowedFeedSettings.CopyFrom(def.building.defaultStorageSettings);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            pasteComp = GetComp<PipeSystem.CompResource>();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (mode != DestroyMode.WillReplace && Spawned)
                innerContainer.TryDropAll(InteractionCell, Map, ThingPlaceMode.Near);
            base.DeSpawn(mode);
        }

        public ThingOwner GetDirectlyHeldThings() => innerContainer;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        // ------------------------------------------------------------ storage settings (which foods it accepts)

        public bool StorageTabVisible => true;
        public StorageSettings GetStoreSettings() => allowedFeedSettings;
        public StorageSettings GetParentStoreSettings() => def.building.fixedStorageSettings;
        public void Notify_SettingsChanged() { }

        public bool AcceptsAsFeed(Thing t) => IsFood(t) && allowedFeedSettings.AllowedToAccept(t);

        static bool IsFood(Thing t) => t.def.IsNutritionGivingIngestible && t.def.defName != "RR_BoundConstrictor";

        bool IsIngredient(Thing t) => pendingMutation != null && !mutationPaid && pendingMutation.costList.Any(c => c.thingDef == t.def);

        /// <summary>How many of this ingredient the pending mutation still needs hauled in.</summary>
        public int IngredientMissing(ThingDef ingredient)
        {
            if (pendingMutation == null || mutationPaid)
                return 0;
            ThingDefCountClass cost = pendingMutation.costList.FirstOrDefault(c => c.thingDef == ingredient);
            if (cost == null)
                return 0;
            return Mathf.Max(0, cost.count - innerContainer.TotalStackCountOfDef(ingredient));
        }

        public IEnumerable<ThingDefCountClass> MissingIngredients()
        {
            if (pendingMutation == null || mutationPaid)
                yield break;
            foreach (ThingDefCountClass c in pendingMutation.costList)
            {
                int missing = IngredientMissing(c.thingDef);
                if (missing > 0)
                    yield return new ThingDefCountClass(c.thingDef, missing);
            }
        }

        // ------------------------------------------------------------ ticking

        protected override void Tick()
        {
            base.Tick();
            if (!this.IsHashIntervalTick(CheckInterval))
                return;

            DropExtraConstrictors();
            ClearStaleSelection();
            DropStrays();
            AbsorbHauledFood();
            if (!PowerOn)
                return;
            DrawFromPipes();
            FeedConstrictor();
            GrowMutation();
        }

        void DropExtraConstrictors()
        {
            Thing keep = Constrictor;
            foreach (Thing t in innerContainer.Where(t => t.def.defName == "RR_BoundConstrictor" && t != keep).ToList())
                innerContainer.TryDrop(t, InteractionCell, Map, ThingPlaceMode.Near, out _);
            if (keep != null && selectedConstrictor == keep)
                selectedConstrictor = null;
        }

        /// <summary>The chosen constrictor was used, destroyed, or went into another vat.</summary>
        void ClearStaleSelection()
        {
            if (selectedConstrictor == null)
                return;
            if (selectedConstrictor.Destroyed || (selectedConstrictor.ParentHolder is Building_RRConstrictorVat other && other != this))
                selectedConstrictor = null;
        }

        /// <summary>
        /// Drops anything that is neither the constrictor, food, nor an ingredient the
        /// pending mutation still needs: ingredients that arrive after a mutation is
        /// cancelled, or more of one than it asked for. Without this they would sit
        /// in the vat unseen.
        /// </summary>
        void DropStrays()
        {
            Thing keep = Constrictor;
            foreach (Thing t in innerContainer.ToList())
            {
                // food always stays: extra of an edible ingredient becomes feed once the mutation is paid for
                if (t == keep || IsFood(t))
                    continue;
                int wanted = 0;
                if (IsIngredient(t))
                {
                    int cost = pendingMutation.costList.First(c => c.thingDef == t.def).count;
                    int before = innerContainer.Where(x => x.def == t.def && x != t).Sum(x => x.stackCount);
                    wanted = Mathf.Clamp(cost - before, 0, t.stackCount);
                }
                if (wanted >= t.stackCount)
                    continue;
                innerContainer.TryDrop(t, InteractionCell, Map, ThingPlaceMode.Near, t.stackCount - wanted, out _);
            }
        }

        void AbsorbHauledFood()
        {
            foreach (Thing t in innerContainer.ToList())
            {
                if (!IsFood(t) || IsIngredient(t))
                    continue;
                float per = t.GetStatValue(StatDefOf.Nutrition);
                if (per <= 0f)
                    continue;
                int fits = Mathf.Min(t.stackCount, Mathf.FloorToInt((FeedCapacity - storedFeed) / per));
                if (fits <= 0)
                {
                    // no room: spit out what can't be stored
                    innerContainer.TryDrop(t, InteractionCell, Map, ThingPlaceMode.Near, out _);
                    continue;
                }
                storedFeed += fits * per;
                t.SplitOff(fits).Destroy();
            }
        }

        void DrawFromPipes()
        {
            float room = FeedCapacity - storedFeed;
            if (room < 0.5f)
                return;

            PipeSystem.PipeNet pasteNet = pasteComp?.PipeNet;
            if (pasteNet != null && pasteNet.Stored > 0f)
            {
                float meals = Mathf.Min(PipeDrawPerCheck / PasteNutrition, room / PasteNutrition, pasteNet.Stored);
                pasteNet.DrawAmongStorage(meals, out float drawn, null, drawFromOverflow: true);
                storedFeed += drawn * PasteNutrition;
                room = FeedCapacity - storedFeed;
            }
            if (room >= 0.5f)
                storedFeed += FoodNetworkAccess.Current.DrawNutrition(this, Mathf.Min(PipeDrawPerCheck, room));
        }

        void FeedConstrictor()
        {
            BoundConstrictorData data = Data;
            if (data == null || storedFeed <= 0f)
                return;
            float want = data.Capacity - data.feed;
            if (want <= 0f)
                return;
            float give = Mathf.Min(want, storedFeed, FeedToConstrictorPerDay * CheckInterval / GenDate.TicksPerDay);
            data.feed += give;
            storedFeed -= give;
        }

        void GrowMutation()
        {
            BoundConstrictorData data = Data;
            if (pendingMutation == null || data == null)
                return;

            if (!mutationPaid)
            {
                if (MissingIngredients().Any() || storedFeed < pendingMutation.nutritionCost)
                    return;
                // it was queued for a constrictor that has since been swapped out
                string reason = CannotGrowReason(pendingMutation, data);
                if (reason != null)
                {
                    Messages.Message($"The constrictor vat can't grow {pendingMutation.label} on the constrictor it now holds ({reason.UncapitalizeFirst()}). The mutation is cancelled.", this, MessageTypeDefOf.RejectInput);
                    CancelMutation();
                    return;
                }
                foreach (ThingDefCountClass c in pendingMutation.costList)
                {
                    int left = c.count;
                    foreach (Thing t in innerContainer.Where(t => t.def == c.thingDef).ToList())
                    {
                        int take = Mathf.Min(left, t.stackCount);
                        t.SplitOff(take).Destroy();
                        left -= take;
                        if (left <= 0)
                            break;
                    }
                }
                storedFeed -= pendingMutation.nutritionCost;
                mutationPaid = true;
                mutationProgressTicks = 0;
                Messages.Message($"The constrictor vat has started growing {pendingMutation.label} on its bound constrictor.", this, MessageTypeDefOf.NeutralEvent, historical: false);
                return;
            }

            mutationProgressTicks += CheckInterval;
            if (mutationProgressTicks < pendingMutation.growDays * GenDate.TicksPerDay)
                return;

            data.mutations.Add(pendingMutation);
            data.feed = Mathf.Min(data.feed, data.Capacity);
            Messages.Message($"The bound constrictor has grown {pendingMutation.label}.", this, MessageTypeDefOf.PositiveEvent);
            SoundDef.Named("RR_StomachGurgles_Heavy").PlayOneShot(new TargetInfo(Position, Map));
            pendingMutation = null;
            mutationPaid = false;
            mutationProgressTicks = 0;
        }

        // ------------------------------------------------------------ the constrictor going in and out

        /// <summary>Lifts the constrictor out into the pawn's hands (JobDriver_RRApplyBoundConstrictor).</summary>
        public bool TryTakeConstrictor(Pawn pawn)
        {
            Thing c = Constrictor;
            if (c == null)
                return false;
            InterruptGrowth();
            return innerContainer.TryTransferToContainer(c, pawn.carryTracker.innerContainer, 1) > 0;
        }

        public void EjectConstrictor()
        {
            Thing c = Constrictor;
            if (c == null)
                return;
            InterruptGrowth();
            innerContainer.TryDrop(c, InteractionCell, Map, ThingPlaceMode.Near, out _);
        }

        /// <summary>
        /// The constrictor leaves mid-growth: the half-grown mutation dies with the
        /// fluid it was growing in. A mutation still waiting for ingredients stays
        /// queued for whichever constrictor is in the vat next.
        /// </summary>
        void InterruptGrowth()
        {
            if (pendingMutation == null || !mutationPaid)
                return;
            Messages.Message($"The bound constrictor left the vat before {pendingMutation.label} finished growing. The growth is lost.", this, MessageTypeDefOf.NegativeEvent);
            pendingMutation = null;
            mutationPaid = false;
            mutationProgressTicks = 0;
        }

        /// <summary>Warning for anything that would take the constrictor out mid-growth.</summary>
        public string GrowthLossWarning => pendingMutation != null && mutationPaid
            ? $" This ends the growth of {pendingMutation.label}; what was spent on it is lost."
            : "";

        void SetMutation(RRConstrictorMutationDef m)
        {
            pendingMutation = m;
            mutationPaid = false;
            mutationProgressTicks = 0;
        }

        void CancelMutation()
        {
            foreach (Thing t in innerContainer.Where(IsIngredient).ToList())
                innerContainer.TryDrop(t, InteractionCell, Map, ThingPlaceMode.Near, out _);
            pendingMutation = null;
            mutationPaid = false;
            mutationProgressTicks = 0;
        }

        /// <summary>Why this mutation can't be grown on the vat's constrictor right now, or null.</summary>
        string CannotGrowReason(RRConstrictorMutationDef m, BoundConstrictorData data)
        {
            if (!Utilities.GameComponent_RRStudyUnlocks.Has(m.requiredStudy))
                return "Needs more study of captive gorge constrictors";
            if (data.Has(m))
                return "Already grown";
            if (data.mutations.Count >= BoundConstrictorData.MaxMutations)
                return $"It can only carry {BoundConstrictorData.MaxMutations} mutations";
            return null;
        }

        // ------------------------------------------------------------ gizmos and menus

        static readonly CachedTexture InsertIcon = new CachedTexture("UI/Gizmos/InsertPawn");
        static readonly CachedTexture EjectIcon = new CachedTexture("UI/Commands/PodEject");
        static readonly CachedTexture MutateIcon = new CachedTexture("UI/Icons/Genes/Gene_Tough");
        static readonly CachedTexture CancelIcon = new CachedTexture("UI/Designators/Cancel");

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            foreach (Gizmo g in StorageSettingsClipboard.CopyPasteGizmosFor(allowedFeedSettings))
                yield return g;

            BoundConstrictorData data = Data;
            if (data == null)
            {
                if (selectedConstrictor != null)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "Cancel insertion",
                        defaultDesc = "Stop haulers from bringing the bound constrictor to this vat.",
                        icon = CancelIcon.Texture,
                        action = () => selectedConstrictor = null,
                    };
                }
                else
                {
                    var insert = new Command_Action
                    {
                        defaultLabel = "Insert bound constrictor...",
                        defaultDesc = "Choose a bound gorge constrictor for haulers to bring here. The vat feeds it so it reswells after use, and can grow mutations on it.",
                        icon = InsertIcon.Texture,
                        action = delegate
                        {
                            var opts = Map.listerThings.ThingsOfDef(ThingDef.Named("RR_BoundConstrictor"))
                                .Where(t => !t.IsForbidden(Faction.OfPlayer))
                                .Select(t => new FloatMenuOption(t.LabelCap, () => selectedConstrictor = t, t, Color.white))
                                .ToList();
                            Find.WindowStack.Add(new FloatMenu(opts));
                        },
                    };
                    if (!Map.listerThings.ThingsOfDef(ThingDef.Named("RR_BoundConstrictor")).Any(t => !t.IsForbidden(Faction.OfPlayer)))
                        insert.Disable("There is no bound gorge constrictor lying around on this map.");
                    yield return insert;
                }
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "Take out constrictor",
                    defaultDesc = "Drop the bound constrictor next to the vat. Outside a vat it slowly shrivels." + GrowthLossWarning,
                    icon = EjectIcon.Texture,
                    action = EjectConstrictor,
                };

                if (pendingMutation == null)
                {
                    var grow = new Command_Action
                    {
                        defaultLabel = "Grow mutation...",
                        defaultDesc = $"Shape the bound constrictor: grow a mutation on it. Each costs feed from the vat, a few ingredients for haulers to bring, and a few days of growing while powered. It can carry up to {BoundConstrictorData.MaxMutations}.",
                        icon = MutateIcon.Texture,
                        action = delegate
                        {
                            var opts = new List<FloatMenuOption>();
                            foreach (RRConstrictorMutationDef m in DefDatabase<RRConstrictorMutationDef>.AllDefs.OrderBy(m => m.displayOrder))
                            {
                                string reason = CannotGrowReason(m, data);
                                string label = $"{m.LabelCap} ({m.CostLabel}, {m.growDays:0.#} days)";
                                var opt = reason == null
                                    ? new FloatMenuOption(label, () => SetMutation(m))
                                    : new FloatMenuOption(label + ": " + reason, null);
                                opt.tooltip = m.description;
                                opts.Add(opt);
                            }
                            Find.WindowStack.Add(new FloatMenu(opts));
                        },
                    };
                    if (data.mutations.Count >= BoundConstrictorData.MaxMutations)
                        grow.Disable($"It already carries {BoundConstrictorData.MaxMutations} mutations.");
                    yield return grow;
                }
                else
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "Cancel mutation",
                        defaultDesc = mutationPaid
                            ? "Stop growing this mutation. What was spent on it is lost."
                            : "Stop this mutation. Ingredients already hauled in are dropped next to the vat.",
                        icon = CancelIcon.Texture,
                        action = CancelMutation,
                    };
                }
            }

            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action { defaultLabel = "DEV: Fill feed", action = () => storedFeed = FeedCapacity };
                if (data != null)
                    yield return new Command_Action { defaultLabel = "DEV: Reswell constrictor", action = () => data.feed = data.Capacity };
                if (pendingMutation != null)
                    yield return new Command_Action
                    {
                        defaultLabel = "DEV: Finish mutation",
                        action = delegate
                        {
                            mutationPaid = true;
                            mutationProgressTicks = Mathf.CeilToInt(pendingMutation.growDays * GenDate.TicksPerDay);
                        },
                    };
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption o in base.GetFloatMenuOptions(selPawn))
                yield return o;

            BoundConstrictorData data = Data;
            if (data == null)
                yield break;
            if (data.HungryForUse)
            {
                yield return new FloatMenuOption("Set the bound constrictor on someone: it is still shrivelled", null);
                yield break;
            }
            if (!selPawn.CanReach(this, PathEndMode.InteractionCell, Danger.Deadly))
                yield break;

            string label = "Set the bound constrictor on someone..." + (GrowthLossWarning.Length > 0 ? $" (ends the growth of {pendingMutation.label})" : "");
            yield return new FloatMenuOption(label, delegate
            {
                var parms = new TargetingParameters
                {
                    canTargetPawns = true,
                    canTargetBuildings = false,
                    canTargetAnimals = false,
                    canTargetMechs = false,
                    canTargetSelf = true,
                    validator = t => t.Thing is Pawn p && Hediffs.Hediff_RRConstricted.CannotLatchLeashedReason(p, data) == null,
                };
                Find.Targeter.BeginTargeting(parms, target =>
                {
                    Pawn victim = (Pawn)target.Thing;
                    Job job = victim == selPawn
                        ? JobMaker.MakeJob(Defs.JobDefOf.RR_ApplyBoundConstrictor, this)
                        : JobMaker.MakeJob(Defs.JobDefOf.RR_ApplyBoundConstrictor, this, victim);
                    job.count = 1;
                    selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }, selPawn);
            });
        }

        // ------------------------------------------------------------ drawing

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            Thing c = Constrictor;
            if (c != null)
            {
                // it floats in the fluid, bigger the more it has swollen
                BoundConstrictorData data = Data;
                float bob = Mathf.Sin(Find.TickManager.TicksGame / 90f) * 0.04f;
                float size = Mathf.Lerp(0.8f, 1.35f, data?.FeedFraction ?? 0.5f);
                Vector3 pos = DrawPos + new Vector3(0f, 0f, 0.1f + bob) + Altitudes.AltIncVect * 0.25f;
                Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size, 1f, size));
                Graphics.DrawMesh(MeshPool.plane10, m, c.Graphic.MatSingle, 0);
            }
            if (topGraphic == null)
                topGraphic = GraphicDatabase.Get<Graphic_Single>("Things/Building/RR_ConstrictorVat/RR_ConstrictorVatTop", ShaderDatabase.Transparent, def.graphicData.drawSize, Color.white);
            topGraphic.Draw(DrawPos + Altitudes.AltIncVect * 2f, Rotation, this);
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            sb.AppendLineIfNotEmpty().Append($"Stored feed: {storedFeed:0.#} / {FeedCapacity:0}");
            BoundConstrictorData data = Data;
            if (data != null)
            {
                sb.AppendLine().Append("Holding a bound gorge constrictor.");
                data.Describe(sb);
            }
            else if (selectedConstrictor != null)
            {
                sb.AppendLine().Append("Waiting for the bound constrictor to be brought in.");
            }
            if (pendingMutation != null)
            {
                sb.AppendLine().Append($"Mutation: {pendingMutation.label}");
                if (mutationPaid)
                    sb.Append($" - growing, {((float)mutationProgressTicks / (pendingMutation.growDays * GenDate.TicksPerDay)).ToStringPercent()}");
                else
                {
                    var missing = MissingIngredients().ToList();
                    sb.Append(missing.Count > 0
                        ? " - waiting for " + string.Join(", ", missing.Select(c => c.Label))
                        : $" - waiting for {pendingMutation.nutritionCost:0} stored feed");
                }
            }
            return sb.ToString();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref storedFeed, "storedFeed");
            Scribe_Deep.Look(ref allowedFeedSettings, "allowedFeedSettings", this);
            Scribe_References.Look(ref selectedConstrictor, "selectedConstrictor");
            Scribe_Defs.Look(ref pendingMutation, "pendingMutation");
            Scribe_Values.Look(ref mutationPaid, "mutationPaid");
            Scribe_Values.Look(ref mutationProgressTicks, "mutationProgressTicks");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (innerContainer == null)
                    innerContainer = new ThingOwner<Thing>(this);
                if (allowedFeedSettings == null)
                {
                    allowedFeedSettings = new StorageSettings(this);
                    if (def.building.defaultStorageSettings != null)
                        allowedFeedSettings.CopyFrom(def.building.defaultStorageSettings);
                }
            }
        }
    }
}
