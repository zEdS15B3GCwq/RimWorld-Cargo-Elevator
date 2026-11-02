using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DeepElevator;

/// <summary>
/// The transport loader: in effect a vanilla transport pod that cannot launch. The vanilla transporter comp
/// provides the Load dialog, the loading jobs and the contents tab. The loader stands next to an elevator
/// (its "partner") and the elevator pulls the loaded items out of the container into its own buffer
/// (see Building_Elevator.Loaders.cs). The loader needs no power and has no power-related state.
/// The link to the elevator is saved here and only here; the elevator keeps a runtime list.
/// Items only (step E1); pawns and animals come in step E2.
/// </summary>
public class Building_TransportLoader : Building
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

    // ---- runtime only
    private CompTransporter transporter;
    private int lastPullTick = -99999;

    public Building_Elevator Partner => partner;

    public bool TransferAllowed => transferAllowed;

    public StoragePriority Priority => priority;

    /// <summary>The vanilla transporter's container; the elevator pulls from it.</summary>
    public ThingOwner Container => transporter.innerContainer;

    // ---------------------------------------------------------------- saving

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref partner, "partner");
        Scribe_Values.Look(ref transferAllowed, "transferAllowed", true);
        Scribe_Values.Look(ref priority, "priority", StoragePriority.Normal);
    }

    // ---------------------------------------------------------------- lifecycle

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        transporter = GetComp<CompTransporter>();
        if (transporter == null)
        {
            ElevatorUtil.LogWarn($"{ThingID}: the def has no CompTransporter; the loader cannot hold anything.");
        }

        if (respawningAfterLoad)
        {
            // The reference is resolved before anything spawns. Registering only touches a runtime list on the
            // elevator, so it does not matter whether the elevator has spawned yet.
            partner?.RegisterLoader(this);
        }
        else
        {
            TryAutoLink();
        }
        ElevatorUtil.Log($"SpawnSetup: {ThingID} on {ElevatorUtil.MapDesc(map)} respawningAfterLoad={respawningAfterLoad} partner={partner?.LogId ?? "none"} transfer={transferAllowed} priority={priority}");
    }

    // Deconstructed or destroyed while the map stays. The base call (the transporter comp) cancels the load and
    // drops the contents by itself; the link is released first.
    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        ReleasePartner("DeSpawn");
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
        base.Tick(); // the transporter comp ticks the contents (rot etc.)

        FinishLoadIfComplete();

        if (partner != null && this.IsHashIntervalTick(Building_Elevator.PeriodicInterval) && !IsPartnerStillValid(out string invalidReason))
        {
            ElevatorUtil.LogWarn($"Validation: {ThingID} partner {partner.LogId} invalid ({invalidReason}).");
            Notify_PartnerLost();
        }
    }

    /// <summary>
    /// Ends the load by itself once nothing is left to load and the container is empty (everything was pulled
    /// by the elevator). Same two steps as the end of a launch, so nothing is dropped and no message is shown.
    /// Runs regardless of link and transfer setting: an empty manifest, or a load finished by hand, ends here too.
    /// </summary>
    private void FinishLoadIfComplete()
    {
        if (transporter.LoadingInProgressOrReadyToLaunch && !transporter.AnythingLeftToLoad && !transporter.innerContainer.Any)
        {
            transporter.CancelLoad(Map);
            ElevatorUtil.Log($"Load finished: {ThingID} (nothing left to load, container empty); the loading state ended.");
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

    /// <summary>The elevator took (part of) an item out of the container: remember when and keep the mass cache right.</summary>
    public void Notify_Pulled(Thing pulledItem)
    {
        lastPullTick = Find.TickManager.TicksGame;
        transporter.Notify_ThingRemoved(pulledItem);
    }

    /// <summary>
    /// The elevator is gone, or the link became invalid. Loading continues; the transfer switch turns off and
    /// stays off after relinking, so a new elevator never starts taking items the player did not expect to move.
    /// </summary>
    public void Notify_PartnerLost()
    {
        Building_Elevator formerPartner = partner;
        formerPartner?.UnregisterLoader(this);
        partner = null;
        transferAllowed = false;

        ElevatorUtil.Log($"Partner lost: {ThingID} lost {formerPartner?.LogId ?? "none"}; transfer switched off.");
        Messages.Message("DeepElevator_MsgLoaderUnlinked".Translate(), this, MessageTypeDefOf.NeutralEvent, historical: false);

        TryAutoLink();
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

    /// <summary>Links automatically only when there is exactly one candidate and no partner yet. Leaves the transfer switch alone.</summary>
    public void TryAutoLink()
    {
        if (partner != null)
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

    // ---------------------------------------------------------------- gizmos

    public override IEnumerable<Gizmo> GetGizmos()
    {
        bool isLoading = transporter.LoadingInProgressOrReadyToLaunch;

        foreach (Gizmo gizmo in base.GetGizmos())
        {
            // The vanilla cancel button only shows while the container holds something, so it would flicker as
            // the elevator empties the container. Ours (below) is always there while a load exists.
            if (gizmo is Command_Action { icon: var vanillaIcon } && vanillaIcon == CompTransporter.CancelLoadCommandTex)
            {
                continue;
            }

            // A manifest can only be started once there is an elevator to hand the items to.
            if (gizmo is Command_LoadToTransporter loadCommand && !isLoading && partner == null)
            {
                loadCommand.Disable("DeepElevator_LoaderNeedsLink".Translate());
            }
            yield return gizmo;
        }

        if (isLoading || Container.Any)
        {
            yield return CreateCancelOrUnloadCommand();
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
                defaultLabel = "DEV: log loader state",
                action = LogLoaderState
            };
        }
    }

    /// <summary>
    /// Our own cancel button, with the vanilla texts: "cancel loading" while items are left to load, otherwise
    /// "unload all". Without a load (items left behind, e.g. after transfer was switched off) it just drops them.
    /// </summary>
    private Command_Action CreateCancelOrUnloadCommand()
    {
        bool itemsLeftToLoad = transporter.AnythingLeftToLoad;
        return new Command_Action
        {
            defaultLabel = (itemsLeftToLoad ? "CommandCancelLoad" : "CommandUnload").Translate(),
            defaultDesc = itemsLeftToLoad ? "CommandCancelLoadDesc".Translate() : "CommandUnloadDesc".Translate(LabelShort),
            icon = CompTransporter.CancelLoadCommandTex,
            action = () =>
            {
                SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
                if (transporter.LoadingInProgressOrReadyToLaunch)
                {
                    transporter.CancelLoad();
                }
                else
                {
                    // Safety path: items in the container but no load to cancel.
                    Container.TryDropAll(Position, Map, ThingPlaceMode.Near);
                }
                ElevatorUtil.Log($"Cancel/unload: {ThingID} (itemsLeftToLoad={itemsLeftToLoad})");
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
            icon = ElevatorTextures.LoaderPriority,
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
    /// wins). Elevator states such as a full buffer or no power are not described here, only whether the
    /// elevator can take what is waiting. Null when there is nothing to say.
    /// </summary>
    private string DescribeStatus()
    {
        var statusParts = new List<string>();

        bool isLoading = transporter.LoadingInProgressOrReadyToLaunch;
        if (isLoading)
        {
            statusParts.Add((transporter.AnythingLeftToLoad ? "DeepElevator_LoaderStatusLoading" : "DeepElevator_LoaderStatusComplete").Translate());
        }

        string transferPart = DescribeTransferPart(isLoading);
        if (transferPart != null)
        {
            statusParts.Add(transferPart);
        }
        return statusParts.Count > 0 ? string.Join("; ", statusParts) : null;
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

        // Nothing to transfer and nothing coming: idle, say nothing.
        if (!isLoading && !Container.Any)
        {
            return null;
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

    /// <summary>DEV: dumps the link, the settings, the loading state and the container.</summary>
    private void LogLoaderState()
    {
        var report = new StringBuilder();
        report.Append($"Loader: {ThingID} partner={partner?.LogId ?? "none"} transfer={transferAllowed} priority={priority} ");
        report.Append($"loading={transporter.LoadingInProgressOrReadyToLaunch} groupID={transporter.groupID} leftToLoad={transporter.AnythingLeftToLoad} ");
        report.Append($"lastPullTick={lastPullTick} (now {Find.TickManager.TicksGame}) candidates={Candidates().Count}");
        ThingOwner container = Container;
        for (int entryIndex = 0; entryIndex < container.Count; entryIndex++)
        {
            report.Append($"\n    #{entryIndex} {ElevatorUtil.Describe(container[entryIndex])}");
        }
        ElevatorUtil.Log(report.ToString());
    }
}
