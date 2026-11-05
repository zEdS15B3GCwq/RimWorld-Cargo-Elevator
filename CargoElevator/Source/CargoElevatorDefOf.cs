using RimWorld;
using Verse;

namespace CargoElevator;

/// <summary>Def references for this mod, filled in by the game after defs are loaded.</summary>
[DefOf]
public static class CargoElevatorDefOf
{
    public static ThingDef CargoElevator_Elevator;
    public static ThingDef CargoElevator_ElevatorLarge;
    public static ThingDef CargoElevator_TransportLoader;

    static CargoElevatorDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(CargoElevatorDefOf));
    }
}
