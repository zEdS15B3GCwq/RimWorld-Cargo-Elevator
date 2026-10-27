using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace DeepElevator;

/// <summary>
/// The output half of the elevator: picks the entry to send, plans where it can go at the partner
/// (adjacent accepting storage first, then adjacent floor cells) and moves one piece per send.
/// Nothing leaves the buffer before a plan exists, so a held entry never changes position.
/// </summary>
public partial class Building_Elevator
{
    /// <summary>One distinct storage touching the receiver, with the output cells through which it can be reached.</summary>
    private class DeliveryTarget
    {
        public ISlotGroupParent Storage;
        public readonly List<IntVec3> Cells = new List<IntVec3>();
        public StoragePriority Priority;
        public bool Accepts;
        public int Room; // how much of the piece fits through the output cells; only computed when accepting

        public string Label => Storage.SlotYielderLabel();
    }

    // Hold state of the head of the queue: why nothing was sent on the last attempt. Null key = not holding.
    private string holdReasonKey;
    private Thing heldThing;

    // Change detection for hold and target logs, so a 1-second retry does not spam the log.
    private ThingDef lastLoggedHoldDef;
    private string lastLoggedHoldKey;
    private string lastLoggedTargets;
    private readonly HashSet<int> warnedUndeliverableIds = new HashSet<int>();

    // ---------------------------------------------------------------- sending

    /// <summary>
    /// Tries to send one piece. In FIFO mode only the first entry is considered; otherwise the first
    /// entry that can be delivered is sent. Schedules the next attempt either way.
    /// </summary>
    private void TrySend()
    {
        int now = Find.TickManager.TicksGame;
        ThingOwner buffer = Buffer;

        if (!IsValidPartner(out _))
        {
            SetHold(buffer[0], "DeepElevator_HoldNotConnected", "not connected");
            nextSendTick = now + PeriodicInterval; // no point retrying faster until relinked
            return;
        }

        int entriesToConsider = fifoMode ? 1 : buffer.Count;
        Thing firstHeldEntry = null;
        string firstHoldKey = null;
        string firstHoldDetail = null;
        bool sent = false;

        for (int entryIndex = 0; entryIndex < entriesToConsider; entryIndex++)
        {
            Thing entry = buffer[entryIndex];
            if (TryDeliverEntry(entry, entryIndex, out string holdKey, out string holdDetail))
            {
                sent = true;
                break; // one successful send per call; the buffer has changed
            }

            if (firstHeldEntry == null)
            {
                firstHeldEntry = entry;
                firstHoldKey = holdKey;
                firstHoldDetail = holdDetail;
            }
        }

        if (sent)
        {
            ClearHold();
            nextSendTick = now + Settings.ticksPerStack;
        }
        else
        {
            SetHold(firstHeldEntry, firstHoldKey, firstHoldDetail);
            nextSendTick = now + Settings.HeldRetryTicks;
        }
    }

    /// <summary>
    /// Plans and performs the delivery of one piece (at most one full stack) of <paramref name="entry"/>.
    /// Case A: accepting storage with room. Case B: accepting storage but all full (hold).
    /// Case C: no accepting storage, so drop on adjacent floor cells if allowed.
    /// </summary>
    private bool TryDeliverEntry(Thing entry, int entryIndex, out string holdKey, out string holdDetail)
    {
        if (entry is Pawn)
        {
            // Pawns are not sent yet; nothing puts them in the buffer in this step.
            if (warnedUndeliverableIds.Add(entry.thingIDNumber))
            {
                ElevatorUtil.LogWarn($"{LogId}: {entry.LabelShort} in the buffer cannot be delivered (pawn sending is not implemented).");
            }
            holdKey = "DeepElevator_HoldUndeliverable";
            holdDetail = "pawn entry cannot be delivered";
            return false;
        }

        int pieceCount = Math.Min(entry.stackCount, entry.def.stackLimit);
        List<IntVec3> outputCells = ElevatorUtil.OutputCells(partner);
        List<DeliveryTarget> targets = BuildDeliveryTargets(entry, pieceCount, outputCells);
        LogTargetsIfChanged(entry, targets);

        List<DeliveryTarget> acceptingTargets = targets.Where(target => target.Accepts).ToList();
        if (acceptingTargets.Count > 0)
        {
            // Case A or B.
            if (acceptingTargets.Any(target => target.Room > 0) && DeliverToStorage(entry, entryIndex, pieceCount, acceptingTargets) > 0)
            {
                holdKey = null;
                holdDetail = null;
                return true;
            }

            holdKey = "DeepElevator_HoldTargetsFull";
            holdDetail = $"all {acceptingTargets.Count} accepting storages full";
            return false;
        }

        // Case C.
        if (!allowDrop)
        {
            holdKey = "DeepElevator_HoldDropDisabled";
            holdDetail = "no storage accepts it and dropping on the floor is off";
            return false;
        }

        if (DropOnFloor(entry, entryIndex, pieceCount, outputCells) > 0)
        {
            holdKey = null;
            holdDetail = null;
            return true;
        }

        holdKey = "DeepElevator_HoldNoDropCell";
        holdDetail = "no drop cell";
        return false;
    }

    // ---------------------------------------------------------------- planning

    /// <summary>
    /// The distinct storages among the receiver's output cells, best priority first (ties keep first-seen
    /// cell order). Each one knows whether it accepts the entry and how much of the piece fits through its output cells.
    /// </summary>
    private List<DeliveryTarget> BuildDeliveryTargets(Thing entry, int pieceCount, List<IntVec3> outputCells)
    {
        Map receiverMap = partnerMap;
        var targets = new List<DeliveryTarget>();

        foreach (IntVec3 cell in outputCells)
        {
            ISlotGroupParent storage = cell.GetSlotGroup(receiverMap)?.parent;
            if (storage == null || ReferenceEquals(storage, partner))
            {
                continue;
            }

            DeliveryTarget target = targets.Find(existing => ReferenceEquals(existing.Storage, storage));
            if (target == null)
            {
                target = new DeliveryTarget { Storage = storage, Priority = storage.GetStoreSettings().Priority };
                targets.Add(target);
            }
            target.Cells.Add(cell);
        }

        foreach (DeliveryTarget target in targets)
        {
            target.Accepts = target.Storage.Accepts(entry);
            if (!target.Accepts)
            {
                continue;
            }

            foreach (IntVec3 cell in target.Cells)
            {
                target.Room += ElevatorUtil.StorageCellRoom(cell, receiverMap, entry);
                if (target.Room >= pieceCount)
                {
                    break;
                }
            }
        }

        // OrderByDescending is stable, which keeps the first-seen order within one priority.
        return targets.OrderByDescending(target => target.Priority).ToList();
    }

    private string DescribeDeliveryTargets(Thing entry)
    {
        if (!IsValidPartner(out _))
        {
            return "not connected";
        }

        int pieceCount = Math.Min(entry.stackCount, entry.def.stackLimit);
        List<DeliveryTarget> targets = BuildDeliveryTargets(entry, pieceCount, ElevatorUtil.OutputCells(partner));
        return DescribeTargets(targets);
    }

    private static string DescribeTargets(List<DeliveryTarget> targets)
    {
        IEnumerable<string> descriptions = targets.Select(target =>
            $"{target.Label}({target.Priority}, accepts={(target.Accepts ? "yes" : "no")}, room={target.Room})");
        return "[" + string.Join(", ", descriptions) + "]";
    }

    private void LogTargetsIfChanged(Thing entry, List<DeliveryTarget> targets)
    {
        string targetsText = $"{LogId} for {entry.LabelCapNoCount}: {DescribeTargets(targets)}";
        if (targetsText != lastLoggedTargets)
        {
            lastLoggedTargets = targetsText;
            ElevatorUtil.Log("Targets: " + targetsText);
        }
    }

    // ---------------------------------------------------------------- delivery

    /// <summary>Case A: fills accepting storages in priority order, cell by cell. Returns how much was placed.</summary>
    private int DeliverToStorage(Thing entry, int entryIndex, int pieceCount, List<DeliveryTarget> acceptingTargets)
    {
        Map receiverMap = partnerMap;
        int remaining = pieceCount;

        foreach (DeliveryTarget target in acceptingTargets)
        {
            foreach (IntVec3 cell in target.Cells)
            {
                if (remaining <= 0)
                {
                    return pieceCount;
                }

                int amountForCell = Math.Min(remaining, ElevatorUtil.StorageCellRoom(cell, receiverMap, entry));
                if (amountForCell <= 0)
                {
                    continue;
                }

                int placed = PlaceFromEntry(entry, entryIndex, amountForCell, cell, " into " + target.Label, "Delivered");
                remaining -= placed;
                if (placed < amountForCell)
                {
                    return pieceCount - remaining; // defensive failure; the rest was returned to the buffer
                }
            }
        }

        return pieceCount - remaining;
    }

    /// <summary>Case C: puts the piece down on plain, empty (or same-kind partial) output cells. Returns how much was placed.</summary>
    private int DropOnFloor(Thing entry, int entryIndex, int pieceCount, List<IntVec3> outputCells)
    {
        Map receiverMap = partnerMap;
        int remaining = pieceCount;

        foreach (IntVec3 cell in outputCells)
        {
            if (remaining <= 0)
            {
                break;
            }

            int amountForCell = Math.Min(remaining, ElevatorUtil.DeliveryDropRoom(cell, receiverMap, entry));
            if (amountForCell <= 0)
            {
                continue;
            }

            int placed = PlaceFromEntry(entry, entryIndex, amountForCell, cell, "", "Dropped");
            remaining -= placed;
            if (placed < amountForCell)
            {
                break;
            }
        }

        return pieceCount - remaining;
    }

    /// <summary>
    /// Takes <paramref name="amount"/> out of the entry and places it on a cell of the partner's map.
    /// Direct placement only: it uses exactly that cell and never destroys anything.
    /// Returns how much was really placed; a failure (not expected, the room was computed in the same tick)
    /// puts the remainder back into the buffer.
    /// </summary>
    private int PlaceFromEntry(Thing entry, int entryIndex, int amount, IntVec3 cell, string destinationText, string logVerb)
    {
        string entryLabel = entry.LabelCapNoCount;
        bool wholeEntryTaken = amount >= entry.stackCount;

        Thing portion = Buffer.Take(entry, amount);
        int portionCount = portion.stackCount;
        portion.SetForbidden(false, warnOnFail: false);
        GenPlace.TryPlaceThing(portion, cell, partnerMap, ThingPlaceMode.Direct, out _);

        // Fully placed portions are spawned (or merged away and destroyed); anything else is a remainder.
        int remainder = portion.Destroyed || portion.Spawned ? 0 : portion.stackCount;
        int placed = portionCount - remainder;

        if (remainder > 0)
        {
            ElevatorUtil.LogWarn($"Delivery: {LogId} could only place {placed} of {portionCount} {entryLabel} at {cell} on {ElevatorUtil.MapDesc(partnerMap)}; returning the rest to the buffer.");
            ReturnPortionToBuffer(portion, entry, wholeEntryTaken, entryIndex);
        }

        if (placed > 0)
        {
            int remainingInEntry = Buffer.Contains(entry) ? entry.stackCount : 0;
            ElevatorUtil.Log($"{logVerb}: {LogId} → {partner.LogId}: {entryLabel} x{placed}{destinationText} at {cell}; remaining in entry {remainingInEntry}");
        }
        return placed;
    }

    // ---------------------------------------------------------------- hold state

    /// <summary>Remembers why the queue is stuck (shown in the inspect string); logs only when the reason changes.</summary>
    private void SetHold(Thing entry, string reasonKey, string logDetail)
    {
        holdReasonKey = reasonKey;
        heldThing = entry;

        if (entry.def != lastLoggedHoldDef || reasonKey != lastLoggedHoldKey)
        {
            lastLoggedHoldDef = entry.def;
            lastLoggedHoldKey = reasonKey;
            ElevatorUtil.Log($"Held: {LogId} {entry.LabelCapNoCount}: {logDetail}");
        }
    }

    private void ClearHold()
    {
        holdReasonKey = null;
        heldThing = null;
        lastLoggedHoldDef = null;
        lastLoggedHoldKey = null;
    }
}
