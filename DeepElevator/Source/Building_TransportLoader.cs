using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DeepElevator;

/// <summary>
/// The transport loader: a vanilla map portal (the same base MultiFloors' stairs use). The player picks what to
/// send in the vanilla portal dialog; the portal's order (<c>leftToLoad</c>) and the hauling and entering jobs
/// are all vanilla. The loader stands next to an elevator (its "partner") and treats the two kinds of cargo differently:
/// <list type="bullet">
/// <item>Items are hauled into the loader's own container (through <see cref="LoaderContainerProxy"/>), and the
/// elevator pulls them into its buffer (see Building_Elevator.Loaders.cs), so slots, send rate and storage
/// placement at the destination apply.</item>
/// <item>People and animals use the vanilla portal behaviour: walking colonists walk in, animals, prisoners and
/// downed pawns are carried in, and all of them appear next to the partner elevator on the other level directly.</item>
/// </list>
/// The loader needs no power and has no power-related state. The link to the elevator is saved here and only
/// here; the elevator keeps a runtime list.
/// </summary>
public class Building_TransportLoader : MapPortal, IThingHolder
{
    /// <summary>How long after the last pull the status line still counts the elevator as accepting (avoids flicker).</summary>
    private const int PullGraceTicks = 120;

    // The same levels as storage priority, offered in the priority menu in this order.
    private static readonly StoragePriority[] SelectablePriorities =
    {
        StoragePriority.Low, StoragePriority.Normal, StoragePriority.Preferred, StoragePriority.Important, StoragePriority.Critical
    };

    // ---- saved
    private Building_Elevator partner;
    private bool transferAllowed = true;   // "Transfer items to elevator"; default on, also for a loader without a link yet
    private StoragePriority priority = StoragePriority.Normal;

    // Automatic linking happens only until the loader has been linked once (by any means). After a loss the player
    // reconnects by hand, so a rebuilt or remaining elevator never takes over a loader unasked.
    private bool autoLinkAllowed = true;

    // The container and the holder that presents it to the game; created in the constructor, never null.
    private readonly LoaderContainerHolder containerHolder;

    // ---- runtime only
    private int lastPullTick = -99999;

    public Building_TransportLoader()
    {
        containerHolder = new LoaderContainerHolder(this);
    }

    public Building_Elevator Partner => partner;

    public bool TransferAllowed => transferAllowed;

    public StoragePriority Priority => priority;

    /// <summary>The loader's own container: hauled items wait here until the elevator pulls them. Never null.</summary>
    public ThingOwner<Thing> Container => containerHolder.Container;

    // ---------------------------------------------------------------- container (IThingHolder)

    // Re-implemented for GetChildHolders only, like the elevator's Accepts. The game ticks, counts and searches the
    // container through the child holder. GetDirectlyHeldThings must stay MapPortal's (the proxy): vanilla hauling
    // deposits through it, and that is what shrinks the order. Explicit, because MapPortal's own public
    // GetChildHolders is not virtual.
    void IThingHolder.GetChildHolders(List<IThingHolder> outChildren)
    {
        outChildren.Add(containerHolder);
    }

    // ---------------------------------------------------------------- portal behaviour

    protected override Texture2D EnterTex => ElevatorTextures.LoaderEnter;

    // Wording: the portal's order is a loading order here, nobody "enters" anything.
    public override string CancelEnterString => "DeepElevator_LoaderCancelOrder".Translate();

    // Header of the order list in the loading tab.
    public override string EnteringString => "DeepElevator_LoaderOrderHeader".Translate();

    /// <summary>The other end of the trip: the level of the partner elevator's partner, or null while the pair is not connected.</summary>
    public override Map GetOtherMap() => partner?.Partner?.Map;

    /// <summary>The partner elevator's partner; vanilla puts the arriving pawn on a standable cell within 5 cells of it.</summary>
    public override IntVec3 GetDestinationLocation()
    {
        Building_Elevator destinationElevator = partner?.Partner;
        return destinationElevator is { Spawned: true } ? destinationElevator.Position : IntVec3.Invalid;
    }

    /// <summary>
    /// Pawns can enter (walk in, or be carried in) while the loader is linked and both elevators have power.
    /// The "transfer items" switch only governs items, so it does not matter here.
    /// </summary>
    public override bool IsEnterable(out string reason)
    {
        if (!base.IsEnterable(out reason))
        {
            return false;
        }
        if (partner == null)
        {
            reason = "DeepElevator_LoaderNeedsLink".Translate();
            return false;
        }
        if (!partner.IsOperational)
        {
            reason = "DeepElevator_LoaderPairDown".Translate();
            return false;
        }
        return true;
    }

    /// <summary>
    /// A pawn has arrived on the other level. Never calls the base: it needs portal def properties and an exit
    /// that this portal does not have. Taking the pawn off the order is a no-op for a right-click entry.
    /// </summary>
    public override void OnEntered(Pawn pawn)
    {
        Notify_ThingAdded(pawn);
        ElevatorUtil.Log($"Entered (loader): {ThingID} sent {pawn.LabelShort} to {ElevatorUtil.MapDesc(GetOtherMap())} at {GetDestinationLocation()}");
    }

    // ---------------------------------------------------------------- saving

    public override void ExposeData()
    {
        base.ExposeData();
        containerHolder.ExposeData();
        Scribe_References.Look(ref partner, "partner");
        Scribe_Values.Look(ref transferAllowed, "transferAllowed", true);
        Scribe_Values.Look(ref priority, "priority", StoragePriority.Normal);
        Scribe_Values.Look(ref autoLinkAllowed, "autoLinkAllowed", true);
    }

    // ---------------------------------------------------------------- lifecycle

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);

        // Replaces the vanilla proxy the base just created, so deposits go through the loader's rules.
        containerProxy = new LoaderContainerProxy { portal = this, loader = this };

        if (respawningAfterLoad)
        {
            // The reference is resolved before anything spawns. Registering only touches a runtime list on the
            // elevator, so it does not matter whether the elevator has spawned yet.
            partner?.RegisterLoader(this);
            autoLinkAllowed &= partner == null; // a loader that is linked has been linked once
        }
        else
        {
            TryAutoLink();
        }
        ElevatorUtil.Log($"SpawnSetup: {ThingID} on {ElevatorUtil.MapDesc(map)} respawningAfterLoad={respawningAfterLoad} partner={partner?.LogId ?? "none"} transfer={transferAllowed} priority={priority}");
    }

    // Deconstructed or destroyed while the map stays. The base class leaves the lord and the order as they are,
    // so the load is cancelled here (the haulers stop), and the container is emptied before the building is gone.
    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        ReleasePartner("DeSpawn");
        if (Spawned)
        {
            if (LoadInProgress)
            {
                CancelLoad();
            }

            // A replacement (e.g. minify) keeps the thing alive, so then the container stays.
            if (mode != DestroyMode.WillReplace)
            {
                DropContainerContents("despawn");

                // Whatever found no spot off the elevator cells is put down plainly: nothing is ever deleted.
                Container.TryDropAll(Position, Map, ThingPlaceMode.Near);
            }
        }
        base.DeSpawn(mode);
    }

    // Map removal calls neither Destroy nor DeSpawn, only this. No relinking: the map is going away.
    public override void Notify_MyMapRemoved()
    {
        ReleasePartner(nameof(Notify_MyMapRemoved));
        base.Notify_MyMapRemoved();
    }

    /// <summary>Silent clean-up for a loader that leaves the map: drops the link without relinking or messages.</summary>
    private void ReleasePartner(string callbackName)
    {
        ElevatorUtil.Log($"{callbackName}: {ThingID} partner={partner?.LogId ?? "none"}");
        partner?.UnregisterLoader(this);
        partner = null;
    }

    // ---------------------------------------------------------------- periodic work

    protected override void Tick()
    {
        base.Tick(); // vanilla's "nobody can load anything" message; the game ticks the container contents (rot etc.) itself

        if (!this.IsHashIntervalTick(Building_Elevator.PeriodicInterval))
        {
            return;
        }

        PruneFinishedOrder();

        if (partner != null && !IsPartnerStillValid(out string invalidReason))
        {
            ElevatorUtil.LogWarn($"Validation: {ThingID} partner {partner.LogId} invalid ({invalidReason}).");
            Notify_PartnerLost();
        }
    }

    /// <summary>
    /// Removes order entries that have nothing left to send, and ends the order when none are left. Vanilla's
    /// "remove" button in the order tab only sets an entry's count to zero, so an emptied order would stay "in
    /// progress" (tab and cancel button remain) because the portal counts entries, not items. Called by the loading
    /// tab right after it is drawn, and periodically as a safety net.
    /// </summary>
    public void PruneFinishedOrder()
    {
        if (leftToLoad == null || leftToLoad.Count == 0)
        {
            return;
        }

        leftToLoad.RemoveAll(entry => entry.CountToTransfer <= 0 || !entry.HasAnyThing);
        if (leftToLoad.Count == 0 && Spawned)
        {
            CancelLoad(); // also ends the lord, so nobody waits for an order that no longer exists
            ElevatorUtil.Log($"Order ended: {ThingID} nothing left to send.");
        }
    }

    private bool IsPartnerStillValid(out string invalidReason)
    {
        if (partner.Destroyed) { invalidReason = "partner destroyed"; return false; }
        if (!partner.Spawned) { invalidReason = "partner not spawned"; return false; }
        if (partner.Map != Map) { invalidReason = "partner on another map"; return false; }
        if (!DirectionToward(partner).HasValue) { invalidReason = "partner no longer adjacent"; return false; }

        invalidReason = null;
        return true;
    }

    // ---------------------------------------------------------------- notifications from the elevator

    /// <summary>The elevator took (part of) an item out of the container: remember when, for the status line.</summary>
    public void Notify_Pulled(Thing pulledItem)
    {
        lastPullTick = Find.TickManager.TicksGame;
    }

    /// <summary>
    /// The elevator is gone, or the link became invalid. The order continues; the transfer switch turns off and
    /// stays off after relinking, so a new elevator never starts taking items the player did not expect to move.
    /// There is no automatic relinking: the player reconnects with "Connect to...".
    /// </summary>
    public void Notify_PartnerLost()
    {
        Building_Elevator formerPartner = partner;
        formerPartner?.UnregisterLoader(this);
        partner = null;
        transferAllowed = false;

        ElevatorUtil.Log($"Partner lost: {ThingID} lost {formerPartner?.LogId ?? "none"}; transfer switched off.");
        Messages.Message("DeepElevator_MsgLoaderUnlinked".Translate(), this, MessageTypeDefOf.NeutralEvent, historical: false);
    }

    // ---------------------------------------------------------------- linking

    /// <summary>
    /// Spawned elevators on the cells next to this loader (cardinal only; diagonals do not count), each listed
    /// once even if it touches several of those cells. An elevator that is being removed no longer counts.
    /// </summary>
    public List<Building_Elevator> Candidates()
    {
        var candidates = new List<Building_Elevator>();
        if (!Spawned)
        {
            return candidates;
        }

        foreach (IntVec3 adjacentCell in GenAdj.CellsAdjacentCardinal(this))
        {
            if (adjacentCell.InBounds(Map)
                && adjacentCell.GetFirstThing<Building_Elevator>(Map) is { Spawned: true, Destroyed: false } elevator
                && !candidates.Contains(elevator))
            {
                candidates.Add(elevator);
            }
        }
        return candidates;
    }

    /// <summary>
    /// Links automatically only when there is exactly one candidate, no partner yet, and the loader has never
    /// been linked before (<see cref="autoLinkAllowed"/>). Leaves the transfer switch alone.
    /// </summary>
    public void TryAutoLink()
    {
        if (partner != null || !autoLinkAllowed)
        {
            return;
        }

        List<Building_Elevator> candidates = Candidates();
        ElevatorUtil.Log($"TryAutoLink: {ThingID} found {candidates.Count} candidate(s)");
        if (candidates.Count == 1)
        {
            LinkTo(candidates[0]);
        }
    }

    private void LinkTo(Building_Elevator elevator)
    {
        partner = elevator;
        autoLinkAllowed = false; // from now on only the player relinks
        elevator.RegisterLoader(this);

        ElevatorUtil.Log($"Loader linked: {ThingID} -> {elevator.LogId}");
        Messages.Message("DeepElevator_MsgLoaderLinked".Translate(elevator.LabelCap), this, MessageTypeDefOf.PositiveEvent, historical: false);
    }

    /// <summary>
    /// The side of this loader on which <paramref name="elevator"/> stands, or null if it does not touch a
    /// cardinal neighbour cell. The direction is that of the neighbour cell, so it also works for a 2x2 elevator.
    /// </summary>
    public Rot4? DirectionToward(Building_Elevator elevator)
    {
        foreach (IntVec3 adjacentCell in GenAdj.CellsAdjacentCardinal(this))
        {
            if (adjacentCell.InBounds(Map) && adjacentCell.GetFirstThing<Building_Elevator>(Map) == elevator)
            {
                return Rot4.FromIntVec3(adjacentCell - Position);
            }
        }
        return null;
    }

    /// <summary>Translated "north", "east", "south" or "west" (own keys, so the wording can be changed).</summary>
    public static string DirectionText(Rot4 direction)
    {
        string directionKey = direction == Rot4.North ? "DeepElevator_DirectionNorth"
            : direction == Rot4.East ? "DeepElevator_DirectionEast"
            : direction == Rot4.South ? "DeepElevator_DirectionSouth"
            : "DeepElevator_DirectionWest";
        return directionKey.Translate();
    }

    // ---------------------------------------------------------------- unloading

    /// <summary>
    /// Puts every entry of the container down near the loader on cells that are not elevator cells (a plain "near"
    /// drop could land on the intake cell, which would absorb the item again). Whatever finds no room stays.
    /// Does not touch the order. Returns how many entries are left.
    /// </summary>
    private int DropContainerContents(string source)
    {
        int entriesBefore = Container.Count;
        for (int entryIndex = Container.Count - 1; entryIndex >= 0; entryIndex--)
        {
            Thing entry = Container[entryIndex];
            Container.TryDrop(entry, Position, Map, ThingPlaceMode.Near, out _, null, cell => ElevatorUtil.UnloadValidator(cell, Map, entry));
        }

        int entriesLeft = Container.Count;
        ElevatorUtil.Log($"Unloaded ({source}): {ThingID} {entriesBefore - entriesLeft} entr{(entriesBefore - entriesLeft == 1 ? "y" : "ies")} dropped, {entriesLeft} left");
        return entriesLeft;
    }

    /// <summary>Takes <paramref name="count"/> of an entry out of the container and puts it down near the loader (contents tab). Returns false if there was no room; the portion is then back in the container.</summary>
    public bool DropFromContainer(Thing entry, int count, string source)
    {
        if (!Spawned || !Container.Contains(entry))
        {
            return false;
        }

        int entryIndex = Container.IndexOf(entry);
        bool wholeEntryTaken = count >= entry.stackCount;
        Thing portion = Container.Take(entry, count);
        string description = ElevatorUtil.Describe(portion);

        if (ElevatorUtil.TryDropNear(portion, Position, Map))
        {
            ElevatorUtil.Log($"Dropped from loader ({source}): {ThingID} {description}");
            return true;
        }

        // No room: put the portion back where it came from.
        if (!wholeEntryTaken && Container.Contains(entry))
        {
            entry.TryAbsorbStack(portion, respectStackLimit: false);
        }
        else
        {
            Container.TryAdd(portion, canMergeWithExistingStacks: false);
        }
        ElevatorUtil.LogWarn($"Dropped from loader ({source}): {ThingID} found no room for {description}; it stays (was entry #{entryIndex}).");
        return false;
    }

    // ---------------------------------------------------------------- gizmos

    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (Gizmo gizmo in base.GetGizmos())
        {
            // The vanilla Enter command is disabled whenever IsEnterable is false (including an unpowered pair);
            // EnterTex is our own cached texture, so the reference match is reliable.
            if (gizmo is Command_Action enterCommand && enterCommand.icon == EnterTex)
            {
                ApplyEnterCommandRules(enterCommand);
            }
            yield return gizmo;
        }

        if (Container.Any)
        {
            yield return CreateUnloadCommand();
        }

        yield return CreateTransferToggle();
        yield return CreatePriorityCommand();

        if (partner == null)
        {
            List<Building_Elevator> candidates = Candidates();
            if (candidates.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DeepElevator_Connect".Translate(),
                    defaultDesc = "DeepElevator_LoaderConnectDesc".Translate(),
                    icon = ElevatorTextures.LoaderConnect,
                    action = () => OpenConnectMenu(candidates)
                };
            }
        }

        // Vanilla convention for dev gizmos: dev mode and god mode both on.
        if (DebugSettings.ShowDevGizmos)
        {
            yield return new Command_Action
            {
                defaultLabel = "DEV: log loader state",
                action = LogLoaderState
            };
        }
    }

    /// <summary>
    /// Wording of the command and when the dialog may be opened. The vanilla label says "enter", but the dialog is
    /// also how an order in progress is changed, so the command is called a menu. A new order needs a linked elevator that is connected to its partner on another
    /// level (otherwise there is nowhere to send anything). An unpowered pair is fine: the order waits for power.
    /// While an order is in progress the command stays available whatever the link says, so the order can still
    /// be changed or reduced. Vanilla's own rule (the portal must be enterable) is replaced by this one.
    /// </summary>
    private void ApplyEnterCommandRules(Command_Action enterCommand)
    {
        enterCommand.defaultLabel = "DeepElevator_LoaderOpenMenu".Translate();
        enterCommand.defaultDesc = "DeepElevator_LoaderOpenMenuDesc".Translate();

        string blockedReasonKey = null;
        if (!LoadInProgress)
        {
            if (partner == null)
            {
                blockedReasonKey = "DeepElevator_LoaderNeedsLink";
            }
            else if (!partner.IsConnected)
            {
                blockedReasonKey = "DeepElevator_LoaderElevatorNotConnected";
            }
        }

        enterCommand.Disabled = blockedReasonKey != null;
        enterCommand.disabledReason = blockedReasonKey?.Translate().Resolve();
    }

    /// <summary>
    /// "Unload all", with the vanilla texts: drops what waits in the container (e.g. after the order was
    /// cancelled, or transfer was switched off). The order is a separate matter: the vanilla cancel button handles it.
    /// </summary>
    private Command_Action CreateUnloadCommand()
    {
        return new Command_Action
        {
            defaultLabel = "CommandUnload".Translate(),
            defaultDesc = "CommandUnloadDesc".Translate(LabelShort),
            icon = ElevatorTextures.Unload,
            action = () =>
            {
                SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
                int entriesLeft = DropContainerContents("unload command");
                if (entriesLeft > 0)
                {
                    Messages.Message("DeepElevator_LoaderUnloadIncomplete".Translate(LabelCap, entriesLeft), this, MessageTypeDefOf.RejectInput, historical: false);
                }
            }
        };
    }

    private Command_Toggle CreateTransferToggle()
    {
        var transferToggle = new Command_Toggle
        {
            defaultLabel = "DeepElevator_ToggleTransfer".Translate(),
            defaultDesc = "DeepElevator_ToggleTransferDesc".Translate(),
            icon = ElevatorTextures.LoaderTransfer,
            isActive = () => transferAllowed,
            toggleAction = () =>
            {
                transferAllowed = !transferAllowed;
                ElevatorUtil.Log($"Mode: {ThingID} transfer={transferAllowed}");
            }
        };
        if (partner == null)
        {
            transferToggle.Disable("DeepElevator_LoaderNeedsLink".Translate());
        }
        return transferToggle;
    }

    /// <summary>Shows the current level in its label and opens a menu of the levels to choose from.</summary>
    private Command_Action CreatePriorityCommand()
    {
        return new Command_Action
        {
            defaultLabel = "DeepElevator_LoaderPriority".Translate(priority.Label()),
            defaultDesc = "DeepElevator_LoaderPriorityDesc".Translate(),
            icon = ElevatorTextures.LoaderPriorityIcon(priority),
            action = () =>
            {
                List<FloatMenuOption> options = SelectablePriorities
                    .Select(level => new FloatMenuOption(level.Label().CapitalizeFirst(), () => ChangePriority(level)))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
        };
    }

    private void ChangePriority(StoragePriority newPriority)
    {
        priority = newPriority;
        partner?.Notify_LoaderPriorityChanged();
        ElevatorUtil.Log($"Priority: {ThingID} {priority}");
    }

    /// <summary>
    /// Lets the player choose among several adjacent elevators. Elevators next to the loader share its level,
    /// so the level label would be the same for all; the menu shows each one's label and its direction instead.
    /// </summary>
    private void OpenConnectMenu(List<Building_Elevator> candidates)
    {
        List<FloatMenuOption> options = candidates
            .Select(candidate =>
            {
                Rot4? direction = DirectionToward(candidate);
                string optionLabel = direction.HasValue
                    ? "DeepElevator_LabelWithTag".Translate(candidate.LabelCap, DirectionText(direction.Value)).Resolve()
                    : candidate.LabelCap;
                return new FloatMenuOption(optionLabel, () =>
                {
                    // State may have changed while the menu was open, so check again.
                    if (partner == null && candidate.Spawned && Candidates().Contains(candidate))
                    {
                        LinkTo(candidate);
                    }
                });
            })
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>One full blink (orange, then normal) of the waiting look takes this long: about one second at normal speed.</summary>
    private const int WaitingBlinkPeriodTicks = 60;

    // The orange look, loaded on first use; the flag keeps a missing texture from being looked up every frame.
    private Graphic loadingGraphic;
    private bool loadingGraphicLoaded;

    /// <summary>
    /// MapMeshAndRealTime, like the elevator: the base sprite is already in the map mesh, so only the orange look
    /// is drawn here, over it. Runs every frame, so it only reads fields. The look follows what the loader is doing:
    /// <list type="bullet">
    /// <item>an order is in progress (items still to be sent): constant orange;</item>
    /// <item>no order, but items wait in the container for the elevator: orange and normal alternate;</item>
    /// <item>nothing to send and nothing waiting: normal.</item>
    /// </list>
    /// </summary>
    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        base.DrawAt(drawLoc, flip);

        if (ShowsOrangeLook() && LoadingGraphic != null)
        {
            // Half an altitude step: just above the base sprite and still below items lying on the cell.
            LoadingGraphic.Draw(drawLoc + Altitudes.AltIncVect * 0.5f, Rot4.North, this);
        }
    }

    private bool ShowsOrangeLook()
    {
        if (LoadInProgress)
        {
            return true;
        }
        if (Container.Any)
        {
            // Game ticks, so the blink stops while paused; the first half of each period is orange.
            return Find.TickManager.TicksGame % WaitingBlinkPeriodTicks < WaitingBlinkPeriodTicks / 2;
        }
        return false;
    }

    private Graphic LoadingGraphic
    {
        get
        {
            if (!loadingGraphicLoaded)
            {
                loadingGraphicLoaded = true;
                loadingGraphic = ElevatorUtil.LoadOverlayGraphic(def, def.GetModExtension<LoaderSettingsExtension>()?.loadingTexPath);
            }
            return loadingGraphic;
        }
    }

    // ---------------------------------------------------------------- inspect string

    public override string GetInspectString()
    {
        var text = new StringBuilder();
        string baseText = base.GetInspectString();
        if (!baseText.NullOrEmpty())
        {
            text.AppendLine(baseText);
        }

        // Needed when two elevators stand next to the loader: says which one this loader hands its items to.
        if (partner != null)
        {
            Rot4? direction = DirectionToward(partner);
            text.AppendLine("DeepElevator_LoaderLinkedTo".Translate(partner.LabelCap, direction.HasValue ? DirectionText(direction.Value) : "?"));
        }

        string statusText = DescribeStatus();
        if (statusText != null)
        {
            text.AppendLine("DeepElevator_StatusUnlinked".Translate(statusText));
        }
        return text.ToString().TrimEndNewlines();
    }

    /// <summary>
    /// The loader's own state, parts joined with "; ": the loading part, then the transfer part (first match
    /// wins), then the priority once linked. Elevator states such as a full buffer or no power are not described
    /// here, only whether the elevator can take what is waiting. Never empty: the transfer part always says something.
    /// </summary>
    private string DescribeStatus()
    {
        var statusParts = new List<string>();

        bool isLoading = LoadInProgress;
        if (isLoading)
        {
            statusParts.Add("DeepElevator_LoaderStatusLoading".Translate());
        }

        statusParts.Add(DescribeTransferPart(isLoading));

        if (partner != null)
        {
            statusParts.Add("DeepElevator_LoaderStatusPriority".Translate(priority.Label()));
        }
        return string.Join("; ", statusParts);
    }

    private string DescribeTransferPart(bool isLoading)
    {
        if (partner == null)
        {
            return "DeepElevator_LoaderStatusNotLinked".Translate();
        }
        if (!transferAllowed)
        {
            return "DeepElevator_LoaderStatusTransferOff".Translate();
        }

        // Nothing to transfer and nothing coming: idle, but ready.
        if (!isLoading && !Container.Any)
        {
            return "DeepElevator_LoaderStatusTransferOn".Translate();
        }

        // The grace period keeps the line steady: in steady transfer the buffer is full between sends, and the
        // elevator only has room right after each send, so without it the line would flicker.
        bool pulledRecently = Find.TickManager.TicksGame - lastPullTick <= PullGraceTicks;
        if (!pulledRecently && ElevatorCannotAcceptNow())
        {
            return "DeepElevator_LoaderStatusCannotAccept".Translate();
        }
        return "DeepElevator_LoaderStatusTransferring".Translate();
    }

    /// <summary>
    /// The elevator cannot take what is waiting: it is not operational, or the container holds an item
    /// the elevator has no room for. An empty container (items still being hauled in) is not a refusal.
    /// </summary>
    private bool ElevatorCannotAcceptNow()
    {
        if (!partner.IsOperational)
        {
            return true;
        }

        // Pawns never reach the container any more; the check stays as a safety net, like the elevator's pull.
        bool anythingToPull = false;
        ThingOwner container = Container;
        for (int entryIndex = 0; entryIndex < container.Count; entryIndex++)
        {
            if (!(container[entryIndex] is Pawn))
            {
                anythingToPull = true;
                break;
            }
        }
        return anythingToPull && !partner.CanPullFrom(this);
    }

    // ---------------------------------------------------------------- dev

    /// <summary>DEV: dumps the link, the settings, the order and the container.</summary>
    private void LogLoaderState()
    {
        var report = new StringBuilder();
        report.Append($"Loader: {ThingID} partner={partner?.LogId ?? "none"} transfer={transferAllowed} priority={priority} ");
        report.Append($"loadInProgress={LoadInProgress} orderEntries={leftToLoad?.Count ?? 0} notifiedCantLoadMore={notifiedCantLoadMore} ");
        report.Append($"lastPullTick={lastPullTick} (now {Find.TickManager.TicksGame}) candidates={Candidates().Count}");
        ThingOwner container = Container;
        for (int entryIndex = 0; entryIndex < container.Count; entryIndex++)
        {
            report.Append($"\n    #{entryIndex} {ElevatorUtil.Describe(container[entryIndex])}");
        }
        ElevatorUtil.Log(report.ToString());
    }
}
