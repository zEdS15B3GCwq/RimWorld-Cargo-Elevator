using System.Collections.Generic;
using Verse;

namespace CargoElevator;

/// <summary>
/// Owns the loader's container and presents it to the game as a child holder of the loader. The loader itself
/// cannot expose the container through <c>GetDirectlyHeldThings</c> (that must stay the portal proxy, or hauled
/// items would never reach the order), so this small holder is registered through <c>GetChildHolders</c> instead.
/// That is enough for the game to tick the held items (rot, deterioration), count their wealth and find them in searches.
/// </summary>
public class LoaderContainerHolder : IThingHolder
{
    private readonly Building_TransportLoader loader;
    private ThingOwner<Thing> container;

    public LoaderContainerHolder(Building_TransportLoader loader)
    {
        this.loader = loader;
        container = new ThingOwner<Thing>(this);
    }

    /// <summary>The items waiting to be pulled by the elevator. Never null.</summary>
    public ThingOwner<Thing> Container => container;

    public IThingHolder ParentHolder => loader;

    public ThingOwner GetDirectlyHeldThings() => container;

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, container);
    }

    /// <summary>Saves the container under the name the E1 transporter comp used, so the saved node keeps its name.</summary>
    public void ExposeData()
    {
        Scribe_Deep.Look(ref container, "innerContainer", this);

        // A save without that node leaves the field null.
        if (Scribe.mode == LoadSaveMode.PostLoadInit && container == null)
        {
            container = new ThingOwner<Thing>(this);
        }
    }
}
