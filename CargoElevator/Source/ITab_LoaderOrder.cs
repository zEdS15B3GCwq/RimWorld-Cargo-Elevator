using RimWorld;
using UnityEngine;

namespace CargoElevator;

/// <summary>
/// The loader's order tab: the vanilla portal tab (what is still to be sent) with our own title, and a fix for its
/// remove button. Vanilla only sets an entry's count to zero, which leaves an empty order "in progress" (the tab and
/// the cancel button stay), so the loader is asked to clean up right after the list is drawn.
/// </summary>
public class ITab_LoaderOrder : ITab_ContentsMapPortal
{
    public ITab_LoaderOrder()
    {
        labelKey = "CargoElevator_TabLoaderOrder"; // replaces the vanilla "Travel"
    }

    protected override void DoItemsLists(Rect inRect, ref float curY)
    {
        base.DoItemsLists(inRect, ref curY);
        (Portal as Building_TransportLoader)?.PruneFinishedOrder();
    }
}
