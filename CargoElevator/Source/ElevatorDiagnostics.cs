using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace CargoElevator;

/// <summary>
/// Logs once at startup everything about each elevator def that could make the game reject a placement
/// for reasons other than our own PlaceWorker (extra place workers added by other mods, interaction cells,
/// passability), plus the def facts the storage, power and drawing behaviour depends on.
/// </summary>
[StaticConstructorOnStartup]
public static class ElevatorDiagnostics
{
    static ElevatorDiagnostics()
    {
        List<ThingDef> elevatorDefs = ElevatorUtil.ElevatorDefs;
        if (elevatorDefs.Count == 0)
        {
            ElevatorUtil.LogWarn("Diagnostics: no elevator def found.");
            return;
        }

        foreach (ThingDef elevatorDef in elevatorDefs)
        {
            LogDef(elevatorDef);
        }
    }

    private static void LogDef(ThingDef elevatorDef)
    {
        string placeWorkerNames = elevatorDef.PlaceWorkers == null
            ? "none"
            : string.Join(", ", elevatorDef.PlaceWorkers.Select(placeWorker => placeWorker.GetType().FullName));

        ElevatorUtil.Log($"Diagnostics: {elevatorDef.defName} size={elevatorDef.size} drawerType={elevatorDef.drawerType} placeWorkers=[{placeWorkerNames}] " +
                         $"hasInteractionCell={elevatorDef.hasInteractionCell} passability={elevatorDef.passability} " +
                         $"thingClass={elevatorDef.thingClass.FullName} tickerType={elevatorDef.tickerType}");

        var settingsExtension = elevatorDef.GetModExtension<ElevatorSettingsExtension>();
        string settingsText = settingsExtension == null
            ? "missing"
            : $"slots {settingsExtension.defaultSlots} ({settingsExtension.minSlots}-{settingsExtension.maxSlots}), ticksPerStack={settingsExtension.ticksPerStack}, " +
              $"heldRetryTicks={settingsExtension.HeldRetryTicks}, fifo={settingsExtension.defaultFifo}, merge={settingsExtension.defaultMerge}, drop={settingsExtension.defaultAllowDrop}, insert={settingsExtension.defaultAllowInsert}, " +
              $"activeHoldTicks={settingsExtension.ActiveHoldTicks}, blockedAfterSendIntervals={settingsExtension.blockedAfterSendIntervals}, " +
              $"lights=[{settingsExtension.greenLightTexPath}, {settingsExtension.redLightTexPath}]";

        var powerProperties = elevatorDef.GetCompProperties<CompProperties_Power>();
        string powerText = powerProperties == null
            ? "none"
            : $"active={powerProperties.PowerConsumption} idle={powerProperties.idlePowerDraw}";

        ElevatorUtil.Log($"Diagnostics: {elevatorDef.defName} surfaceType={elevatorDef.surfaceType} maxItemsInCell={elevatorDef.building?.maxItemsInCell} " +
                         $"power=[{powerText}] settings=[{settingsText}]");
    }
}
