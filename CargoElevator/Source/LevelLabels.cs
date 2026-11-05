using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace CargoElevator;

/// <summary>
/// Supplies a label part for maps that one specific mod knows about.
/// Implementations resolve their reflection members once, when created.
/// </summary>
public interface ILevelLabelProvider
{
    /// <summary>Name used in logs, e.g. "DeepRim".</summary>
    string ProviderName { get; }

    /// <summary>Tag text shown in parentheses after the label, e.g. "DeepRim".</summary>
    string Tag { get; }

    /// <summary>
    /// Short label part without tag, e.g. "70m Mines" or "B1". False if this provider doesn't know the map.
    /// <paramref name="sortValue"/> orders levels of this provider (DeepRim depth, MultiFloors level).
    /// </summary>
    bool TryGetLabel(Map map, out string label, out int sortValue);
}

/// <summary>
/// Human readable level names. Labels are UI text only; pairing never depends on them.
/// Each supported mod contributes a part tagged with its name, read at runtime through reflection
/// (no compile-time reference). Without any known mod the vanilla label plus map id is used.
/// Labels are computed on demand and never cached, so they cannot go stale (only reflection members are cached).
/// </summary>
[StaticConstructorOnStartup]
public static class LevelLabels
{
    /// <summary>Added to a provider's sort value so negative values (basements) stay ordered inside one digit group.</summary>
    private const int SortValueOffset = 500;
    private const int SortValueRange = 1000;
    private const long SortKeyMapIdRange = 1_000_000;

    // Registered once at startup, in the order their parts appear in a combined label.
    private static readonly List<ILevelLabelProvider> providers = new();

    // Providers that threw an exception are switched off for the rest of the session.
    private static readonly HashSet<ILevelLabelProvider> disabledProviders = new();

    static LevelLabels()
    {
        TryRegister(DeepRimLabelProvider.TryCreate());
        TryRegister(MultiFloorsLabelProvider.TryCreate());
    }

    private static void TryRegister(ILevelLabelProvider provider)
    {
        if (provider != null)
        {
            providers.Add(provider);
        }
    }

    /// <summary>
    /// Prefixed with the map id so that levels with equal names stay distinguishable:
    /// "map27 - 90m (DeepRim)", "map25 - B1 (MultiFloors)", "map0 - surface",
    /// or the generic pocket map label "pocket map (map 5)".
    /// </summary>
    public static string LevelLabel(Map map)
    {
        if (map == null)
        {
            return "?";
        }

        var labelParts = new List<string>();
        foreach (ILevelLabelProvider provider in providers)
        {
            if (disabledProviders.Contains(provider))
            {
                continue;
            }

            try
            {
                if (provider.TryGetLabel(map, out string label, out _))
                {
                    labelParts.Add("CargoElevator_LabelWithTag".Translate(label, provider.Tag));
                }
            }
            catch (Exception exception)
            {
                DisableProvider(provider, exception);
            }
        }

        if (labelParts.Count > 0)
        {
            string modLabel = string.Join("CargoElevator_LabelSeparator".Translate(), labelParts);
            return "CargoElevator_MapPrefix".Translate(map.uniqueID, modLabel);
        }

        // A map on a world tile that no mod labelled is the surface (the map of a settlement).
        // Pocket maps keep the generic label: their parent's name plus the map id.
        if (map.Parent != null && map.Parent is not PocketMapParent)
        {
            return "CargoElevator_MapPrefix".Translate(map.uniqueID, "CargoElevator_Surface".Translate());
        }

        return "CargoElevator_VanillaLabel".Translate(map.Parent?.LabelCap ?? "?", map.uniqueID);
    }

    /// <summary>"custom name – level label" if the elevator has a custom name, else just the level label.</summary>
    public static string ElevatorLabel(Building_Elevator elevator)
    {
        string levelLabel = LevelLabel(elevator.Map);
        return elevator.HasCustomName
            ? "CargoElevator_ElevatorOnLevel".Translate(elevator.RenamableLabel, levelLabel).Resolve()
            : levelLabel;
    }

    /// <summary>
    /// Stable ordering key for menus: each provider's sort value in registration order
    /// (DeepRim depth, then MultiFloors level), then the map's unique id as a tie breaker.
    /// A map a provider doesn't know sorts at that provider's neutral value, so the surface comes first.
    /// </summary>
    public static long SortKey(Map map)
    {
        long sortKey = 0;
        foreach (ILevelLabelProvider provider in providers)
        {
            int sortValue = 0;
            if (!disabledProviders.Contains(provider))
            {
                try
                {
                    provider.TryGetLabel(map, out _, out sortValue);
                }
                catch (Exception exception)
                {
                    DisableProvider(provider, exception);
                }
            }

            sortKey = sortKey * SortValueRange + Math.Max(0, Math.Min(SortValueRange - 1, sortValue + SortValueOffset));
        }

        return sortKey * SortKeyMapIdRange + map.uniqueID;
    }

    private static void DisableProvider(ILevelLabelProvider provider, Exception exception)
    {
        if (disabledProviders.Add(provider))
        {
            ElevatorUtil.LogWarn($"{provider.ProviderName} labels failed and are disabled for this session; using generic labels. {exception}");
        }
    }
}
