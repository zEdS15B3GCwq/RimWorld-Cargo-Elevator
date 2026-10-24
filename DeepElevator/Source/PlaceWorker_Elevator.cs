using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepElevator;

/// <summary>
/// Placement rules for the elevator: the map needs exactly one DeepRim anchor (shaft or lift),
/// and the elevator must not touch it (DeepRim empties any storage next to a shaft/lift).
/// </summary>
public class PlaceWorker_Elevator : PlaceWorker
{
    public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
        Thing thingToIgnore = null, Thing thing = null)
    {
        Building anchor = ElevatorUtil.FindAnchor(map, out string failReason);
        if (anchor == null)
        {
            return new AcceptanceReport(failReason);
        }

        // ExpandedBy(1) covers the anchor's own cells plus the surrounding 8-neighbourhood.
        if (anchor.OccupiedRect().ExpandedBy(1).Contains(loc))
        {
            return "DeepElevator_AdjacentToAnchor".Translate();
        }

        return true;
    }

    /// <summary>Highlights the forbidden zone around the anchor while the ghost is being placed.</summary>
    public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
    {
        Map map = Find.CurrentMap;
        Building anchor = ElevatorUtil.FindAnchor(map, out _);
        if (anchor != null)
        {
            GenDraw.DrawFieldEdges(anchor.OccupiedRect().ExpandedBy(1).Cells.ToList(), Color.red);
        }
    }
}
