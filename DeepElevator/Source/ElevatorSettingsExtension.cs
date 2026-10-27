using Verse;

namespace DeepElevator;

/// <summary>Tunables for the elevator, set in the ThingDef (no mod settings).</summary>
public class ElevatorSettingsExtension : DefModExtension
{
    // Buffer size in slots (a slot holds one full stack).
    public int defaultSlots = 4;
    public int minSlots = 1;
    public int maxSlots = 20;

    // One piece (at most one full stack) is sent per this many ticks.
    public int ticksPerStack = 60;

    // Ticks before a held entry is retried; -1 = same as ticksPerStack.
    public int heldRetryTicks = -1;

    public bool defaultFifo = true;
    public bool defaultMerge = true;
    public bool defaultAllowDrop = true;

    public int HeldRetryTicks => heldRetryTicks > 0 ? heldRetryTicks : ticksPerStack;
}
