using System.Linq;
using Verse;

namespace DeepElevator;

/// <summary>
/// Step A probe: logs once at startup everything about the elevator def that could make the game
/// reject a placement for reasons other than our own PlaceWorker (extra place workers added by other
/// mods, interaction cells, passability).
/// </summary>
[StaticConstructorOnStartup]
public static class ElevatorDiagnostics
{
    static ElevatorDiagnostics()
    {
        ThingDef elevatorDef = DeepElevatorDefOf.DeepElevator_Elevator;
        if (elevatorDef == null)
        {
            ElevatorUtil.LogWarn("Diagnostics: elevator def not found.");
            return;
        }

        string placeWorkerNames = elevatorDef.PlaceWorkers == null
            ? "none"
            : string.Join(", ", elevatorDef.PlaceWorkers.Select(placeWorker => placeWorker.GetType().FullName));

        ElevatorUtil.Log($"Diagnostics: placeWorkers=[{placeWorkerNames}] hasInteractionCell={elevatorDef.hasInteractionCell} " +
                         $"passability={elevatorDef.passability} thingClass={elevatorDef.thingClass.FullName} tickerType={elevatorDef.tickerType}");
    }
}
