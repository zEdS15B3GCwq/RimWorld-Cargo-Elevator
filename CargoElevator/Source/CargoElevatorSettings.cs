using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CargoElevator;

/// <summary>
/// The player's values for one elevator def. -1 = not changed: the def keeps the value it had at startup (its XML,
/// including other mods' patches), so the settings file only holds what the player actually changed.
/// </summary>
public class ElevatorDefSettings : IExposable
{
    public int ticksPerStack = -1;
    public int maxSlots = -1;
    public int activePower = -1;
    public int idlePower = -1;

    public void ExposeData()
    {
        Scribe_Values.Look(ref ticksPerStack, "ticksPerStack", -1);
        Scribe_Values.Look(ref maxSlots, "maxSlots", -1);
        Scribe_Values.Look(ref activePower, "activePower", -1);
        Scribe_Values.Look(ref idlePower, "idlePower", -1);
    }
}

/// <summary>
/// Mod settings (Options → Mod settings → Cargo Elevator), saved by the game in Config/Mod_&lt;folder&gt;_CargoElevatorMod.xml.
/// Per-def values are written into the defs (<see cref="ApplyToDefs"/>), so the rest of the code keeps reading
/// the defs. The light and logging switches are read live.
/// </summary>
public class CargoElevatorSettings : ModSettings
{
    public bool verboseLogging;
    public bool elevatorGlow = true;
    public bool loaderGlow = true;
    public bool loaderBlink = true;

    // Keyed by defName, so a def removed from the game only leaves an unused entry.
    private Dictionary<string, ElevatorDefSettings> elevatorDefSettings = new Dictionary<string, ElevatorDefSettings>();

    public override void ExposeData()
    {
        Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
        Scribe_Values.Look(ref elevatorGlow, "elevatorGlow", true);
        Scribe_Values.Look(ref loaderGlow, "loaderGlow", true);
        Scribe_Values.Look(ref loaderBlink, "loaderBlink", true);
        Scribe_Collections.Look(ref elevatorDefSettings, "elevators", LookMode.Value, LookMode.Deep);
        elevatorDefSettings ??= new Dictionary<string, ElevatorDefSettings>();
    }

    public ElevatorDefSettings For(ThingDef elevatorDef)
    {
        if (!elevatorDefSettings.TryGetValue(elevatorDef.defName, out ElevatorDefSettings defSettings))
        {
            defSettings = new ElevatorDefSettings();
            elevatorDefSettings[elevatorDef.defName] = defSettings;
        }
        return defSettings;
    }

    /// <summary>Back to the defaults: every value follows the def again, lights on, verbose logging off.</summary>
    public void ResetToDefaults()
    {
        verboseLogging = false;
        elevatorGlow = true;
        loaderGlow = true;
        loaderBlink = true;
        elevatorDefSettings.Clear();
    }

    // ---------------------------------------------------------------- the defs' own values

    /// <summary>What a def had at startup, before any setting was applied. Used as the default and for "reset".</summary>
    public class DefDefaults
    {
        public int ticksPerStack;
        public int maxSlots;
        public int defaultSlots;
        public int activePower;
        public int idlePower; // -1 = the def has no idle draw
    }

    // basePowerConsumption is private; Harmony is a dependency anyway.
    private static readonly AccessTools.FieldRef<CompProperties_Power, float> BasePowerConsumption =
        AccessTools.FieldRefAccess<CompProperties_Power, float>("basePowerConsumption");

    private static readonly Dictionary<ThingDef, DefDefaults> defaultsByDef = new Dictionary<ThingDef, DefDefaults>();

    /// <summary>The def's startup values; captured on first use, which is <see cref="ApplyToDefs"/> at startup.</summary>
    public static DefDefaults DefaultsOf(ThingDef elevatorDef)
    {
        if (!defaultsByDef.TryGetValue(elevatorDef, out DefDefaults defaults))
        {
            ElevatorSettingsExtension extension = elevatorDef.GetModExtension<ElevatorSettingsExtension>();
            CompProperties_Power powerProperties = elevatorDef.GetCompProperties<CompProperties_Power>();
            defaults = new DefDefaults
            {
                ticksPerStack = extension?.ticksPerStack ?? 0,
                maxSlots = extension?.maxSlots ?? 0,
                defaultSlots = extension?.defaultSlots ?? 0,
                activePower = powerProperties == null ? 0 : (int)BasePowerConsumption(powerProperties),
                idlePower = powerProperties == null ? -1 : (int)powerProperties.idlePowerDraw
            };
            defaultsByDef[elevatorDef] = defaults;
        }
        return defaults;
    }

    private static int Effective(int settingValue, int defaultValue) => settingValue >= 0 ? settingValue : defaultValue;

    public int EffectiveTicksPerStack(ThingDef elevatorDef) => Effective(For(elevatorDef).ticksPerStack, DefaultsOf(elevatorDef).ticksPerStack);
    public int EffectiveMaxSlots(ThingDef elevatorDef) => Effective(For(elevatorDef).maxSlots, DefaultsOf(elevatorDef).maxSlots);
    public int EffectiveActivePower(ThingDef elevatorDef) => Effective(For(elevatorDef).activePower, DefaultsOf(elevatorDef).activePower);
    public int EffectiveIdlePower(ThingDef elevatorDef) => Effective(For(elevatorDef).idlePower, DefaultsOf(elevatorDef).idlePower);

    // ---------------------------------------------------------------- applying

    /// <summary>
    /// Writes the per-def values into the defs (at startup and when the settings window closes), then lets every
    /// spawned elevator and loader pick up the change: slots, power draw and glow.
    /// </summary>
    public void ApplyToDefs()
    {
        foreach (ThingDef elevatorDef in ElevatorUtil.ElevatorDefs)
        {
            DefDefaults defaults = DefaultsOf(elevatorDef);
            ElevatorSettingsExtension extension = elevatorDef.GetModExtension<ElevatorSettingsExtension>();
            if (extension != null)
            {
                extension.ticksPerStack = EffectiveTicksPerStack(elevatorDef);
                extension.maxSlots = UnityEngine.Mathf.Max(extension.minSlots, EffectiveMaxSlots(elevatorDef));

                // New elevators start with as many slots as the def gives them: all of them if it did so (the
                // shipped defs do), otherwise the def's default, lowered to the maximum if needed.
                extension.defaultSlots = defaults.defaultSlots == defaults.maxSlots
                    ? extension.maxSlots
                    : UnityEngine.Mathf.Min(defaults.defaultSlots, extension.maxSlots);
            }

            CompProperties_Power powerProperties = elevatorDef.GetCompProperties<CompProperties_Power>();
            if (powerProperties != null)
            {
                BasePowerConsumption(powerProperties) = EffectiveActivePower(elevatorDef);
                if (defaults.idlePower >= 0)
                {
                    powerProperties.idlePowerDraw = EffectiveIdlePower(elevatorDef);
                }
            }
        }

        NotifySpawnedBuildings();
    }

    private static void NotifySpawnedBuildings()
    {
        if (Current.Game == null)
        {
            return;
        }

        foreach (Map map in Find.Maps)
        {
            foreach (ThingDef elevatorDef in ElevatorUtil.ElevatorDefs)
            {
                foreach (Thing thing in map.listerThings.ThingsOfDef(elevatorDef))
                {
                    (thing as Building_Elevator)?.Notify_ModSettingsChanged();
                }
            }

            foreach (Thing thing in map.listerThings.ThingsOfDef(CargoElevatorDefOf.CargoElevator_TransportLoader))
            {
                (thing as Building_TransportLoader)?.Notify_ModSettingsChanged();
            }
        }
    }
}
