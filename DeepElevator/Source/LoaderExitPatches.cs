using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DeepElevator;

/// <summary>
/// Which portals the game must never offer as "the way out of this map". The transport loader is a MapPortal, and
/// vanilla treats every MapPortal on a pocket map (MultiFloors levels, vanilla pocket maps) as an exit for pawns
/// that want to leave. The loader is no means of transportation for pawns that nobody sent: they would arrive on a
/// level they could not otherwise reach, and as elevators only connect pairs, might never get away from it again.
/// Pawns only use the loader on request: the portal dialog (an order the player gave) and the right-click "Enter".
/// To exclude another building later, extend <see cref="IsExcludedFromExitSearch"/>.
/// </summary>
public static class PortalExitRules
{
    public static bool IsExcludedFromExitSearch(Thing portal) => portal is Building_TransportLoader;
}

/// <summary>
/// The pawn AI's exit job. On a map without an edge (<c>!CanEverExit</c>) that is a pocket map, vanilla gives
/// <c>EnterPortal</c> on the first reachable MapPortal in the map's portal list, without asking whether that
/// portal is enterable. Used by visitors, traders, released prisoners and slaves, and by the overseer-following
/// mech exit (which calls the base method). If vanilla picked a loader, the job is replaced with one for the first
/// reachable portal that is not excluded, or no job at all when there is none. Without a patch the pawn would
/// either ride the loader (pair working) or retry a failing job forever (pair down), hiding every other portal.
/// </summary>
[HarmonyPatch(typeof(JobGiver_ExitMap), "TryGiveJob")]
public static class Patch_JobGiver_ExitMap_TryGiveJob
{
    public static void Postfix(Pawn pawn, ref Job __result)
    {
        if (__result == null || __result.def != JobDefOf.EnterPortal || !PortalExitRules.IsExcludedFromExitSearch(__result.targetA.Thing))
        {
            return;
        }

        __result = null;
        foreach (Thing portal in pawn.MapHeld.listerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.MapPortal)))
        {
            if (!PortalExitRules.IsExcludedFromExitSearch(portal) && pawn.CanReach(portal, PathEndMode.Touch, Danger.Deadly))
            {
                __result = JobMaker.MakeJob(JobDefOf.EnterPortal, portal); // the same job vanilla would have made
                return;
            }
        }
    }
}

/// <summary>
/// The player's right-click "Carry to exit" for a downed colonist, prisoner or capturable pawn on a pocket map
/// (<c>FloatMenuOptionProvider_CarryPawnToExit</c>). Vanilla picks the nearest MapPortal. If that is a loader, the
/// nearest portal that is not excluded is used instead; with none, the vanilla "no path" option shows.
/// </summary>
[HarmonyPatch(typeof(RCellFinder), nameof(RCellFinder.TryFindExitPortal))]
public static class Patch_RCellFinder_TryFindExitPortal
{
    public static void Postfix(Pawn pawn, ref bool __result, ref Thing portal)
    {
        if (!__result || !PortalExitRules.IsExcludedFromExitSearch(portal))
        {
            return;
        }

        portal = null;
        float nearestDistanceSquared = float.MaxValue;
        foreach (Thing candidate in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.MapPortal))
        {
            float distanceSquared = candidate.Position.DistanceToSquared(pawn.Position);
            if (!PortalExitRules.IsExcludedFromExitSearch(candidate) && distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                portal = candidate;
            }
        }
        __result = portal != null;
    }
}
