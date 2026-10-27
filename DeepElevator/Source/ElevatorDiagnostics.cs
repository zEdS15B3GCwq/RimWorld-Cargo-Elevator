using System.Linq;
using RimWorld;
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

        // Step C: the def facts the storage behaviour depends on.
        var settingsExtension = elevatorDef.GetModExtension<ElevatorSettingsExtension>();
        string settingsText = settingsExtension == null
            ? "missing"
            : $"slots {settingsExtension.defaultSlots} ({settingsExtension.minSlots}-{settingsExtension.maxSlots}), ticksPerStack={settingsExtension.ticksPerStack}, " +
              $"heldRetryTicks={settingsExtension.HeldRetryTicks}, fifo={settingsExtension.defaultFifo}, merge={settingsExtension.defaultMerge}, drop={settingsExtension.defaultAllowDrop}";
        ElevatorUtil.Log($"Diagnostics: surfaceType={elevatorDef.surfaceType} maxItemsInCell={elevatorDef.building?.maxItemsInCell} " +
                         $"hasTransporterComp={elevatorDef.HasComp(typeof(CompTransporter))} settings=[{settingsText}]");
    }
}
