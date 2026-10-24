using Verse;

namespace DeepElevator;

/// <summary>
/// Step A probe: logs when a map is removed, so testers can see the order of map-removal callbacks.
/// Can be dropped once Building_Elevator.Notify_MyMapRemoved has proven sufficient.
/// </summary>
public class MapComponent_ElevatorProbe : MapComponent
{
    public MapComponent_ElevatorProbe(Map map) : base(map)
    {
    }

    public override void MapRemoved()
    {
        // The map is already out of Find.Maps here, so avoid ElevatorUtil.LayerLabel.
        ElevatorUtil.Log($"MapComponent.MapRemoved: map#{map.uniqueID} tile={map.Tile}");
    }
}
