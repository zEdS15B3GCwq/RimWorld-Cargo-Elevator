using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CargoElevator;

/// <summary>
/// Contents tab of the elevator: one list with the buffer. Vanilla has no tab for a building's own
/// container, so this subclass of the shared base shows <see cref="Building_Elevator.Buffer"/>.
/// </summary>
public class ITab_ElevatorContents : ITab_ContentsBase
{
    public override IList<Thing> container => ((Building_Elevator)SelThing).Buffer;

    // Dropping an item is a deliberate click on the drop button; no confirmation, like the vanilla transporter tab.
    public override bool UseDiscardMessage => false;

    // Shown like the old transporter tab outside of loading: only while the buffer holds something.
    public override bool IsVisible
        => SelThing.Faction == Faction.OfPlayer && ((Building_Elevator)SelThing).BufferEntryCount > 0;

    public ITab_ElevatorContents()
    {
        labelKey = "TabTransporterContents";
        containedItemsKey = "ContainedItems";
    }

    protected override void OnDropThing(Thing thing, int count)
    {
        // Vanilla drops "near" the building's own position, which starts at the intake cell,
        // so the item would be absorbed again.
        var elevator = (Building_Elevator)SelThing;
        elevator.DropFromBuffer(thing, count, "contents tab");
    }
}
