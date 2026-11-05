using Verse;

namespace CargoElevator;

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

    // Pawns may haul items into the elevator (the "allow insert" switch).
    public bool defaultAllowInsert = true;

    // Ticks a transfer keeps both ends in the active power state; -1 = ticksPerStack + ActiveMarginTicks.
    public int activeHoldTicks = -1;
    public const int ActiveMarginTicks = 30;

    // "Blocked" (red light) needs this many send intervals without a delivery while items are held.
    public int blockedAfterSendIntervals = 2;

    // State lights: lights-only overlays on the same canvas as the base sprite (transparent except the lamp pixels),
    // drawn over it with a shader that ignores darkness, so they stay bright at night. Null = no light.
    public string greenLightTexPath, redLightTexPath;

    // Glow colour (and so brightness) per state; the radius is on the CompProperties_Glower. Alpha stays 0 like vanilla.
    public ColorInt greenGlowColor = new ColorInt(60, 200, 80, 0);
    public ColorInt redGlowColor = new ColorInt(220, 60, 50, 0);

    public int HeldRetryTicks => heldRetryTicks > 0 ? heldRetryTicks : ticksPerStack;

    public int ActiveHoldTicks => activeHoldTicks > 0 ? activeHoldTicks : ticksPerStack + ActiveMarginTicks;
}
