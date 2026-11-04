using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace DeepElevator;

/// <summary>
/// An elevator that pairs with exactly one elevator on another level of the same level group.
/// Step B: link, lifecycle, naming. Step C: it is also a shelf-like storage building whose cell is the
/// intake; items are absorbed into one plain container (the buffer) and streamed to the partner. Step E:
/// it also pulls finished items out of adjacent transport loaders (manifest loading lives on the loader).
/// The class is split over several files: this one (link, lifecycle, UI), Building_Elevator.Buffer.cs
/// (intake and buffer), Building_Elevator.Send.cs (delivery at the partner), Building_Elevator.Power.cs
/// (power and status) and Building_Elevator.Loaders.cs (linked transport loaders).
/// </summary>
// ISlotGroupParent and IHaulDestination are listed again so that interface calls bind to our own Accepts.
public partial class Building_Elevator : Building_Storage, IRenameable, ISlotGroupParent, IHaulDestination, IStorageGroupMember, IThingHolder, ISearchableContents
{
    /// <summary>Interval for periodic work; matches the Rare ticker so switching tickerType is XML-only.</summary>
    public const int PeriodicInterval = GenTicks.TickRareInterval;

    // The linked elevator on another level, and the map it lives on. Both null when unconnected.
    private Building_Elevator partner;
    private Map partnerMap;

    // Player-given name; null while the elevator uses its default label.
    private string customName;

    public Building_Elevator Partner => partner;

    public bool IsConnected => partner != null;

    public bool HasCustomName => customName != null;

    /// <summary>Identifies this elevator in log messages: its thing id, plus the custom name if it has one.</summary>
    public string LogId => customName != null ? $"{ThingID} \"{customName}\"" : ThingID;

    // ---------------------------------------------------------------- naming (IRenameable)

    public string RenamableLabel
    {
        get => customName ?? BaseLabel;
        set => customName = value.NullOrEmpty() || value == BaseLabel ? null : value;
    }

    public string BaseLabel => def.label.CapitalizeFirst();

    public string InspectLabel => LabelCap;

    // ---------------------------------------------------------------- storage groups (disabled)

    // Building_Storage implements IStorageGroupMember explicitly. Re-implemented here so the inspect pane
    // shows the normal IRenameable rename button instead of the "create storage group" one; elevators
    // never join shared storage groups. Everything else delegates to the same values as the base.
    StorageGroup IStorageGroupMember.Group
    {
        get => storageGroup;
        set => storageGroup = value;
    }

    Map IStorageGroupMember.Map => MapHeld;
    StorageSettings IStorageGroupMember.StoreSettings => GetStoreSettings();
    StorageSettings IStorageGroupMember.ParentStoreSettings => GetParentStoreSettings();
    StorageSettings IStorageGroupMember.ThingStoreSettings => settings;
    string IStorageGroupMember.StorageGroupTag => def.building.storageGroupTag;
    bool IStorageGroupMember.DrawConnectionOverlay => false;
    bool IStorageGroupMember.DrawStorageTab => true;
    bool IStorageGroupMember.ShowRenameButton => false;

    // Makes the name show in the selection title and in vanilla messages.
    public override string Label => customName ?? base.Label;

    // ---------------------------------------------------------------- saving

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref partner, "partner");
        Scribe_References.Look(ref partnerMap, "partnerMap");
        Scribe_Values.Look(ref customName, "customName");
        Scribe_Values.Look(ref maxSlots, "maxSlots", Settings.defaultSlots);
        Scribe_Values.Look(ref fifoMode, "fifoMode", Settings.defaultFifo);
        Scribe_Values.Look(ref mergeStacks, "mergeStacks", Settings.defaultMerge);
        Scribe_Values.Look(ref allowDrop, "allowDrop", Settings.defaultAllowDrop);
        Scribe_Values.Look(ref allowInsert, "allowInsert", Settings.defaultAllowInsert);
        Scribe_Deep.Look(ref buffer, "innerContainer", this);

        // A save without that node (an elevator from before the buffer existed) leaves the field null.
        if (Scribe.mode == LoadSaveMode.PostLoadInit && buffer == null)
        {
            buffer = new ThingOwner<Thing>(this);
        }

        if (Scribe.mode == LoadSaveMode.PostLoadInit && (partner == null) != (partnerMap == null))
        {
            ElevatorUtil.LogWarn($"{LogId}: inconsistent link after load (partner={partner?.LogId ?? "null"}, partnerMap={(partnerMap == null ? "null" : "set")}); clearing.");
            partner = null;
            partnerMap = null;
        }
    }

    // ---------------------------------------------------------------- lifecycle

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        CachePower();
        ElevatorUtil.Log($"SpawnSetup: {LogId} on {ElevatorUtil.MapDesc(map)} respawningAfterLoad={respawningAfterLoad} partner={partner?.LogId ?? "none"}");

        if (respawningAfterLoad)
        {
            // Only the reference itself is checked while loading, never the partner's spawned state:
            // the partner may live on a map that has not spawned yet.
            VerifyPartnerLinksBack();
        }
        else
        {
            if (partner == null)
            {
                TryAutoPair();
            }
            LinkWaitingLoaders();
        }
    }

    /// <summary>
    /// A link must exist on both sides. All data and references are resolved before any map spawns,
    /// so the partner's saved link can be read here even if the partner itself is not spawned yet.
    /// A one-sided link (damaged save, mod removed mid-game) is dropped on this end.
    /// </summary>
    private void VerifyPartnerLinksBack()
    {
        if (partner == null || partner.partner == this)
        {
            return;
        }

        ElevatorUtil.LogWarn($"{LogId}: partner {partner.LogId} does not link back (it links to {partner.partner?.LogId ?? "none"}); unlinking.");
        Unlink("load check: partner does not link back");
    }

    /// <summary>Links automatically only when exactly one unambiguous candidate exists.</summary>
    private void TryAutoPair()
    {
        List<Building_Elevator> candidates = ElevatorUtil.UnconnectedCandidates(this);
        string candidateList = string.Join(", ", candidates.Select(candidate => $"{candidate.LogId} on {ElevatorUtil.MapDesc(candidate.Map)}"));
        ElevatorUtil.Log($"TryAutoPair: {LogId} found {candidates.Count} candidate(s){(candidates.Count > 0 ? ": " + candidateList : "")}");
        if (candidates.Count == 1)
        {
            Link(candidates[0]);
        }
    }

    public void Link(Building_Elevator other)
    {
        if (other == null || other == this || partner != null || other.partner != null)
        {
            ElevatorUtil.LogWarn($"Link refused: {LogId} -> {other?.LogId ?? "null"} (self partner={partner?.LogId ?? "none"}, other partner={other?.partner?.LogId ?? "none"})");
            return;
        }

        partner = other;
        partnerMap = other.Map;
        other.partner = this;
        other.partnerMap = Map;

        ElevatorUtil.Log($"Linked: {LogId} ({ElevatorUtil.MapDesc(Map)}) <-> {other.LogId} ({ElevatorUtil.MapDesc(other.Map)})");
        Messages.Message("DeepElevator_MsgLinked".Translate(LevelLabels.ElevatorLabel(other)), this, MessageTypeDefOf.PositiveEvent, historical: false);
    }

    /// <summary>Idempotent. Clears both sides (the other side only if it still points at us).</summary>
    public void Unlink(string reason)
    {
        if (partner == null)
        {
            partnerMap = null;
            return;
        }

        Building_Elevator formerPartner = partner;
        Map formerPartnerMap = partnerMap;
        partner = null;
        partnerMap = null;

        if (formerPartner.partner == this)
        {
            formerPartner.partner = null;
            formerPartner.partnerMap = null;
        }

        ElevatorUtil.Log($"Unlinked ({reason}): {LogId} x {formerPartner.LogId} (partner map {ElevatorUtil.MapDesc(formerPartnerMap)})");

        // Tell the player about whichever end survived, if any.
        Building_Elevator survivor = Spawned ? this : (formerPartner.Spawned ? formerPartner : null);
        if (survivor != null)
        {
            Map lostLevelMap = survivor == this ? formerPartnerMap : Map;
            Messages.Message("DeepElevator_MsgUnlinked".Translate(LevelLabels.LevelLabel(lostLevelMap)), survivor, MessageTypeDefOf.NeutralEvent, historical: false);
        }
    }

    /// <summary>
    /// Shared handler for every callback that can end this elevator's life on its map.
    /// Called before the base implementation, because the base calls change state
    /// (DeSpawn clears the map, Notify_MyMapRemoved makes Map null).
    /// </summary>
    private void OnLifecycleEnd(string callbackName, string details)
    {
        ElevatorUtil.Log($"{callbackName}: {LogId} on {ElevatorUtil.MapDesc(Map)} spawned={Spawned} {details} partner={partner?.LogId ?? "none"}");
        Unlink(callbackName);
    }

    // Deconstructed or destroyed while the map stays.
    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        OnLifecycleEnd(nameof(DeSpawn), $"mode={mode}");
        Map mapBeforeDespawn = Map;
        base.DeSpawn(mode);
        ReleaseLoaders();

        // Dropped after the base call, so the items do not land on this elevator's own (now free) storage cells.
        // A replacement (e.g. minify or upgrade) keeps the thing alive, so then the buffer stays.
        if (mode != DestroyMode.WillReplace)
        {
            ElevatorUtil.Log($"Despawn drop: {LogId} drops {BufferEntryCount} buffer entries near {Position}");
            buffer.TryDropAll(Position, mapBeforeDespawn, ThingPlaceMode.Near);
        }
    }

    public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
    {
        OnLifecycleEnd(nameof(Destroy), $"mode={mode}");
        base.Destroy(mode);
    }

    // Probe only: shows whether Discard is reached after Destroy.
    public override void Discard(bool silentlyRemoveReferences = false)
    {
        OnLifecycleEnd(nameof(Discard), $"silently={silentlyRemoveReferences}");
        base.Discard(silentlyRemoveReferences);
    }

    // Map removal calls neither Destroy nor DeSpawn, only this.
    public override void Notify_MyMapRemoved()
    {
        OnLifecycleEnd(nameof(Notify_MyMapRemoved), "");
        linkedLoaders.Clear(); // the loaders go with the map and clean up themselves
        ElevatorUtil.Log($"Map removed: {LogId} buffer has {BufferEntryCount} entries (discarded with the map)");
        base.Notify_MyMapRemoved();
    }

    // ---------------------------------------------------------------- periodic work

    // tickerType Normal: called every tick; everything below exits early when idle.
    protected override void Tick()
    {
        base.Tick(); // ticks the comps; the game ticks the buffer contents (rot etc.) itself, as we are an IThingHolder

        // Loaders first: the intake absorb below runs from this same tick (the arrival event only sets
        // absorbPending), so a pull always comes before an absorb and loaders have precedence over cells.
        PullFromLoaders();

        if (absorbPending)
        {
            absorbPending = false;
            AbsorbIntake("event");
        }

        if (this.IsHashIntervalTick(PeriodicInterval))
        {
            DoPeriodicWork();
        }

        if (BufferEntryCount > 0 && Find.TickManager.TicksGame >= nextSendTick)
        {
            TrySend();
        }

        if (this.IsHashIntervalTick(PowerUpdateInterval))
        {
            UpdatePowerDraw();
            LogStatusIfChanged();
        }
    }

    /// <summary>
    /// Periodic logic, run every <see cref="PeriodicInterval"/> ticks: partner validation (a backstop,
    /// the lifecycle callbacks should already have caught every broken link) and the intake backstop.
    /// </summary>
    private void DoPeriodicWork()
    {
        if (partner != null && !IsValidPartner(out string invalidReason))
        {
            ElevatorUtil.LogWarn($"Validation: {LogId} partner {partner.LogId} invalid ({invalidReason}); unlinking.");
            Unlink("validation: " + invalidReason);
        }

        AbsorbIntake("backstop");
    }

    public bool IsValidPartner(out string invalidReason)
    {
        if (partner == null) { invalidReason = "no partner"; return false; }
        if (partnerMap == null) { invalidReason = "partnerMap null"; return false; }
        if (!Find.Maps.Contains(partnerMap)) { invalidReason = "partner map removed"; return false; }
        if (partner.Destroyed) { invalidReason = "partner destroyed"; return false; }
        if (!partner.Spawned) { invalidReason = "partner not spawned"; return false; }
        if (partner.Map != partnerMap) { invalidReason = "partner on different map"; return false; }
        if (partner.partner != this) { invalidReason = "partner links elsewhere (" + (partner.partner?.LogId ?? "none") + ")"; return false; }

        invalidReason = null;
        return true;
    }

    // ---------------------------------------------------------------- UI

    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (Gizmo gizmo in base.GetGizmos())
        {
            yield return gizmo;
        }

        foreach (Gizmo bufferGizmo in GetBufferGizmos())
        {
            yield return bufferGizmo;
        }

        // Like the "build linkable facility" buttons of beds and workbenches: a loader is not a facility to the game,
        // but it is built next to the elevator and links to it, so the elevator offers its build command too.
        // Null when the loader cannot be built (not unlocked, hidden).
        Designator_Build buildLoaderCommand = BuildCopyCommandUtility.FindAllowedDesignator(DeepElevatorDefOf.DeepElevator_TransportLoader);
        if (buildLoaderCommand != null)
        {
            yield return buildLoaderCommand;
        }

        if (partner != null)
        {
            // Jumps the camera to the linked elevator (switching maps if needed) and selects it.
            yield return new Command_Action
            {
                defaultLabel = "DeepElevator_GoToPartner".Translate(),
                defaultDesc = "DeepElevator_GoToPartnerDesc".Translate(),
                icon = ElevatorTextures.GoToPartner,
                action = () => CameraJumper.TryJumpAndSelect(partner)
            };
        }
        else
        {
            List<Building_Elevator> candidates = ElevatorUtil.UnconnectedCandidates(this);
            if (candidates.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DeepElevator_Connect".Translate(),
                    defaultDesc = "DeepElevator_ConnectDesc".Translate(),
                    icon = ElevatorTextures.Connect,
                    action = () => OpenConnectMenu(candidates)
                };
            }
        }

        // Vanilla convention for dev gizmos: dev mode and god mode both on.
        if (DebugSettings.ShowDevGizmos)
        {
            yield return new Command_Action
            {
                defaultLabel = "DEV: log link state",
                action = LogLinkState
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: log buffer state",
                action = LogBufferState
            };
        }
    }

    /// <summary>Lets the player choose which of several ambiguous candidates to link with.</summary>
    private void OpenConnectMenu(List<Building_Elevator> candidates)
    {
        List<FloatMenuOption> options = candidates
            .Select(candidate => new FloatMenuOption(LevelLabels.ElevatorLabel(candidate), () =>
            {
                // State may have changed while the menu was open, so check again.
                if (partner == null && candidate.Spawned && candidate.Partner == null)
                {
                    Link(candidate);
                }
            }))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void LogLinkState()
    {
        string validityText = partner == null ? "-" : (IsValidPartner(out string invalidReason) ? "yes" : invalidReason);
        ElevatorUtil.Log($"State: {LogId} on {ElevatorUtil.MapDesc(Map)} offset={ElevatorUtil.CenterOffset(Position, Map)} root=map#{ElevatorUtil.GroupRoot(Map)?.uniqueID} partner={partner?.LogId ?? "none"} partnerMap={ElevatorUtil.MapDesc(partnerMap)} valid={validityText} candidates={ElevatorUtil.UnconnectedCandidates(this).Count}");

        // One line per other level of the group: where the matching cell is and which elevator stands there.
        foreach (Map otherMap in ElevatorUtil.OtherLevelMaps(Map))
        {
            string matchingCellText = "out of bounds";
            string elevatorThereText = "none";
            if (ElevatorUtil.TryGetMatchingCell(Position, Map, otherMap, out IntVec3 matchingCell))
            {
                matchingCellText = matchingCell.ToString();
                elevatorThereText = ElevatorUtil.ElevatorAnchoredAt(matchingCell, otherMap, def)?.LogId ?? "none";
            }
            ElevatorUtil.Log($"    {ElevatorUtil.MapDesc(otherMap)} matchingCell={matchingCellText} elevatorThere={elevatorThereText}");
        }
    }

    public override string GetInspectString()
    {
        var text = new StringBuilder();
        string baseText = base.GetInspectString();
        if (!baseText.NullOrEmpty())
        {
            text.AppendLine(baseText);
        }

        // The linked loaders are part of what this elevator is connected to, so they close the status line.
        string statusText = DescribeStatus();
        string loadersText = DescribeLoaderDirections();
        if (partner != null)
        {
            string linkedLine = "DeepElevator_StatusLinked".Translate(statusText, LevelLabels.ElevatorLabel(partner)).Resolve();
            text.AppendLine(loadersText == null ? linkedLine : linkedLine + ", " + loadersText);
        }
        else
        {
            string unlinkedLine = "DeepElevator_StatusUnlinked".Translate(statusText).Resolve();
            text.AppendLine(loadersText == null ? unlinkedLine : unlinkedLine + ", " + loadersText);
            // Auto-pairing only runs on build, so show the hint for any candidate count above zero.
            int candidateCount = ElevatorUtil.UnconnectedCandidates(this).Count;
            if (candidateCount > 0)
            {
                text.AppendLine("DeepElevator_CandidatesAvailable".Translate(candidateCount));
            }
        }

        AppendBufferInspectLines(text);

        return text.ToString().TrimEndNewlines();
    }
}
