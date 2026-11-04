using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepElevator;

/// <summary>
/// The intake and buffer half of the elevator: what it accepts, how items move from its cell into the
/// buffer (a plain container the elevator owns), the player-facing buffer settings and the unload commands.
/// </summary>
public partial class Building_Elevator
{
    // ---------------------------------------------------------------- settings and state

    // Fallback for defs without the mod extension, so the elevator always has tunables.
    private static readonly ElevatorSettingsExtension FallbackSettings = new ElevatorSettingsExtension();

    // Reused scratch list for AbsorbIntake (the game is single threaded).
    private static readonly List<Thing> itemsOnIntakeCell = new List<Thing>();

    private ElevatorSettingsExtension cachedSettings;

    // The buffer: one plain container owned by the elevator. Saved under the name the old transporter comp
    // used ("innerContainer"), so buffers of older saves load into it. Created in the constructor, never null.
    private ThingOwner<Thing> buffer;

    // Saved, per-elevator player settings; initialised from the def's extension in PostMake.
    private int maxSlots = 4;
    private bool fifoMode = true;     // only governs which entry is sent (Send.cs)
    private bool mergeStacks = true;  // only governs how arriving items are added to the buffer
    private bool allowDrop = true;    // sender's setting: drop on the floor at the partner when no storage accepts
    private bool allowInsert = true;  // pawns may haul items here; conveyors and other mods are not affected

    // Runtime only.
    private bool absorbPending;       // an item arrived on the intake cell; absorb it on the next tick
    private int nextSendTick;

    // Change detection for log messages, so repeated identical outcomes are logged once.
    private ThingDef lastRefusedDef;
    private string lastRefusalReason;
    private int lastIgnoredThingId = -1;

    public ElevatorSettingsExtension Settings => cachedSettings ??= def.GetModExtension<ElevatorSettingsExtension>() ?? FallbackSettings;

    public Building_Elevator()
    {
        buffer = new ThingOwner<Thing>(this);
    }

    /// <summary>The buffer. Never null.</summary>
    public ThingOwner<Thing> Buffer => buffer;

    public int BufferEntryCount => buffer.Count;

    public int MaxSlots => maxSlots;

    // ---------------------------------------------------------------- container (IThingHolder)

    // Being an IThingHolder is what makes the game tick the buffered items (rot, deterioration) every tick
    // and find them for searches and saving; nothing in this mod ticks the buffer itself.

    public ThingOwner GetDirectlyHeldThings() => buffer;

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
    }

    public ThingOwner SearchableContents => buffer;

    // Called on newly made elevators only; loaded ones get their values from ExposeData.
    public override void PostMake()
    {
        base.PostMake(); // creates the storage settings
        maxSlots = Settings.defaultSlots;
        fifoMode = Settings.defaultFifo;
        mergeStacks = Settings.defaultMerge;
        allowDrop = Settings.defaultAllowDrop;
        allowInsert = Settings.defaultAllowInsert;
    }

    // ---------------------------------------------------------------- slots

    /// <summary>Slots in use: every buffer entry takes one slot per (started) full stack.</summary>
    public int UsedSlots()
    {
        int usedSlots = 0;
        for (int index = 0; index < buffer.Count; index++)
        {
            Thing entry = buffer[index];
            int stackLimit = entry.def.stackLimit;
            usedSlots += (entry.stackCount + stackLimit - 1) / stackLimit;
        }
        return usedSlots;
    }

    /// <summary>True if merging is on and a same-kind buffer entry has a partly empty last slot.</summary>
    private bool CanMergeIntoBuffer(Thing incoming)
    {
        if (!mergeStacks)
        {
            return false;
        }

        for (int index = 0; index < buffer.Count; index++)
        {
            Thing entry = buffer[index];
            if (entry.stackCount % entry.def.stackLimit != 0 && entry.CanStackWith(incoming))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Index of the first entry the buffer would merge <paramref name="incoming"/> into, or -1.</summary>
    private int FindMergeTargetIndex(Thing incoming)
    {
        for (int index = 0; index < buffer.Count; index++)
        {
            if (buffer[index].CanStackWith(incoming))
            {
                return index;
            }
        }
        return -1;
    }

    // ---------------------------------------------------------------- accepting (hot path: no allocations)

    // The one refusal that is not logged: the game pre-filters on the storage filter anyway.
    private const string FilterRefusalReason = "not allowed by the filter";

    /// <summary>
    /// The single rule for "can this thing be put into the buffer right now": null = yes, otherwise the reason.
    /// No side effects, so it may be called freely from searches. <see cref="Accepts"/> (the game's job checks)
    /// and <see cref="AcceptsPawnDelivery"/> (the haul search, via the Harmony patch) both build on it, so the
    /// search can never be more permissive than the check that follows it.
    /// </summary>
    private string GetRefusalReason(Thing thing)
    {
        if (thing is Pawn) return "pawn";
        if (!IsConnected) return "not connected";
        if (!IsPowered) return "no power";
        if (!partner.IsPowered) return "partner unpowered";
        if (!GetStoreSettings().AllowedToAccept(thing)) return FilterRefusalReason;
        if (CanMergeIntoBuffer(thing)) return null;
        if (UsedSlots() < maxSlots) return null;
        return "buffer full";
    }

    /// <summary>
    /// Hides Building_Storage.Accepts, which is not virtual; the interface re-declaration on the class makes
    /// the game's interface calls reach this one. Used by the game's haul job (after the search), by conveyors
    /// and by our own deliveries, so it does not depend on <see cref="allowInsert"/>. Logs a refusal only when
    /// it differs from the previous one.
    /// </summary>
    public new bool Accepts(Thing thing)
    {
        string refusalReason = GetRefusalReason(thing);
        if (refusalReason == null)
        {
            return true;
        }

        if (refusalReason != FilterRefusalReason)
        {
            LogRefusal(thing, refusalReason);
        }
        return false;
    }

    /// <summary>
    /// Whether the haul search may pick this elevator as a destination for a pawn to carry <paramref name="thing"/> to:
    /// everything <see cref="Accepts"/> requires, plus the player's "allow insert" switch. Stricter than or equal to
    /// Accepts by construction, never looser. Does not log (called per cell per search).
    /// </summary>
    public bool AcceptsPawnDelivery(Thing thing)
    {
        return allowInsert && GetRefusalReason(thing) == null;

        // PROBE (disabled): logs every refusal of the haul search with its reason, unfiltered. Used to find out why
        // pawn deliveries were refused during steady transfer (results_stepD §15.5/§16). It floods the log (the search
        // runs per cell, per item, per pawn scan), so it is commented out; to re-enable, replace the line above with this.
        // string refusalReason = allowInsert ? GetRefusalReason(thing) : "allow insert is off";
        // if (refusalReason == null)
        // {
        //     return true;
        // }
        //
        // ElevatorUtil.Log($"Pawn delivery refused: {LogId} refuses {ElevatorUtil.Describe(thing)}: {refusalReason}");
        // return false;
    }

    private void LogRefusal(Thing thing, string reason)
    {
        if (thing.def != lastRefusedDef || reason != lastRefusalReason)
        {
            lastRefusedDef = thing.def;
            lastRefusalReason = reason;
            ElevatorUtil.Log($"Rejected: {LogId} refuses {thing.def.defName}: {reason}");
        }
    }

    // ---------------------------------------------------------------- intake

    // Called while the item is still being spawned onto our cell, so nothing may be despawned here.
    public override void Notify_ReceivedThing(Thing newItem)
    {
        base.Notify_ReceivedThing(newItem);
        ElevatorUtil.Log($"Received: {LogId} got {newItem.LabelCapNoCount}");
        absorbPending = true;
    }

    /// <summary>
    /// Moves items lying on the intake cell into the buffer. Items that are not allowed (or any item while
    /// disconnected) stay on the cell and are treated by the game as misplaced, so pawns haul them away.
    /// </summary>
    private void AbsorbIntake(string trigger)
    {
        if (!Spawned)
        {
            return;
        }

        itemsOnIntakeCell.Clear();
        foreach (IntVec3 intakeCell in AllSlotCellsList())
        {
            List<Thing> thingsOnCell = intakeCell.GetThingList(Map);
            for (int index = 0; index < thingsOnCell.Count; index++)
            {
                if (thingsOnCell[index].def.category == ThingCategory.Item)
                {
                    itemsOnIntakeCell.Add(thingsOnCell[index]);
                }
            }
        }

        foreach (Thing item in itemsOnIntakeCell)
        {
            if (!ShouldAbsorb(item, out string ignoreReason))
            {
                if (item.thingIDNumber != lastIgnoredThingId)
                {
                    lastIgnoredThingId = item.thingIDNumber;
                    ElevatorUtil.Log($"Ignored: {LogId} leaves {ElevatorUtil.Describe(item)} on the cell ({ignoreReason})");
                }
                continue;
            }

            // A full buffer still absorbs: an in-flight haul may slightly overfill it. Accepts keeps new hauls away.
            item.DeSpawn();
            AddToBuffer(item, trigger);
        }
        itemsOnIntakeCell.Clear();
    }

    private bool ShouldAbsorb(Thing item, out string ignoreReason)
    {
        if (!IsConnected)
        {
            ignoreReason = "not connected";
            return false;
        }
        if (!IsPowered)
        {
            ignoreReason = "no power";
            return false;
        }
        if (!partner.IsPowered)
        {
            ignoreReason = "partner unpowered";
            return false;
        }
        if (item is Pawn)
        {
            ignoreReason = "cannot be buffered";
            return false;
        }
        if (!GetStoreSettings().AllowedToAccept(item))
        {
            ignoreReason = "not allowed by the filter";
            return false;
        }

        ignoreReason = null;
        return true;
    }

    /// <summary>
    /// Adds a (despawned) thing to the buffer. Shared by every future source of input.
    /// With merging, ThingOwner joins the first stackable entry; otherwise the thing is always appended.
    /// </summary>
    public bool AddToBuffer(Thing thing, string trigger)
    {
        // Described up front: the thing may be destroyed by merging into an existing entry.
        string description = ElevatorUtil.Describe(thing);
        int mergeTargetIndex = mergeStacks ? FindMergeTargetIndex(thing) : -1;

        if (!buffer.TryAdd(thing, canMergeWithExistingStacks: mergeStacks))
        {
            ElevatorUtil.LogWarn($"AddToBuffer: {LogId} could not buffer {description}; putting it down nearby.");
            ElevatorUtil.TryDropNear(thing, Position, Map);
            return false;
        }

        bool merged = mergeTargetIndex >= 0;
        int entryIndex = merged ? mergeTargetIndex : buffer.Count - 1;
        ElevatorUtil.Log($"Absorbed ({trigger}): {LogId} {description} → entry #{entryIndex} ({(merged ? "merged" : "new")}); slots {UsedSlots()}/{maxSlots}");
        return true;
    }

    // ---------------------------------------------------------------- unloading

    /// <summary>
    /// Puts the whole buffer down around the elevator, like a vanilla transporter unload. Works while disconnected.
    /// Whatever finds no room stays in the buffer.
    /// </summary>
    private void UnloadBuffer()
    {
        if (!Spawned)
        {
            return;
        }

        int entriesBefore = buffer.Count;
        for (int index = buffer.Count - 1; index >= 0; index--)
        {
            Thing entry = buffer[index];
            buffer.TryDrop(entry, Position, Map, ThingPlaceMode.Near, out _, null, cell => ElevatorUtil.UnloadValidator(cell, Map, entry));
        }

        int entriesLeft = buffer.Count;
        ElevatorUtil.Log($"Unloaded: {LogId} {entriesBefore - entriesLeft} entr{(entriesBefore - entriesLeft == 1 ? "y" : "ies")} dropped, {entriesLeft} left");
        if (entriesLeft > 0)
        {
            ElevatorUtil.LogWarn($"Unload: {LogId} found no room for {entriesLeft} entr{(entriesLeft == 1 ? "y" : "ies")}.");
            Messages.Message("DeepElevator_UnloadIncomplete".Translate(LabelCap, entriesLeft), this, MessageTypeDefOf.RejectInput, historical: false);
        }
    }

    /// <summary>Takes <paramref name="count"/> of an entry out of the buffer and puts it down near the elevator (contents tab).</summary>
    public void DropFromBuffer(Thing entry, int count, string source)
    {
        if (!Spawned || !buffer.Contains(entry))
        {
            return;
        }

        int entryIndex = buffer.IndexOf(entry);
        bool wholeEntryTaken = count >= entry.stackCount;
        Thing portion = buffer.Take(entry, count);
        string description = ElevatorUtil.Describe(portion);

        if (ElevatorUtil.TryDropNear(portion, Position, Map))
        {
            ElevatorUtil.Log($"Dropped from buffer ({source}): {LogId} {description}");
            return;
        }

        ReturnPortionToBuffer(portion, entry, wholeEntryTaken, entryIndex);
        Messages.Message("DeepElevator_UnloadIncomplete".Translate(LabelCap, 1), this, MessageTypeDefOf.RejectInput, historical: false);
    }

    /// <summary>
    /// Puts back a portion that could not leave. If only part of an entry was taken, it is merged back into
    /// that entry (position unchanged). If the whole entry was taken it re-enters the buffer like any new
    /// item and may land at the end, which changes the send order (accepted and logged).
    /// </summary>
    private void ReturnPortionToBuffer(Thing portion, Thing originalEntry, bool wholeEntryTaken, int oldIndex)
    {
        if (portion == null || portion.Destroyed || portion.stackCount <= 0)
        {
            return;
        }

        string description = ElevatorUtil.Describe(portion);
        if (!wholeEntryTaken && buffer.Contains(originalEntry))
        {
            originalEntry.TryAbsorbStack(portion, respectStackLimit: false);
            return;
        }

        if (!buffer.TryAdd(portion, canMergeWithExistingStacks: mergeStacks))
        {
            ElevatorUtil.LogWarn($"ReturnPortionToBuffer: {LogId} could not return {description}; putting it down nearby.");
            ElevatorUtil.TryDropNear(portion, Position, Map);
            return;
        }

        ElevatorUtil.Log($"Order changed: {LogId} {description} returned to the buffer at #{buffer.Count - 1} (was #{oldIndex}) after a failed delivery");
    }

    // ---------------------------------------------------------------- gizmos and inspect

    /// <summary>Unload, the three mode toggles and the slot buttons.</summary>
    private IEnumerable<Gizmo> GetBufferGizmos()
    {
        var unloadCommand = new Command_Action
        {
            defaultLabel = "DeepElevator_Unload".Translate(),
            defaultDesc = "DeepElevator_UnloadDesc".Translate(),
            icon = ElevatorTextures.Unload,
            action = UnloadBuffer
        };
        if (BufferEntryCount == 0)
        {
            unloadCommand.Disable("DeepElevator_UnloadEmpty".Translate());
        }
        yield return unloadCommand;

        yield return CreateToggle("DeepElevator_ToggleFifo", "DeepElevator_ToggleFifoDesc", ElevatorTextures.ToggleFifo,
            () => fifoMode, () => fifoMode = !fifoMode, "fifo");
        yield return CreateToggle("DeepElevator_ToggleMerge", "DeepElevator_ToggleMergeDesc", ElevatorTextures.ToggleMerge,
            () => mergeStacks, () => mergeStacks = !mergeStacks, "merge");
        yield return CreateToggle("DeepElevator_ToggleDrop", "DeepElevator_ToggleDropDesc", ElevatorTextures.ToggleDrop,
            () => allowDrop, () => allowDrop = !allowDrop, "allowDrop");
        yield return CreateToggle("DeepElevator_ToggleInsert", "DeepElevator_ToggleInsertDesc", ElevatorTextures.ToggleInsert,
            () => allowInsert, () => allowInsert = !allowInsert, "allowInsert");

        yield return CreateSlotsCommand("DeepElevator_SlotsLess", TexButton.Minus, -1);
        yield return CreateSlotsCommand("DeepElevator_SlotsMore", TexButton.Plus, +1);
    }

    private Command_Toggle CreateToggle(string labelKey, string descKey, Texture2D icon,
        System.Func<bool> readValue, System.Action flipValue, string modeName)
    {
        return new Command_Toggle
        {
            defaultLabel = labelKey.Translate(),
            defaultDesc = descKey.Translate(),
            icon = icon,
            isActive = readValue,
            toggleAction = () =>
            {
                flipValue();
                ElevatorUtil.Log($"Mode: {LogId} {modeName}={readValue()}");
            }
        };
    }

    private Command_Action CreateSlotsCommand(string labelKey, Texture2D icon, int slotChange)
    {
        var slotsCommand = new Command_Action
        {
            defaultLabel = labelKey.Translate(),
            defaultDesc = "DeepElevator_SlotsDesc".Translate(maxSlots),
            icon = icon,
            action = () =>
            {
                int oldSlots = maxSlots;
                maxSlots = Mathf.Clamp(maxSlots + slotChange, Settings.minSlots, Settings.maxSlots);
                ElevatorUtil.Log($"Slots: {LogId} {oldSlots} → {maxSlots}");
            }
        };

        // A saved value above the def's maximum (older version or changed def) can still be reduced;
        // the clamp above then brings it straight down to the maximum.
        bool atLimit = slotChange < 0 ? maxSlots <= Settings.minSlots : maxSlots >= Settings.maxSlots;
        if (atLimit)
        {
            slotsCommand.Disable();
        }
        return slotsCommand;
    }

    private void AppendBufferInspectLines(StringBuilder text)
    {
        text.AppendLine("DeepElevator_BufferSlots".Translate(UsedSlots(), maxSlots));

        string pausedText = DescribePausedTransfer();
        if (pausedText != null)
        {
            text.AppendLine("DeepElevator_Paused".Translate(pausedText));
        }
    }

    /// <summary>
    /// The held entries with their reasons in square brackets (round ones would look like quality / hit points).
    /// The held-items line and the contents tab already list the buffer, so short forms are used where possible:
    /// "all items [no power]" when one reason applies to every held entry; "Berries x25 [no storage]; all others [FIFO on]"
    /// when FIFO holds the queue behind its head; otherwise the entries grouped by reason, e.g.
    /// "Corn x50, Rice x20 [storage full]; Steel x75 [no storage]". Null when nothing is held.
    /// Entries that left the buffer since the last attempt are skipped.
    /// </summary>
    private string DescribePausedTransfer()
    {
        // Not connected wins over no power (either end). Read live rather than from heldEntries: TrySend stops
        // before evaluating entries in this state, and the live check is never stale after the state changes.
        string notOperationalReasonKey = !IsConnected ? "DeepElevator_PausedNotConnected"
            : !IsOperational ? "DeepElevator_PausedNoPower"
            : null;
        if (notOperationalReasonKey != null)
        {
            return BufferEntryCount == 0 ? null : PausedGroup("DeepElevator_PausedAllItems".Translate(), notOperationalReasonKey.Translate());
        }

        List<KeyValuePair<Thing, HoldReason>> stillHeld = heldEntries.Where(held => buffer.Contains(held.Key)).ToList();
        if (stillHeld.Count == 0)
        {
            return null;
        }

        // FIFO: only the head was evaluated; everything behind it waits for the head.
        if (stillHeld.Count > 1 && stillHeld.Skip(1).All(held => held.Value == HoldReason.FifoWaiting))
        {
            return PausedGroup(stillHeld[0].Key.LabelCap, PausedReasonText(stillHeld[0].Value)) + "; "
                + PausedGroup("DeepElevator_PausedAllOthers".Translate(), PausedReasonText(HoldReason.FifoWaiting));
        }

        // FIFO off and nothing could be sent, for the same reason everywhere.
        if (stillHeld.Count > 1 && stillHeld.All(held => held.Value == stillHeld[0].Value))
        {
            return PausedGroup("DeepElevator_PausedAllItems".Translate(), PausedReasonText(stillHeld[0].Value));
        }

        var groups = new List<string>();
        foreach (HoldReason reason in new[] { HoldReason.StorageFull, HoldReason.NoStorage, HoldReason.FifoWaiting })
        {
            string labelList = string.Join(", ", stillHeld.Where(held => held.Value == reason).Select(held => held.Key.LabelCap));
            if (labelList.Length > 0)
            {
                groups.Add(PausedGroup(labelList, PausedReasonText(reason)));
            }
        }
        return groups.Count > 0 ? string.Join("; ", groups) : null;
    }

    private static string PausedReasonText(HoldReason reason) => ("DeepElevator_Paused" + reason).Translate();

    private static string PausedGroup(string labels, string reasonText) => $"{labels} [{reasonText}]";

    /// <summary>DEV: dumps the buffer, the modes, the send timer and what the head entry would be delivered to.</summary>
    private void LogBufferState()
    {
        var report = new StringBuilder();
        report.Append($"Buffer: {LogId} slots {UsedSlots()}/{maxSlots} fifo={fifoMode} merge={mergeStacks} allowDrop={allowDrop} allowInsert={allowInsert} ");
        report.Append($"nextSendTick={nextSendTick} (now {Find.TickManager.TicksGame}) hold=[{string.Join(", ", heldEntries.Select(held => held.Key.LabelCapNoCount + ":" + held.Value))}]");
        for (int index = 0; index < buffer.Count; index++)
        {
            Thing entry = buffer[index];
            int stackLimit = entry.def.stackLimit;
            report.Append($"\n    #{index} {ElevatorUtil.Describe(entry)} ({(entry.stackCount + stackLimit - 1) / stackLimit} slot(s))");
        }
        if (buffer.Count > 0)
        {
            report.Append($"\n    head targets: {DescribeDeliveryTargets(buffer[0])}");
        }
        ElevatorUtil.Log(report.ToString());
    }
}
