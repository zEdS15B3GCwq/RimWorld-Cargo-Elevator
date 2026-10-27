using RimWorld;
using Verse;

namespace DeepElevator;

/// <summary>The vanilla transporter contents tab, with item drops redirected away from the intake cell.</summary>
public class ITab_ElevatorContents : ITab_ContentsTransporter
{
    protected override void OnDropThing(Thing thing, int count)
    {
        // Vanilla drops "near" the building's own position, which starts at the intake cell,
        // so the item would be absorbed again.
        var elevator = (Building_Elevator)SelThing;
        elevator.DropFromBuffer(thing, count, "contents tab");
    }
}
