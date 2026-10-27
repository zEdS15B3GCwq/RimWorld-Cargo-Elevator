using Verse;

namespace DeepElevator;

/// <summary>The vanilla rename window, applied to an elevator.</summary>
public class Dialog_RenameElevator : Dialog_Rename<Building_Elevator>
{
    public Dialog_RenameElevator(Building_Elevator elevator) : base(elevator)
    {
    }

    protected override void OnRenamed(string name)
    {
        ElevatorUtil.Log($"Renamed elevator {renaming.ThingID} to \"{name}\"");
    }
}
