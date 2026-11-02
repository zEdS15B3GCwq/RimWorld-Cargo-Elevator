using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DeepElevator;

/// <summary>Applies all Harmony patches of this assembly once, after the game's content has loaded.</summary>
[StaticConstructorOnStartup]
public static class HarmonyPatches
{
    static HarmonyPatches()
    {
        var harmony = new Harmony("FT.deepelevator");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        ElevatorUtil.Log($"Harmony: patched {harmony.GetPatchedMethods().EnumerableCount()} method(s)");
    }
}

/// <summary>
/// Makes the haul search see the elevator's real acceptance rules.
/// The game's search (StoreUtility.TryFindBestBetterStoreCellFor) uses only the storage filter and per-cell
/// checks (IsGoodStoreCell); it never asks the building's Accepts. The hauling job then fails at once when
/// Accepts says no, and the pawn restarts the same job ("started 10 jobs in one tick"). A vanilla shelf cannot
/// disagree with itself because its limits are cell contents, which IsGoodStoreCell checks. The elevator's cells
/// are always empty, so its limits (buffer full, no power, not connected, "allow insert" off) are applied here:
/// a cell of an elevator is only a good store cell when <see cref="Building_Elevator.AcceptsPawnDelivery"/> says so.
/// Same approach as Vanilla Furniture Expanded - Factory uses for its hoppers.
/// </summary>
[HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsGoodStoreCell))]
public static class Patch_StoreUtility_IsGoodStoreCell
{
    // Postfix: only ever turns true into false, so it can never make the search more permissive than vanilla.
    public static void Postfix(IntVec3 c, Map map, Thing t, ref bool __result)
    {
        if (!__result || t == null)
        {
            return;
        }

        if (map.haulDestinationManager.SlotGroupParentAt(c) is Building_Elevator elevator && !elevator.AcceptsPawnDelivery(t))
        {
            __result = false; // the reason log in AcceptsPawnDelivery is a disabled probe
        }
    }
}
