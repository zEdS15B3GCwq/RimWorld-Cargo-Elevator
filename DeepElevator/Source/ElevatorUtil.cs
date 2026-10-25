using System;
using System.Collections.Generic;
using System.Linq;
using DeepRim;
using RimWorld;
using Verse;

namespace DeepElevator;

/// <summary>
/// Shared helpers: logging, DeepRim anchor lookup, pairing key and candidate search, layer labels.
/// </summary>
public static class ElevatorUtil
{
    public const string LogPrefix = "[DeepElevator] ";

    /// <summary>DeepRim shows a layer's depth as depth * 10 metres.</summary>
    private const int MetresPerDepthLevel = 10;

    // Step A is a probe build, so logging is unconditional. Later steps may gate it on Prefs.DevMode.
    public static void Log(string message) => Verse.Log.Message(LogPrefix + message);

    public static void LogWarn(string message) => Verse.Log.Warning(LogPrefix + message);

    /// <summary>
    /// The DeepRim anchor of a map: the mining shaft (surface) or the lift (deep layer).
    /// Returns null with a reason if the map has none, or more than one.
    /// </summary>
    public static Building FindAnchor(Map map, out string failReason)
    {
        using (ElevatorProfiler.Measure("ElevatorUtil.FindAnchor"))
        {
            return FindAnchorUnmeasured(map, out failReason);
        }
    }

    private static Building FindAnchorUnmeasured(Map map, out string failReason)
    {
        failReason = null;
        if (map == null)
        {
            failReason = "DeepElevator_NoAnchor".Translate();
            return null;
        }

        // listerThings keeps a per-def index, so these lookups cost the same on a huge map as on a tiny one.
        // (The earlier AllBuildingsColonistOfClass / OfDef version scans every colonist building on the map;
        // it was measured at ~0.13ms per call on the surface, and the game calls it thousands of times per frame.)
        // Only player-owned buildings count; DeepRim gives the lift the player faction.
        Building anchor = null;
        int anchorCount = 0;
        CountPlayerBuildings(map, ShaftThingDefOf.miningshaft, ref anchor, ref anchorCount);
        CountPlayerBuildings(map, ShaftThingDefOf.undergroundlift, ref anchor, ref anchorCount);

        if (anchorCount == 0)
        {
            failReason = "DeepElevator_NoAnchor".Translate();
            return null;
        }
        if (anchorCount > 1)
        {
            failReason = "DeepElevator_MultipleAnchors".Translate();
            return null;
        }

        return anchor;
    }

    /// <summary>Counts the player's spawned buildings of a def on a map, remembering the last one found.</summary>
    private static void CountPlayerBuildings(Map map, ThingDef buildingDef, ref Building lastFound, ref int count)
    {
        List<Thing> things = map.listerThings.ThingsOfDef(buildingDef);
        for (int index = 0; index < things.Count; index++)
        {
            if (things[index] is Building building && building.Faction == Faction.OfPlayer)
            {
                lastFound = building;
                count++;
            }
        }
    }

    /// <summary>
    /// The pairing key: the elevator's offset from its map's anchor. Two elevators pair when keys match.
    /// </summary>
    public static bool TryGetKey(Thing elevator, out IntVec3 key)
    {
        Building anchor = FindAnchor(elevator.Map, out _);
        if (anchor == null)
        {
            key = IntVec3.Invalid;
            return false;
        }

        key = elevator.Position - anchor.Position;
        return true;
    }

    /// <summary>Other maps on the same world tile that are the surface or a DeepRim layer (have exactly one anchor).</summary>
    public static IEnumerable<Map> OtherLayerMaps(Map map)
    {
        foreach (Map otherMap in Find.Maps)
        {
            if (otherMap != map && otherMap.Tile == map.Tile && FindAnchor(otherMap, out _) != null)
            {
                yield return otherMap;
            }
        }
    }

    /// <summary>
    /// Unconnected, spawned elevators on other layers of the same tile that sit at the same offset
    /// from their anchor as <paramref name="self"/>. Looks up one cell per map, so it is cheap.
    /// </summary>
    public static List<Building_Elevator> UnconnectedCandidates(Building_Elevator self)
    {
        var candidates = new List<Building_Elevator>();
        if (!self.Spawned || !TryGetKey(self, out IntVec3 key))
        {
            return candidates;
        }

        foreach (Map otherMap in OtherLayerMaps(self.Map))
        {
            Building otherAnchor = FindAnchor(otherMap, out _);
            IntVec3 matchingCell = otherAnchor.Position + key;
            // No scaling between map sizes: a cell outside the other map means no match.
            if (!matchingCell.InBounds(otherMap))
            {
                continue;
            }

            Building_Elevator otherElevator = matchingCell.GetFirstThing<Building_Elevator>(otherMap);
            if (otherElevator != null && otherElevator != self && otherElevator.Spawned && otherElevator.Partner == null)
            {
                candidates.Add(otherElevator);
            }
        }

        return candidates;
    }

    /// <summary>Human readable layer name: "surface", "layer at 30m", "layer at 30m (Mines)".</summary>
    public static string LayerLabel(Map map)
    {
        if (map == null)
        {
            return "?";
        }

        if (map.Parent is not UndergroundMapParent layerParent)
        {
            return "DeepElevator_Surface".Translate();
        }

        int depthMetres = layerParent.depth * MetresPerDepthLevel;

        // Custom layer names are stored on the surface map's UndergroundManager.
        Map surfaceMap = Find.Maps.FirstOrDefault(otherMap => otherMap.Tile == map.Tile && otherMap.Parent is not UndergroundMapParent);
        string layerName = surfaceMap?.GetComponent<UndergroundManager>()?.GetLayerName(layerParent.depth);

        return layerName.NullOrEmpty()
            ? "DeepElevator_Layer".Translate(depthMetres)
            : "DeepElevator_LayerNamed".Translate(depthMetres, layerName);
    }

    /// <summary>For logs: "map#id (label)". Safe for null and already removed maps.</summary>
    public static string MapDesc(Map map)
    {
        if (map == null)
        {
            return "map#null";
        }

        try
        {
            return $"map#{map.uniqueID} ({LayerLabel(map)})";
        }
        catch (Exception)
        {
            return $"map#{map.uniqueID}";
        }
    }
}
