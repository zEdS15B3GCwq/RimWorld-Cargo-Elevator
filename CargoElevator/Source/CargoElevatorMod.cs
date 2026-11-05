using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CargoElevator;

/// <summary>
/// The mod's entry point for the game: loads the settings (in the constructor, before defs exist) and draws the
/// settings page. Changes take effect when the window closes (<see cref="WriteSettings"/>).
/// </summary>
public class CargoElevatorMod : Mod
{
    private const float RowHeight = 30f;
    private const float FieldWidth = 100f;
    private const float FooterHeight = 40f;
    private const float ResetButtonWidth = 220f;

    // Numeric ranges offered on the page.
    private const int TicksPerStackMin = 1, TicksPerStackMax = 600;
    private const int MaxSlotsMax = 20;
    private const int PowerMax = 5000;

    /// <summary>Never null once the game has constructed the mod, which happens before any other code of ours runs.</summary>
    public static CargoElevatorSettings Settings { get; private set; }

    private Vector2 scrollPosition;
    private float cachedScrollHeight = 1000f;

    // Text of the numeric fields while they are being edited, by field key.
    private readonly Dictionary<string, string> numericBuffers = new Dictionary<string, string>();

    public CargoElevatorMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<CargoElevatorSettings>();
    }

    public override string SettingsCategory() => "CargoElevator_SettingsCategory".Translate();

    public override void WriteSettings()
    {
        numericBuffers.Clear();
        base.WriteSettings();
        Settings.ApplyToDefs();
        ElevatorUtil.Log("Settings applied.");
    }

    // ---------------------------------------------------------------- page

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var scrollRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - FooterHeight - 5f);
        var footerRect = new Rect(inRect.x, inRect.yMax - FooterHeight, ResetButtonWidth, FooterHeight - 5f);
        var viewRect = new Rect(0f, 0f, scrollRect.width - 16f, cachedScrollHeight);

        Widgets.BeginScrollView(scrollRect, ref scrollPosition, viewRect);
        var listing = new Listing_Standard { maxOneColumn = true };
        listing.Begin(viewRect);

        listing.Label("CargoElevator_SettingsAppliedOnClose".Translate());

        Heading(listing, "CargoElevator_SettingsLights".Translate());
        listing.CheckboxLabeled("CargoElevator_SettingElevatorGlow".Translate(), ref Settings.elevatorGlow, "CargoElevator_SettingElevatorGlowDesc".Translate());
        listing.CheckboxLabeled("CargoElevator_SettingLoaderGlow".Translate(), ref Settings.loaderGlow, "CargoElevator_SettingLoaderGlowDesc".Translate());
        listing.CheckboxLabeled("CargoElevator_SettingLoaderBlink".Translate(), ref Settings.loaderBlink, "CargoElevator_SettingLoaderBlinkDesc".Translate());

        foreach (ThingDef elevatorDef in ElevatorUtil.ElevatorDefs)
        {
            DrawElevatorDefSettings(listing, elevatorDef);
        }

        Heading(listing, "CargoElevator_SettingsLogging".Translate());
        listing.CheckboxLabeled("CargoElevator_SettingVerboseLogging".Translate(), ref Settings.verboseLogging, "CargoElevator_SettingVerboseLoggingDesc".Translate());

        cachedScrollHeight = listing.CurHeight + 10f;
        listing.End();
        Widgets.EndScrollView();

        // A listing's button, because Widgets.ButtonText would need one more Unity assembly reference.
        var footer = new Listing_Standard();
        footer.Begin(footerRect);
        if (footer.ButtonText("CargoElevator_SettingsReset".Translate()))
        {
            Settings.ResetToDefaults();
            numericBuffers.Clear();
        }
        footer.End();
    }

    private void DrawElevatorDefSettings(Listing_Standard listing, ThingDef elevatorDef)
    {
        Heading(listing, elevatorDef.LabelCap);
        ElevatorDefSettings defSettings = Settings.For(elevatorDef);
        CargoElevatorSettings.DefDefaults defaults = CargoElevatorSettings.DefaultsOf(elevatorDef);
        string keyPrefix = elevatorDef.defName + ".";

        int ticksPerStack = Settings.EffectiveTicksPerStack(elevatorDef);
        string rateText = "CargoElevator_SettingPiecesPerSecond".Translate((GenTicks.TicksPerRealSecond / (float)ticksPerStack).ToString("0.##"));
        NumericField(listing, keyPrefix + "ticks", "CargoElevator_SettingTicksPerStack".Translate() + " " + rateText,
            ref defSettings.ticksPerStack, ticksPerStack, defaults.ticksPerStack, TicksPerStackMin, TicksPerStackMax,
            "CargoElevator_SettingTicksPerStackDesc".Translate());

        ElevatorSettingsExtension extension = elevatorDef.GetModExtension<ElevatorSettingsExtension>();
        NumericField(listing, keyPrefix + "slots", "CargoElevator_SettingMaxSlots".Translate(),
            ref defSettings.maxSlots, Settings.EffectiveMaxSlots(elevatorDef), defaults.maxSlots, extension?.minSlots ?? 1, MaxSlotsMax,
            "CargoElevator_SettingMaxSlotsDesc".Translate());

        NumericField(listing, keyPrefix + "active", "CargoElevator_SettingActivePower".Translate(),
            ref defSettings.activePower, Settings.EffectiveActivePower(elevatorDef), defaults.activePower, 0, PowerMax,
            "CargoElevator_SettingActivePowerDesc".Translate());

        if (defaults.idlePower >= 0)
        {
            NumericField(listing, keyPrefix + "idle", "CargoElevator_SettingIdlePower".Translate(),
                ref defSettings.idlePower, Settings.EffectiveIdlePower(elevatorDef), defaults.idlePower, 0, PowerMax,
                "CargoElevator_SettingIdlePowerDesc".Translate());
        }
    }

    // ---------------------------------------------------------------- widgets

    // Coloured rather than in a larger font: the Text class would need one more Unity assembly reference.
    private static void Heading(Listing_Standard listing, string text)
    {
        listing.Gap(8f);
        listing.Label(text.AsTipTitle());
        listing.GapLine();
    }

    /// <summary>
    /// One labelled number field. Shows the effective value; storing the def's own value keeps the setting at -1
    /// ("follow the def"), so only real changes end up in the settings file.
    /// </summary>
    private void NumericField(Listing_Standard listing, string key, string label, ref int settingValue, int effectiveValue, int defaultValue,
        int min, int max, string tooltip)
    {
        Rect row = listing.GetRect(RowHeight);
        var labelRect = new Rect(row.x, row.y + 5f, row.width - FieldWidth - 10f, row.height - 5f); // about centred on the field
        var fieldRect = new Rect(row.xMax - FieldWidth, row.y + 2f, FieldWidth, row.height - 4f);

        Widgets.Label(labelRect, label + " " + "CargoElevator_SettingDefault".Translate(defaultValue));

        int value = effectiveValue;
        numericBuffers.TryGetValue(key, out string buffer);
        Widgets.TextFieldNumeric(fieldRect, ref value, ref buffer, min, max);
        numericBuffers[key] = buffer;
        settingValue = value == defaultValue ? -1 : value;

        TooltipHandler.TipRegion(row, tooltip + "\n\n" + "CargoElevator_SettingRange".Translate(min, max));
    }
}
