using System;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepElevator;

/// <summary>
/// Placement rule for the elevator: the map needs exactly one DeepRim anchor (shaft or lift).
/// (A rule keeping elevators away from the anchor existed in Step A and is commented out below.)
/// </summary>
public class PlaceWorker_Elevator : PlaceWorker
{
    private const string AllowsPlacingSection = "PlaceWorker.AllowsPlacing";

    // Probe: the game was seen calling AllowsPlacing ~20000 times in one frame. When a frame passes this
    // many calls, log who is calling (a few times only), to find the code that scans cells.
    private const int SuspiciousCallsPerFrame = 500;
    private const int MaxCallerReports = 3;
    private static int callerReportsLogged;

    public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
        Thing thingToIgnore = null, Thing thing = null)
    {
        AcceptanceReport report;
        using (ElevatorProfiler.Measure(AllowsPlacingSection))
        {
            report = EvaluatePlacement(loc, map);
            ElevatorProfiler.Note(AllowsPlacingSection, $"cell={loc} result={(report.Accepted ? "accepted" : report.Reason)}");
        }

        if (callerReportsLogged < MaxCallerReports && ElevatorProfiler.GetCallCount(AllowsPlacingSection) == SuspiciousCallsPerFrame)
        {
            callerReportsLogged++;
            ElevatorUtil.LogWarn($"AllowsPlacing was called {SuspiciousCallsPerFrame} times within one frame. Caller:{Environment.NewLine}{new StackTrace(skipFrames: 1, fNeedFileInfo: false)}");
        }

        return report;
    }

    private static AcceptanceReport EvaluatePlacement(IntVec3 loc, Map map)
    {
        Building anchor = ElevatorUtil.FindAnchor(map, out string failReason);
        if (anchor == null)
        {
            return new AcceptanceReport(failReason);
        }

        // Disabled by decision (see results_stepA.md, section 5): elevators may touch the shaft/lift. The player
        // is expected to know that DeepRim pulls items out of adjacent storage. Kept in case adjacency rules return.
        // ExpandedBy(1) covers the anchor's own cells plus the surrounding 8-neighbourhood.
        // if (anchor.OccupiedRect().ExpandedBy(1).Contains(loc))
        // {
        //     return "DeepElevator_AdjacentToAnchor".Translate();
        // }

        return true;
    }

    // Disabled together with the adjacency rule above: highlighted the forbidden zone around the anchor.
    // /// <summary>Highlights the forbidden zone around the anchor while the ghost is being placed.</summary>
    // public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
    // {
    //     using (ElevatorProfiler.Measure("PlaceWorker.DrawGhost"))
    //     {
    //         Map map = Find.CurrentMap;
    //         Building anchor = ElevatorUtil.FindAnchor(map, out _);
    //         if (anchor != null)
    //         {
    //             GenDraw.DrawFieldEdges(anchor.OccupiedRect().ExpandedBy(1).Cells.ToList(), Color.red);
    //         }
    //     }
    // }
}
