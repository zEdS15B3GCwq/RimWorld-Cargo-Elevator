using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CargoElevator;

/// <summary>
/// Shared helpers: logging, level groups, the center-relative pairing key and the candidate search.
/// A "level" is any map on the same world location (surface, DeepRim layer, MultiFloors level, pocket map).
/// </summary>
public static class ElevatorUtil
{
    public const string LogPrefix = "[CargoElevator] ";

    /// <summary>Guard against cycles or runaway chains when following PocketMapParent.sourceMap.</summary>
    private const int MaxSourceChainLength = 16;

    private static bool loggedSourceChainTooLong;

    private static List<ThingDef> elevatorDefs;

    /// <summary>
    /// Every def whose building class is an elevator (both sizes, and any later variant), found on first use.
    /// Not available while defs are still loading, so only call it from gameplay code.
    /// </summary>
    public static List<ThingDef> ElevatorDefs => elevatorDefs ??= DefDatabase<ThingDef>.AllDefsListForReading
        .Where(thingDef => typeof(Building_Elevator).IsAssignableFrom(thingDef.thingClass))
        .ToList();

    // Probe build, so logging is unconditional. Later steps may gate it on Prefs.DevMode.
    public static void Log(string message) => Verse.Log.Message(LogPrefix + message);

    public static void LogWarn(string message) => Verse.Log.Warning(LogPrefix + message);

    // ---------------------------------------------------------------- overlay graphics

    /// <summary>
    /// Loads a full-sprite overlay (same canvas and size as the def's base sprite, drawn over it), or null when the
    /// path is empty or the texture does not exist. Used for the elevator's state lights and the loader's
    /// "loading" look. The shader is Cutout (darkens at night like the building) unless the caller passes another.
    /// Call from gameplay code only (graphics are not available while defs load).
    /// </summary>
    public static Graphic LoadOverlayGraphic(ThingDef def, string texturePath, UnityEngine.Shader shader = null)
    {
        if (texturePath.NullOrEmpty())
        {
            return null;
        }

        // Checked first: GraphicDatabase would return a "bad" placeholder graphic and log an error.
        if (ContentFinder<UnityEngine.Texture2D>.Get(texturePath, reportFailure: false) == null)
        {
            LogWarn($"{def.defName}: overlay texture '{texturePath}' not found; it is not drawn.");
            return null;
        }
        return GraphicDatabase.Get<Graphic_Single>(texturePath, shader ?? ShaderDatabase.Cutout, def.graphicData.drawSize, UnityEngine.Color.white);
    }

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

            // Same def only: elevators of different sizes never pair.
            Building_Elevator otherElevator = ElevatorAnchoredAt(matchingCell, otherMap, self.def);
            if (otherElevator != null && otherElevator != self && otherElevator.Spawned && otherElevator.Partner == null)
            {
                candidates.Add(otherElevator);
            }
        }

        return candidates.OrderBy(candidate => LevelLabels.SortKey(candidate.Map)).ToList();
    }

    /// <summary>The elevator of def <paramref name="def"/> whose anchor (Position) is exactly <paramref name="cell"/>, or null.</summary>
    public static Building_Elevator ElevatorAnchoredAt(IntVec3 cell, Map map, ThingDef def)
    {
        List<Thing> thingsOnCell = cell.GetThingList(map);
        for (int index = 0; index < thingsOnCell.Count; index++)
        {
            if (thingsOnCell[index] is Building_Elevator elevator && elevator.def == def && elevator.Position == cell)
            {
                return elevator;
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- delivery cells

    /// <summary>
    /// The ring of in-bounds cells adjacent to <paramref name="receiver"/>, in a fixed order.
    /// These are the only cells ever used for delivery and drops: vanilla haulers walk to one specific
    /// cell, so reaching into a far cell of a big stockpile would be a teleport.
    /// </summary>
    public static List<IntVec3> OutputCells(Building_Elevator receiver)
    {
        var outputCells = new List<IntVec3>();
        CellRect ownRect = receiver.OccupiedRect();
        foreach (IntVec3 cell in ownRect.ExpandedBy(1).Cells)
        {
            if (!ownRect.Contains(cell) && cell.InBounds(receiver.Map))
            {
                outputCells.Add(cell);
            }
        }
        return outputCells;
    }

    /// <summary>
    /// How much of <paramref name="piece"/> a storage cell can take right now: free item slots times the
    /// stack limit, plus the headroom of same-kind partial stacks. 0 if the cell is not valid storage for it.
    /// </summary>
    public static int StorageCellRoom(IntVec3 cell, Map map, Thing piece)
    {
        if (!cell.IsValidStorageFor(map, piece))
        {
            return 0;
        }

        int stackLimit = piece.def.stackLimit;
        int freeItemSlots = Math.Max(0, cell.GetMaxItemsAllowedInCell(map) - cell.GetItemCount(map));
        int room = freeItemSlots * stackLimit;

        List<Thing> thingsOnCell = cell.GetThingList(map);
        for (int index = 0; index < thingsOnCell.Count; index++)
        {
            Thing thingOnCell = thingsOnCell[index];
            if (thingOnCell.def.category == ThingCategory.Item && thingOnCell.CanStackWith(piece))
            {
                room += Math.Max(0, stackLimit - thingOnCell.stackCount);
            }
        }
        return room;
    }

    /// <summary>
    /// How much of <paramref name="piece"/> a plain floor cell can take as a delivery drop.
    /// Never a storage cell, a blocked cell, or a cell with anything built or planned on it;
    /// an empty cell takes a full stack, a lone same-kind partial stack is topped up.
    /// </summary>
    public static int DeliveryDropRoom(IntVec3 cell, Map map, Thing piece)
    {
        if (!cell.InBounds(map) || cell.Fogged(map) || cell.GetSlotGroup(map) != null
            || !cell.Standable(map) || cell.GetEdifice(map) != null)
        {
            return 0;
        }

        int stackLimit = piece.def.stackLimit;
        Thing onlyItem = null;
        int itemCount = 0;
        List<Thing> thingsOnCell = cell.GetThingList(map);
        for (int index = 0; index < thingsOnCell.Count; index++)
        {
            ThingDef thingDef = thingsOnCell[index].def;
            if (thingDef.IsBlueprint || thingDef.IsFrame)
            {
                return 0;
            }
            if (thingDef.category == ThingCategory.Item)
            {
                onlyItem = thingsOnCell[index];
                itemCount++;
            }
        }

        if (itemCount == 0)
        {
            return stackLimit;
        }
        if (itemCount == 1 && onlyItem.CanStackWith(piece))
        {
            return Math.Max(0, stackLimit - onlyItem.stackCount);
        }
        return 0;
    }

    // ---------------------------------------------------------------- unloading

    /// <summary>
    /// Cells where vanilla-style "near" drops of buffer contents may land: not fogged, never an elevator
    /// (it would absorb and send the item again), and a storage cell only if that storage accepts the item.
    /// </summary>
    public static bool UnloadValidator(IntVec3 cell, Map map, Thing thing)
    {
        if (cell.Fogged(map) || cell.GetFirstThing<Building_Elevator>(map) != null)
        {
            return false;
        }

        SlotGroup slotGroup = cell.GetSlotGroup(map);
        return slotGroup == null || slotGroup.parent.Accepts(thing);
    }

    /// <summary>Puts a thing down near <paramref name="center"/> using <see cref="UnloadValidator"/>. False if there is no room.</summary>
    public static bool TryDropNear(Thing thing, IntVec3 center, Map map)
    {
        return GenPlace.TryPlaceThing(thing, center, map, ThingPlaceMode.Near, out _, null, cell => UnloadValidator(cell, map, thing));
    }

    /// <summary>Item description for logs: label without count, then the stack count.</summary>
    public static string Describe(Thing thing) => $"{thing.LabelCapNoCount} x{thing.stackCount}";

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
