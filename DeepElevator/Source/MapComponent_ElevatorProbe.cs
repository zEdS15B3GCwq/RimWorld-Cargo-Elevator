using UnityEngine;
using Verse;

namespace DeepElevator;

/// <summary>
/// Step A probe, two jobs:
/// 1. logs when a map is removed, so testers can see the order of map-removal callbacks;
/// 2. detects frame hitches and logs them together with our own timings (see ElevatorProfiler),
///    to tell whether a freeze (for example on a rejected placement) is caused by our code.
/// Can be dropped once both questions are settled.
/// </summary>
public class MapComponent_ElevatorProbe : MapComponent
{
    /// <summary>A frame longer than this counts as a hitch worth logging.</summary>
    private const float HitchThresholdSeconds = 0.15f;

    // Static, because MapComponentUpdate only runs for the map being shown and the time must carry over map switches.
    private static float previousFrameTime = -1f;

    public MapComponent_ElevatorProbe(Map map) : base(map)
    {
    }

    public override void MapComponentUpdate()
    {
        float currentFrameTime = Time.realtimeSinceStartup;
        float frameSeconds = currentFrameTime - previousFrameTime;
        bool hasPreviousFrame = previousFrameTime >= 0f;
        previousFrameTime = currentFrameTime;

        // Statistics are cleared every frame, so a hitch report only contains the frame that caused it.
        if (!hasPreviousFrame || frameSeconds < HitchThresholdSeconds)
        {
            ElevatorProfiler.Reset();
            return;
        }

        // Long frames with no designator selected and none of our code involved are unrelated to us
        // (alt-tab, saving, loading, ...), so they are not reported.
        Designator selectedDesignator = Find.DesignatorManager?.SelectedDesignator;
        if (selectedDesignator == null && !ElevatorProfiler.HasStatistics)
        {
            return;
        }

        ElevatorUtil.LogWarn($"Hitch: previous frame took {frameSeconds * 1000f:F0}ms on map#{map.uniqueID}; " +
                             $"selected designator={selectedDesignator?.GetType().Name ?? "none"} ({selectedDesignator?.Label ?? "-"}), " +
                             $"mouse cell={UI.MouseCell()}. Our code during that frame:{ElevatorProfiler.DumpAndReset()}");
    }

    public override void MapRemoved()
    {
        // The map is already out of Find.Maps here, so avoid LevelLabels.LevelLabel.
        ElevatorUtil.Log($"MapComponent.MapRemoved: map#{map.uniqueID} tile={map.Tile}");
    }
}
