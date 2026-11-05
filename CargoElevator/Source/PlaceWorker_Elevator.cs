using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CargoElevator;

/// <summary>
/// Placement helper. There are no placement rules; while an elevator is being placed this worker shows
/// where connections are possible: it highlights cells that match an unconnected elevator on another level,
/// outlines the area the current map shares with smaller levels, and explains the hovered cell under the cursor.
/// Everything is cached and rebuilt rarely, because the game calls these hooks every frame while placing.
/// </summary>
public class PlaceWorker_Elevator : PlaceWorker
{
    /// <summary>How often the cached cells are rebuilt while placing, so changes on other levels show up.</summary>
    private const int RebuildIntervalFrames = 60;

    private const float MouseTextLineHeight = 29f;
    private const float MouseTextWidth = 999f;

    // ---- cache, valid for (cachedMap, cachedDef) until the rebuild interval has passed
    private static Map cachedMap;
    private static ThingDef cachedDef;
    private static int cachedFrame = -RebuildIntervalFrames;

    // Every footprint cell on the current map that matches an unconnected elevator of the placed def on another level (for DrawFieldEdges).
    private static readonly List<IntVec3> targetCells = new();

    // The anchor cells of those footprints with the elevators they would connect to (for the cursor text).
    private static readonly Dictionary<IntVec3, List<Building_Elevator>> targetsByCell = new();

    // Other levels that are smaller than the current map: the area of the current map they cover.
    private static readonly List<(Map otherMap, CellRect overlap)> partialOverlaps = new();

    // ---------------------------------------------------------------- cache

    private static void EnsureCache(Map map, ThingDef def)
    {
        if (map == cachedMap && def == cachedDef && Time.frameCount - cachedFrame < RebuildIntervalFrames)
        {
            return;
        }

        targetCells.Clear();
        targetsByCell.Clear();
        partialOverlaps.Clear();

        foreach (Map otherMap in ElevatorUtil.OtherLevelMaps(map))
        {
            AddPartialOverlap(map, otherMap);
            AddTargets(map, otherMap, def);
        }

        cachedMap = map;
        cachedDef = def;
        cachedFrame = Time.frameCount;
    }

    /// <summary>
    /// Remembers the part of <paramref name="map"/> covered by <paramref name="otherMap"/>, if that is not all of it.
    /// The other map is overlaid 1:1 at the center, so its rectangle is its size shifted by the center difference.
    /// </summary>
    private static void AddPartialOverlap(Map map, Map otherMap)
    {
        IntVec3 centerDifference = map.Center - otherMap.Center;
        CellRect overlap = new CellRect(centerDifference.x, centerDifference.z, otherMap.Size.x, otherMap.Size.z).ClipInsideMap(map);
        if (overlap.Area < map.Size.x * map.Size.z)
        {
            partialOverlaps.Add((otherMap, overlap));
        }
    }

    /// <summary>
    /// Adds the cells of <paramref name="map"/> that match unconnected elevators of def <paramref name="def"/> on
    /// <paramref name="otherMap"/>. Only the same def can pair, so other sizes are not highlighted.
    /// </summary>
    private static void AddTargets(Map map, Map otherMap, ThingDef def)
    {
        List<Thing> elevatorsThere = otherMap.listerThings.ThingsOfDef(def);
        for (int elevatorIndex = 0; elevatorIndex < elevatorsThere.Count; elevatorIndex++)
        {
            if (elevatorsThere[elevatorIndex] is not Building_Elevator elevator || !elevator.Spawned || elevator.Partner != null)
            {
                continue;
            }

            // The mapping is symmetric, so the cell here is the one that would pair with that elevator.
            if (!ElevatorUtil.TryGetMatchingCell(elevator.Position, otherMap, map, out IntVec3 cellHere))
            {
                continue;
            }

            // The anchor is where the ghost is centered, so it keys the cursor text; the whole footprint is outlined.
            if (!targetsByCell.TryGetValue(cellHere, out List<Building_Elevator> elevatorsAtCell))
            {
                elevatorsAtCell = new List<Building_Elevator>();
                targetsByCell[cellHere] = elevatorsAtCell;
                foreach (IntVec3 footprintCell in GenAdj.OccupiedRect(cellHere, Rot4.North, def.Size))
                {
                    if (footprintCell.InBounds(map) && !targetCells.Contains(footprintCell))
                    {
                        targetCells.Add(footprintCell);
                    }
                }
            }
            elevatorsAtCell.Add(elevator);
        }
    }

    // ---------------------------------------------------------------- world pass

    public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
    {
        using (ElevatorProfiler.Measure("PlaceWorker.DrawGhost"))
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            EnsureCache(map, def);
            if (targetCells.Count > 0)
            {
                GenDraw.DrawFieldEdges(targetCells, Color.green);
            }
            foreach ((Map _, CellRect overlap) in partialOverlaps)
            {
                DrawRectOutline(overlap, SimpleColor.Yellow);
            }
        }
    }

    /// <summary>Outlines a rectangle with four lines; DrawFieldEdges would walk every cell of a large area.</summary>
    private static void DrawRectOutline(CellRect rect, SimpleColor color)
    {
        float altitude = AltitudeLayer.MetaOverlays.AltitudeFor();
        var minCorner = new Vector3(rect.minX, altitude, rect.minZ);
        var minXMaxZCorner = new Vector3(rect.minX, altitude, rect.maxZ + 1);
        var maxCorner = new Vector3(rect.maxX + 1, altitude, rect.maxZ + 1);
        var maxXMinZCorner = new Vector3(rect.maxX + 1, altitude, rect.minZ);

        GenDraw.DrawLineBetween(minCorner, maxXMinZCorner, color);
        GenDraw.DrawLineBetween(maxXMinZCorner, maxCorner, color);
        GenDraw.DrawLineBetween(maxCorner, minXMaxZCorner, color);
        GenDraw.DrawLineBetween(minXMaxZCorner, minCorner, color);
    }

    // ---------------------------------------------------------------- GUI pass

    public override void DrawPlaceMouseAttachments(float curX, ref float curY, BuildableDef def, IntVec3 center, Rot4 rot)
    {
        using (ElevatorProfiler.Measure("PlaceWorker.DrawPlaceMouseAttachments"))
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            // center is the cell under the mouse.
            EnsureCache(map, def as ThingDef);
            if (targetsByCell.TryGetValue(center, out List<Building_Elevator> elevatorsAtCell))
            {
                foreach (Building_Elevator elevator in elevatorsAtCell)
                {
                    DrawMouseTextLine(curX, ref curY, "CargoElevator_HelperConnectsTo".Translate(LevelLabels.ElevatorLabel(elevator)), Color.green);
                }
            }

            foreach ((Map otherMap, CellRect overlap) in partialOverlaps)
            {
                if (!overlap.Contains(center))
                {
                    DrawMouseTextLine(curX, ref curY, "CargoElevator_HelperOutsideMap".Translate(LevelLabels.LevelLabel(otherMap)), Color.yellow);
                }
            }
        }
    }

    /// <summary>One line of cursor text; shared with the transport loader's place worker.</summary>
    internal static void DrawMouseTextLine(float curX, ref float curY, string text, Color color)
    {
        Text.Font = GameFont.Small;
        GUI.color = color;
        Widgets.Label(new Rect(curX, curY, MouseTextWidth, MouseTextLineHeight), text);
        GUI.color = Color.white;
        curY += MouseTextLineHeight;
    }
}
