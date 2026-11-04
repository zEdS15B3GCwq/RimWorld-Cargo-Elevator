using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepElevator;

/// <summary>
/// The elevator's side of the transport loaders: the list of loaders linked to it, and the "pull" that moves
/// finished items from a loader's container into the buffer. Think of a loader as a conveyor putting items
/// on the elevator: the pull ignores the elevator's filter and "allow insert", never overfills the buffer,
/// and runs before the intake cells are absorbed, so loaders have precedence over hauled items.
/// The loader owns the saved link; everything here is runtime-only and independent of the elevator-to-elevator
/// partner link (a loader may be linked to a disconnected elevator).
/// </summary>
public partial class Building_Elevator
{
    // Linked loaders, highest priority first; loaders of equal priority keep their link order
    // (OrderByDescending is a stable sort). Rebuilt only on register, unregister and priority change.
    private readonly List<Building_TransportLoader> linkedLoaders = new List<Building_TransportLoader>();

    public IReadOnlyList<Building_TransportLoader> LinkedLoaders => linkedLoaders;

    // ---------------------------------------------------------------- loader registry

    /// <summary>Called by the loader when it links to this elevator (and after loading a save). Idempotent.</summary>
    public void RegisterLoader(Building_TransportLoader loader)
    {
        if (linkedLoaders.Contains(loader))
        {
            return;
        }

        linkedLoaders.Add(loader);
        SortLoadersByPriority();
        ElevatorUtil.Log($"Loader registered: {LogId} <- {loader.ThingID}; {linkedLoaders.Count} loader(s)");
    }

    /// <summary>Called by the loader when it unlinks or leaves the map. Idempotent.</summary>
    public void UnregisterLoader(Building_TransportLoader loader)
    {
        if (linkedLoaders.Remove(loader))
        {
            ElevatorUtil.Log($"Loader unregistered: {LogId} x {loader.ThingID}; {linkedLoaders.Count} loader(s)");
        }
    }

    public void Notify_LoaderPriorityChanged()
    {
        SortLoadersByPriority();
    }

    private void SortLoadersByPriority()
    {
        List<Building_TransportLoader> sortedLoaders = linkedLoaders.OrderByDescending(loader => loader.Priority).ToList();
        linkedLoaders.Clear();
        linkedLoaders.AddRange(sortedLoaders);
    }

    // ---------------------------------------------------------------- loader directions (status line)

    // The 16 compass points, clockwise from north, as letters (each letter is translated on its own).
    // A 1x1 elevator only has the four cardinal ones; a 2x2 has two loader spots per side, which are the "in between" ones.
    private static readonly string[] CompassPointLetters =
    {
        "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
    };

    private const float DegreesPerCompassPoint = 360f / 16f;

    /// <summary>
    /// The linked loaders as the directions they stand in from this elevator, clockwise from north, e.g.
    /// "loaders: N, ESE". Null when no loader is linked. The direction comes from the offset between the centers:
    /// next to a 2x2 elevator a loader sits at 18 degrees off a side's middle, which rounds to the in-between point.
    /// </summary>
    private string DescribeLoaderDirections()
    {
        if (linkedLoaders.Count == 0)
        {
            return null;
        }

        Vector3 elevatorCenter = this.TrueCenter();
        List<string> directionTexts = linkedLoaders
            .Where(loader => loader.Spawned)
            .Select(loader => CompassPointIndex(loader.TrueCenter() - elevatorCenter))
            .OrderBy(compassPointIndex => compassPointIndex)
            .Select(compassPointIndex => CompassPointText(CompassPointLetters[compassPointIndex]))
            .ToList();
        return "DeepElevator_StatusLoaders".Translate(string.Join(", ", directionTexts)).Resolve();
    }

    /// <summary>Index into <see cref="CompassPointLetters"/> of the point nearest to the direction of <paramref name="offset"/> (x east, z north).</summary>
    private static int CompassPointIndex(Vector3 offset)
    {
        float degreesClockwiseFromNorth = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        int pointIndex = Mathf.RoundToInt(degreesClockwiseFromNorth / DegreesPerCompassPoint);
        return (pointIndex % CompassPointLetters.Length + CompassPointLetters.Length) % CompassPointLetters.Length;
    }

    /// <summary>Translates the letters of a compass point one by one (own keys, so a language can use its own letters).</summary>
    private static string CompassPointText(string letters)
    {
        var text = new System.Text.StringBuilder();
        foreach (char letter in letters)
        {
            text.Append($"DeepElevator_Compass{letter}".Translate());
        }
        return text.ToString();
    }

    // ---------------------------------------------------------------- loader lifecycle

    /// <summary>A new elevator offers itself to the loaders standing next to it that have no elevator yet.</summary>
    private void LinkWaitingLoaders()
    {
        foreach (IntVec3 adjacentCell in GenAdj.CellsAdjacentCardinal(this))
        {
            if (adjacentCell.InBounds(Map) && adjacentCell.GetFirstThing<Building_TransportLoader>(Map) is { Partner: null } waitingLoader)
            {
                waitingLoader.TryAutoLink();
            }
        }
    }

    /// <summary>
    /// This elevator is gone from its map (called after the base DeSpawn, so it no longer counts as a link
    /// candidate): every linked loader loses its partner (and is not relinked automatically).
    /// </summary>
    private void ReleaseLoaders()
    {
        // A copy, because every loader unregisters itself from the list while it is being notified.
        foreach (Building_TransportLoader loader in linkedLoaders.ToList())
        {
            loader.Notify_PartnerLost();
        }
        linkedLoaders.Clear();
    }

    // ---------------------------------------------------------------- pulling

    /// <summary>
    /// Items of this kind the buffer can still take without exceeding its slot limit. The order (fill the
    /// same-kind stacks first, then a new entry) is the one ThingOwner.TryAdd uses, so a pull of this many
    /// items never needs more slots than are free.
    /// </summary>
    private int RoomFor(Thing thing)
    {
        int freeSlots = Mathf.Max(0, maxSlots - UsedSlots());
        int room = freeSlots * thing.def.stackLimit;

        if (mergeStacks)
        {
            for (int index = 0; index < buffer.Count; index++)
            {
                Thing entry = buffer[index];
                if (entry.CanStackWith(thing))
                {
                    room += Mathf.Max(0, entry.def.stackLimit - entry.stackCount);
                }
            }
        }
        return room;
    }

    /// <summary>
    /// Moves items from the linked loaders into the buffer, highest priority loader first. Runs every tick
    /// but exits at once when nothing is linked. Pawns are skipped (items only in step E1). Anything that does
    /// not fit stays in the loader and is tried again next tick.
    /// </summary>
    private void PullFromLoaders()
    {
        if (linkedLoaders.Count == 0 || !IsOperational)
        {
            return;
        }

        foreach (Building_TransportLoader loader in linkedLoaders)
        {
            if (!loader.TransferAllowed)
            {
                continue;
            }

            ThingOwner loaderContainer = loader.Container;
            for (int entryIndex = 0; entryIndex < loaderContainer.Count;)
            {
                Thing loaderEntry = loaderContainer[entryIndex];
                int pullCount = loaderEntry is Pawn ? 0 : Mathf.Min(loaderEntry.stackCount, RoomFor(loaderEntry));
                if (pullCount <= 0)
                {
                    entryIndex++; // this kind does not fit, but a later entry of another kind may
                    continue;
                }

                bool takesWholeEntry = pullCount == loaderEntry.stackCount;
                Thing pulledPortion = loaderContainer.Take(loaderEntry, pullCount);
                loader.Notify_Pulled(pulledPortion);
                AddToBuffer(pulledPortion, "loader");

                // A whole entry leaves the list, so the next one has moved into this index.
                if (!takesWholeEntry)
                {
                    entryIndex++;
                }
            }
        }
    }

    /// <summary>
    /// True if this elevator could take something out of the loader right now: operational, and the loader
    /// holds a non-pawn item with room for at least one piece. Does not use the storage filter or
    /// <see cref="Accepts"/>: those apply the filter and answer yes to any merge, which could overfill.
    /// </summary>
    public bool CanPullFrom(Building_TransportLoader loader)
    {
        if (!IsOperational)
        {
            return false;
        }

        ThingOwner loaderContainer = loader.Container;
        for (int entryIndex = 0; entryIndex < loaderContainer.Count; entryIndex++)
        {
            Thing loaderEntry = loaderContainer[entryIndex];
            if (!(loaderEntry is Pawn) && RoomFor(loaderEntry) > 0)
            {
                return true;
            }
        }
        return false;
    }
}
