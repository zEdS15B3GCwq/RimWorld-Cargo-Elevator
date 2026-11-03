using RimWorld;
using Verse;

namespace DeepElevator;

/// <summary>
/// What vanilla hauling deposits into (<c>MapPortal.containerProxy</c>). Items go into the loader's container,
/// where the elevator pulls them out. People and animals keep the vanilla portal behaviour: they are put down
/// next to the partner elevator on the other level, without the buffer, slots or send rate.
/// </summary>
public class LoaderContainerProxy : PortalContainerProxy
{
    public Building_TransportLoader loader;

    public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
    {
        if (item is Pawn pawn)
        {
            return DepositPawn(pawn, canMergeWithExistingStacks);
        }
        return DepositItem(item, canMergeWithExistingStacks);
    }

    /// <summary>Carried-in pawns and animals: vanilla drops them near the destination, but only while the pair works.</summary>
    private bool DepositPawn(Pawn pawn, bool canMergeWithExistingStacks)
    {
        if (loader.IsEnterable(out _))
        {
            ElevatorUtil.Log($"Deposited (loader): {loader.ThingID} sends {pawn.LabelShort} to {ElevatorUtil.MapDesc(loader.GetOtherMap())}");
            return base.TryAdd(pawn, canMergeWithExistingStacks); // vanilla: Notify_ThingAdded, then dropped next to the partner elevator
        }

        // Hauling does not check IsEnterable, so a pawn can arrive while the pair is down. Put it down again,
        // take it off the order so it is not hauled forever, and tell the player. No retry loop.
        loader.Notify_ThingAdded(pawn);
        GenDrop.TryDropSpawn(pawn, loader.Position, loader.Map, ThingPlaceMode.Near, out _, null, cell => ElevatorUtil.UnloadValidator(cell, loader.Map, pawn));
        ElevatorUtil.LogWarn($"Deposited (loader): {loader.ThingID} cannot send {pawn.LabelShort} (pair down); put down next to the loader.");
        Messages.Message("DeepElevator_MsgLoaderPawnReturned".Translate(pawn.LabelShortCap, loader.LabelCap), pawn, MessageTypeDefOf.RejectInput, historical: false);
        return true;
    }

    /// <summary>Hauled items wait in the loader's container.</summary>
    private bool DepositItem(Thing item, bool canMergeWithExistingStacks)
    {
        // Described up front: a merge can consume the item.
        string description = ElevatorUtil.Describe(item);

        // Before the item is added anywhere, because a merge can consume it (the order is matched by what the item is).
        loader.Notify_ThingAdded(item);

        if (loader.Container.TryAdd(item, canMergeWithExistingStacks))
        {
            ElevatorUtil.Log($"Deposited (loader): {loader.ThingID} {description}");
            return true;
        }

        // Cannot happen with an unlimited container; kept so that nothing is ever lost.
        ElevatorUtil.LogWarn($"Deposited (loader): {loader.ThingID} could not hold {description}; putting it down nearby.");
        ElevatorUtil.TryDropNear(item, loader.Position, loader.Map);
        return true;
    }
}
