using Verse;

namespace RimRound.Comps
{
    public class CompProperties_DestroyDrop : CompProperties
    {
        public string dropDefName;
        public int dropCount = 1;

        public CompProperties_DestroyDrop()
        {
            compClass = typeof(CompDestroyDrop);
        }
    }

    public class CompDestroyDrop : ThingComp
    {
        private bool hasDropped = false;

        public CompProperties_DestroyDrop Props => (CompProperties_DestroyDrop)props;

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (hasDropped) return;
            hasDropped = true;

            if (previousMap == null) return;

            ThingDef dropDef = ThingDef.Named(Props.dropDefName);
            if (dropDef == null) return;

            int count = Props.dropCount;
            if (count <= 0) return;

            Thing thing = ThingMaker.MakeThing(dropDef);
            thing.stackCount = count;
            GenPlace.TryPlaceThing(thing, parent.Position, previousMap, ThingPlaceMode.Near);
        }
    }
}
