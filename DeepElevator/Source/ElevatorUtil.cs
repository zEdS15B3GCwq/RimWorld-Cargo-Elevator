using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DeepElevator;

/// <summary>
/// Shared helpers: logging, level groups, the center-relative pairing key and the candidate search.
/// A "level" is any map on the same world location (surface, DeepRim layer, MultiFloors level, pocket map).
/// </summary>
public static class ElevatorUtil
{
    public const string LogPrefix = "[DeepElevator] ";

    /// <summary>Guard against cycles or runaway chains when following PocketMapParent.sourceMap.</summary>
    private const int MaxSourceChainLength = 16;

    private static bool loggedSourceChainTooLong;

    // Probe build, so logging is unconditional. Later steps may gate it on Prefs.DevMode.
    public static void Log(string message) => Verse.Log.Message(LogPrefix + message);

    public static void LogWarn(string message) => Verse.Log.Warning(LogPrefix + message);

    // ---------------------------------------------------------------- level groups

    /// <summary>
    /// The map whose world tile defines the level group. Vanilla pocket maps have no valid tile,
    /// so the chain of PocketMapParent.sourceMap is followed until a map with a valid tile is found.
    /// </summary>
    public static Map GroupRoot(Map map)
    {
        Map current = map;
        for (int step = 0; current != null && step < MaxSourceChainLength; step++)
        {
            if (current.Tile.Valid)
            {
                return current;
            }

            if (current.Parent is PocketMapParent pocketParent && pocketParent.sourceMap != null && pocketParent.sourceMap != current)
            {
                current = pocketParent.sourceMap;
                continue;
            }

            // No valid tile and no source map: this map forms a group of its own.
            return current;
        }

        if (!loggedSourceChainTooLong)
        {
            loggedSourceChainTooLong = true;
            LogWarn($"GroupRoot: source chain of map#{map?.uniqueID} is longer than {MaxSourceChainLength}; using the last map reached as root.");
        }
        return current;
    }

    /// <summary>
    /// Same level group: both roots are the same map, or both have a valid tile and the tiles are equal.
    /// Two unrelated pocket maps never match just because both tiles are invalid.
    /// </summary>
    public static bool SameGroup(Map firstMap, Map secondMap)
    {
        if (firstMap == null || secondMap == null)
        {
            return false;
        }

        Map firstRoot = GroupRoot(firstMap);
        Map secondRoot = GroupRoot(secondMap);
        if (firstRoot == secondRoot)
        {
            return true;
        }

        return firstRoot.Tile.Valid && secondRoot.Tile.Valid && firstRoot.Tile == secondRoot.Tile;
    }

    /// <summary>The other maps of the same level group (not including <paramref name="map"/> itself).</summary>
    public static IEnumerable<Map> OtherLevelMaps(Map map)
    {
        foreach (Map otherMap in Find.Maps)
        {
            if (otherMap != map && SameGroup(map, otherMap))
            {
                yield return otherMap;
            }
        }
    }

    // ---------------------------------------------------------------- pairing key

    /// <summary>The pairing key: a cell's offset from the center of its map.</summary>
    public static IntVec3 CenterOffset(IntVec3 cell, Map map) => cell - map.Center;

    /// <summary>
    /// The cell on <paramref name="otherMap"/> that corresponds to <paramref name="cell"/> on <paramref name="map"/>:
    /// the maps are overlaid 1:1 at their centers (no scaling). False if that cell is outside the other map.
    /// The mapping is symmetric, so pairing from either side finds the same cell.
    /// </summary>
    public static bool TryGetMatchingCell(IntVec3 cell, Map map, Map otherMap, out IntVec3 matchingCell)
    {
        matchingCell = otherMap.Center + CenterOffset(cell, map);
        return matchingCell.InBounds(otherMap);
    }

    /// <summary>
    /// Unconnected, spawned elevators on other levels of the group that sit at the matching cell of
    /// <paramref name="self"/>. Looks up one cell per map, so it is cheap. Ordered like the Connect menu.
    /// </summary>
    public static List<Building_Elevator> UnconnectedCandidates(Building_Elevator self)
    {
        var candidates = new List<Building_Elevator>();
        if (!self.Spawned)
        {
            return candidates;
        }

        foreach (Map otherMap in OtherLevelMaps(self.Map))
        {
            if (!TryGetMatchingCell(self.Position, self.Map, otherMap, out IntVec3 matchingCell))
            {
                continue;
            }

            Building_Elevator otherElevator = matchingCell.GetFirstThing<Building_Elevator>(otherMap);
            if (otherElevator != null && otherElevator != self && otherElevator.Spawned && otherElevator.Partner == null)
            {
                candidates.Add(otherElevator);
            }
        }

        return candidates.OrderBy(candidate => LevelLabels.SortKey(candidate.Map)).ToList();
    }

    // ---------------------------------------------------------------- logging

    /// <summary>For logs: the level label (which already contains the map id). Safe for null and already removed maps.</summary>
    public static string MapDesc(Map map)
    {
        if (map == null)
        {
            return "map#null";
        }

        try
        {
            return LevelLabels.LevelLabel(map);
        }
        catch (Exception)
        {
            return $"map#{map.uniqueID}";
        }
    }
}
