using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CargoElevator;

/// <summary>
/// Contents tab of the transport loader: the items waiting in its container for the elevator. The portal's
/// own "Travel" tab lists the order (what is still to be loaded); this one lists what has arrived.
/// </summary>
public class ITab_LoaderContents : ITab_ContentsBase
{
    public override IList<Thing> container => ((Building_TransportLoader)SelThing).Container;

    // Dropping an item is a deliberate click on the drop button; no confirmation, like the elevator's tab.
    public override bool UseDiscardMessage => false;

    // Shown only while something waits in the container.
    public override bool IsVisible
        => SelThing.Faction == Faction.OfPlayer && ((Building_TransportLoader)SelThing).Container.Count > 0;

    public ITab_LoaderContents()
    {
        labelKey = "TabTransporterContents";
        containedItemsKey = "ContainedItems";
    }

    protected override void OnDropThing(Thing thing, int count)
    {
        // Vanilla drops "near" the building's own position, which could land on the elevator's intake cell,
        // so the item would be absorbed again; the loader's drop skips elevator cells.
        var loader = (Building_TransportLoader)SelThing;
        loader.DropFromContainer(thing, count, "contents tab");
    }
}
