using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace CargoElevator;

/// <summary>
/// The walking half of an order: <c>JobGiver_EnterPortal</c> gives a pawn of the order's lord an <c>EnterPortal</c>
/// job without asking whether the portal is enterable, and that job fails at once when it is not. The think tree
/// then gives the same job again in the same tick, which ends in "started 10 jobs in one tick" and an error
/// recovery. A loader is not enterable while its elevator pair is disconnected or unpowered, which can happen
/// when the order is made or changed, or when power drops during it. While that lasts the giver gives no job, so
/// the pawn idles and the order waits; when the pair works again the next think pass gives the job as usual.
/// Other portals are left to vanilla.
/// </summary>
[HarmonyPatch(typeof(JobGiver_EnterPortal), "TryGiveJob")]
public static class Patch_JobGiver_EnterPortal_TryGiveJob
{
    public static void Postfix(ref Job __result)
    {
        if (__result != null && __result.targetA.Thing is Building_TransportLoader loader && !loader.IsEnterable(out _))
        {
            __result = null;
        }
    }
}
