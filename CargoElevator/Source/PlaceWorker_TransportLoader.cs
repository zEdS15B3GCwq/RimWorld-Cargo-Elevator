using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CargoElevator;

/// <summary>
/// Placement helper for the transport loader. There is no placement rule (a loader may be built anywhere);
/// while placing, the cells next to elevators are shaded green, because a loader only does something there,
/// and the cursor text says what the hovered cell would connect to.
/// Everything is cached and rebuilt rarely, because the game calls these hooks every frame while placing.
/// </summary>
public class PlaceWorker_TransportLoader : PlaceWorker
{
    /// <summary>How often the cached cells are rebuilt while placing, so new elevators show up.</summary>
    private const int RebuildIntervalFrames = 60;

    // ---- cache, valid for cachedMap until the rebuild interval has passed
    private static Map cachedMap;
    private static int cachedFrame = -RebuildIntervalFrames;

    // Every in-bounds cell cardinally next to a spawned elevator on the current map (for DrawFieldEdges).
    private static readonly List<IntVec3> cellsNextToElevators = new();

    // The same cells with the elevators they touch (for the cursor text). A cell can touch several.
    private static readonly Dictionary<IntVec3, List<Building_Elevator>> elevatorsByCell = new();

    // ---------------------------------------------------------------- cache

    private static void EnsureCache(Map map)
    {
        if (map == cachedMap && Time.frameCount - cachedFrame < RebuildIntervalFrames)
        {
            return;
        }

        cellsNextToElevators.Clear();
        elevatorsByCell.Clear();

        foreach (ThingDef elevatorDef in ElevatorUtil.ElevatorDefs)
        {
            List<Thing> elevatorsOnMap = map.listerThings.ThingsOfDef(elevatorDef);
            for (int elevatorIndex = 0; elevatorIndex < elevatorsOnMap.Count; elevatorIndex++)
            {
                if (elevatorsOnMap[elevatorIndex] is Building_Elevator { Spawned: true } elevator)
                {
                    AddCellsNextTo(elevator, map);
                }
            }
        }

        cachedMap = map;
        cachedFrame = Time.frameCount;
    }

    private static void AddCellsNextTo(Building_Elevator elevator, Map map)
    {
        foreach (IntVec3 adjacentCell in GenAdj.CellsAdjacentCardinal(elevator))
        {
            if (!adjacentCell.InBounds(map))
            {
                continue;
            }

            if (!elevatorsByCell.TryGetValue(adjacentCell, out List<Building_Elevator> elevatorsAtCell))
            {
                elevatorsAtCell = new List<Building_Elevator>();
                elevatorsByCell[adjacentCell] = elevatorsAtCell;
                cellsNextToElevators.Add(adjacentCell);
            }
            elevatorsAtCell.Add(elevator);
        }
    }

    // ---------------------------------------------------------------- world pass

    public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
    {
        Map map = Find.CurrentMap;
        if (map == null)
        {
            return;
        }

        EnsureCache(map);
        if (cellsNextToElevators.Count > 0)
        {
            GenDraw.DrawFieldEdges(cellsNextToElevators, Color.green);
        }
    }

    // ---------------------------------------------------------------- GUI pass

    public override void DrawPlaceMouseAttachments(float curX, ref float curY, BuildableDef def, IntVec3 center, Rot4 rot)
    {
        Map map = Find.CurrentMap;
        if (map == null)
        {
            return;
        }

        // center is the cell under the mouse (a loader is 1x1).
        EnsureCache(map);
        if (!elevatorsByCell.TryGetValue(center, out List<Building_Elevator> elevatorsAtCell))
        {
            return;
        }

        if (elevatorsAtCell.Count == 1)
        {
            PlaceWorker_Elevator.DrawMouseTextLine(curX, ref curY, "CargoElevator_HelperConnectsTo".Translate(elevatorsAtCell[0].LabelCap), Color.green);
        }
        else
        {
            PlaceWorker_Elevator.DrawMouseTextLine(curX, ref curY, "CargoElevator_HelperSeveralElevators".Translate(), Color.green);
        }
    }
}
