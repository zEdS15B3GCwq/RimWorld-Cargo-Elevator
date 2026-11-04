using RimWorld;
using Verse;

namespace DeepElevator;

/// <summary>Def references for this mod, filled in by the game after defs are loaded.</summary>
[DefOf]
public static class DeepElevatorDefOf
{
    public static ThingDef DeepElevator_Elevator;
    public static ThingDef DeepElevator_ElevatorLarge;
    public static ThingDef DeepElevator_TransportLoader;

    static DeepElevatorDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(DeepElevatorDefOf));
    }
}
