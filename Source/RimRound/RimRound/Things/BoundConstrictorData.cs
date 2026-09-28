using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Things
{
    /// <summary>
    /// One mutation a bound gorge constrictor can grow in a constrictor vat. The
    /// effects are plain multipliers and flags read by Hediff_RRConstricted; the
    /// cost is vat feed, some hauled ingredients and a few days of growing.
    /// </summary>
    public class RRConstrictorMutationDef : Def
    {
        public float nutritionCost = 10f;
        public float growDays = 1f;
        public List<ThingDefCountClass> costList = new List<ThingDefCountClass>();
        public int displayOrder;
        /// <summary>Study unlock key needed before the vat can grow it (see GameComponent_RRStudyUnlocks), or none.</summary>
        public string requiredStudy;

        // effects
        public float pumpIntervalFactor = 1f;
        public float capacityFactor = 1f;
        public float feedPerKiloFactor = 1f;
        public int extraBurstTiers;
        public bool euphoric;
        public bool persuasive;
        public HediffDef afterHediff;

        public string CostLabel => nutritionCost.ToString("0") + " feed" +
            (costList.NullOrEmpty() ? "" : ", " + string.Join(", ", costList.Select(c => c.Label)));
    }

    /// <summary>
    /// Everything a bound gorge constrictor carries between uses: how much feed it
    /// has swollen with (its load - what it can pump into someone) and which
    /// mutations it has grown. It moves with the constrictor -
    /// from the item (CompRRBoundConstrictor) into the vat, onto a victim (the
    /// leashed Hediff_RRConstricted) and back out as the item again.
    /// </summary>
    public class BoundConstrictorData : IExposable
    {
        public const int MaxMutations = 3;
        const float BaseCapacity = 80f;      // nutrition: a full load is about 800 kg
        const float BaseFeedPerKilo = 0.1f;   // ten times better than eating: it's a void thing
        public const float MinFeedToUse = 4f;

        public float feed = 40f;
        public List<RRConstrictorMutationDef> mutations = new List<RRConstrictorMutationDef>();

        public float Capacity => BaseCapacity * Product(m => m.capacityFactor);
        public float FeedPerKilo => BaseFeedPerKilo * Product(m => m.feedPerKiloFactor);
        public float PumpIntervalFactor => Product(m => m.pumpIntervalFactor);
        public int ExtraBurstTiers => mutations.Sum(m => m.extraBurstTiers);
        public bool Euphoric => mutations.Any(m => m.euphoric);
        public bool Persuasive => mutations.Any(m => m.persuasive);
        public float FeedFraction => Capacity <= 0f ? 0f : Mathf.Clamp01(feed / Capacity);
        public bool HungryForUse => feed < MinFeedToUse;

        /// <summary>The load it can pump into someone, in kilos.</summary>
        public float LoadKilos
        {
            get => feed / FeedPerKilo;
            set => feed = Mathf.Clamp(value * FeedPerKilo, 0f, Capacity);
        }

        float Product(System.Func<RRConstrictorMutationDef, float> f)
        {
            float v = 1f;
            foreach (RRConstrictorMutationDef m in mutations)
                v *= f(m);
            return v;
        }

        public bool Has(RRConstrictorMutationDef m) => mutations.Contains(m);

        public void Describe(StringBuilder sb)
        {
            sb.AppendLineIfNotEmpty().Append($"Swollen: {FeedFraction.ToStringPercent()} ({LoadKilos:0} kg load, {feed:0.#} / {Capacity:0} feed)");
            if (HungryForUse)
                sb.AppendLine().Append("Shrivelled and empty. Let it reswell in a constrictor vat.");
            if (mutations.Count > 0)
                sb.AppendLine().Append("Mutations: " + string.Join(", ", mutations.Select(m => m.label)));
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref feed, "feed", 40f);
            Scribe_Collections.Look(ref mutations, "mutations", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (mutations == null)
                    mutations = new List<RRConstrictorMutationDef>();
                mutations.RemoveAll(m => m == null);
            }
        }
    }

    /// <summary>The bound constrictor item's state. See BoundConstrictorData.</summary>
    public class CompRRBoundConstrictor : ThingComp
    {
        public BoundConstrictorData data = new BoundConstrictorData();

        // it gets hungry outside a vat, slowly
        const float FeedLossPerDay = 1f;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (parent.ParentHolder is Building_RRConstrictorVat)
                return;
            data.feed = Mathf.Max(0f, data.feed - FeedLossPerDay * 250f / GenDate.TicksPerDay);
        }

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder();
            data.Describe(sb);
            return sb.ToString();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref data, "boundConstrictor");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && data == null)
                data = new BoundConstrictorData();
        }

        /// <summary>Makes a bound constrictor item carrying this state.</summary>
        public static Thing MakeItem(BoundConstrictorData data)
        {
            Thing item = ThingMaker.MakeThing(ThingDef.Named("RR_BoundConstrictor"));
            if (data != null && item.TryGetComp<CompRRBoundConstrictor>() is CompRRBoundConstrictor comp)
                comp.data = data;
            return item;
        }
    }

    public class CompProperties_RRBoundConstrictor : CompProperties
    {
        public CompProperties_RRBoundConstrictor()
        {
            compClass = typeof(CompRRBoundConstrictor);
        }
    }
}
