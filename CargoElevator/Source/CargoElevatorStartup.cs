using Verse;

namespace CargoElevator;

/// <summary>
/// Runs once after all defs and patches have loaded: writes the mod settings into the defs, then logs the
/// resulting def facts (verbose logging only).
/// </summary>
[StaticConstructorOnStartup]
public static class CargoElevatorStartup
{
    static CargoElevatorStartup()
    {
        CargoElevatorMod.Settings.ApplyToDefs();
        ElevatorDiagnostics.LogDefs();
    }
}
