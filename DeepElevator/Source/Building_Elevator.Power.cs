using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepElevator;

/// <summary>What an elevator is doing right now. Shared by the inspect line, the log and the state light.</summary>
public enum ElevatorStatus
{
    Disconnected,
    NoPower,
    PartnerUnpowered,
    Blocked,
    Active,
    Idle
}

/// <summary>
/// Power, status and the state light. Power: idle/active draw synced across the pair, and a pair only
/// works while both ends are powered. Status: one value derived from link, power, holds and activity.
/// Light: a full-size overlay sprite drawn over the base sprite every frame.
/// </summary>
public partial class Building_Elevator
{
    /// <summary>How often (in ticks) the draw state and the logged status are re-evaluated.</summary>
    private const int PowerUpdateInterval = 30;

    // Runtime only; power state is recomputed after load.
    private CompPowerTrader powerComp;
    private int lastActiveTick = -99999;   // last transfer involving this pair
    private bool powerActive;              // current draw state
    private int blockedSinceTick = -1;     // when the current uninterrupted hold started; -1 = not holding

    // Change detection for log messages.
    private string lastLoggedPauseReason;
    private ElevatorStatus? lastLoggedStatus;

    // ---------------------------------------------------------------- power

    private void CachePower()
    {
        powerComp = GetComp<CompPowerTrader>();
        if (powerComp == null)
        {
            ElevatorUtil.LogWarn($"{LogId}: the def has no CompPowerTrader; the elevator works without power.");
        }
    }

    /// <summary>Powered = the grid powers the power comp. Elevators without a power comp count as powered.</summary>
    public bool IsPowered => powerComp == null || powerComp.PowerOn;

    /// <summary>True while the draw state is "active" (a transfer happened within the hold window).</summary>
    public bool PowerActive => powerActive;

    /// <summary>Linked to a valid partner and both ends powered. Everything that moves items requires this.</summary>
    public bool IsOperational => IsConnected && IsPowered && partner.IsPowered;

    /// <summary>Called on a successful delivery: both ends are working, so both draw active power.</summary>
    private void MarkTransferActive()
    {
        int now = Find.TickManager.TicksGame;
        lastActiveTick = now;
        if (partner != null)
        {
            partner.lastActiveTick = now;
        }

        // Immediate, so the draw follows the first transfer instead of waiting for the next periodic update.
        UpdatePowerDraw();
        partner?.UpdatePowerDraw();
    }

    /// <summary>
    /// Active while the last transfer is within the hold window (at least one send interval plus a margin,
    /// so there is no flicker at the maximum rate). Writes the comp's PowerOutput only on change.
    /// </summary>
    private void UpdatePowerDraw()
    {
        if (powerComp == null)
        {
            return;
        }

        bool shouldBeActive = Find.TickManager.TicksGame - lastActiveTick <= Settings.ActiveHoldTicks;
        if (shouldBeActive == powerActive && powerComp.PowerOutput != 0f)
        {
            return; // nothing to change (PowerOutput 0 = not set up yet)
        }

        powerActive = shouldBeActive;

        // Same pattern as vanilla buildings that switch between idle and active: PowerOutput is set
        // regardless of PowerOn; while unpowered the grid simply does not supply it.
        float activeConsumption = powerComp.Props.PowerConsumption;
        float idleConsumption = powerComp.Props.idlePowerDraw < 0f ? activeConsumption : powerComp.Props.idlePowerDraw;
        powerComp.PowerOutput = powerActive ? -activeConsumption : -idleConsumption;
        ElevatorUtil.Log($"Power: {LogId} {(powerActive ? "active" : "idle")} ({-powerComp.PowerOutput:F0} W)");
    }

    /// <summary>The reason nothing can be moved right now, or null when operational. For logs.</summary>
    private string DescribeNotOperationalReason()
    {
        if (!IsConnected) return "not connected";
        if (!IsPowered) return "no power";
        if (!partner.IsPowered) return "partner unpowered";
        return null;
    }

    /// <summary>Logs "Send paused" once per change of the reason; the caller resets the field when sending works again.</summary>
    private void LogSendPausedIfChanged()
    {
        string reason = DescribeNotOperationalReason() ?? "partner link invalid";
        if (reason != lastLoggedPauseReason)
        {
            lastLoggedPauseReason = reason;
            ElevatorUtil.Log($"Send paused: {LogId} → {partner?.LogId ?? "no partner"}: {reason}");
        }
    }

    // ---------------------------------------------------------------- blocked / status

    /// <summary>
    /// Holds persisted for several send attempts. A delivery clears the hold at once, so with FIFO off a
    /// partly moving queue is never blocked. Not operational states win over this (TrySend clears the hold).
    /// </summary>
    public bool IsBlocked => blockedSinceTick >= 0 && BufferEntryCount > 0
        && Find.TickManager.TicksGame - blockedSinceTick >= Settings.blockedAfterSendIntervals * Settings.HeldRetryTicks;

    /// <summary>First match wins: link, own power, partner power, blocked, active, idle.</summary>
    public ElevatorStatus Status
    {
        get
        {
            if (!IsConnected) return ElevatorStatus.Disconnected;
            if (!IsPowered) return ElevatorStatus.NoPower;
            if (!partner.IsPowered) return ElevatorStatus.PartnerUnpowered;
            if (IsBlocked) return ElevatorStatus.Blocked;
            return powerActive ? ElevatorStatus.Active : ElevatorStatus.Idle;
        }
    }

    /// <summary>The status as shown in the inspect line.</summary>
    private string DescribeStatus()
    {
        ElevatorStatus status = Status;
        string statusText = ("DeepElevator_Status" + status).Translate();

        // The unlinked state hides the power state in Status (disconnected wins); the text shows both.
        if (status == ElevatorStatus.Disconnected && !IsPowered)
        {
            return "DeepElevator_StatusUnpoweredDisconnected".Translate("DeepElevator_StatusNoPower".Translate(), statusText).ToString();
        }

        return statusText;
    }

    /// <summary>Logs a status change. Called from the periodic tick only, never from drawing or inspecting.</summary>
    private void LogStatusIfChanged()
    {
        ElevatorStatus status = Status;
        if (status == lastLoggedStatus)
        {
            return;
        }

        ElevatorUtil.Log($"Status: {LogId} {(lastLoggedStatus.HasValue ? lastLoggedStatus.Value.ToString() : "-")} → {status}");
        lastLoggedStatus = status;
    }

    // ---------------------------------------------------------------- state light

    private const int GreenLightIndex = 0;
    private const int RedLightIndex = 1;
    private const int LightCount = 2;

    // Per def: light graphics by index above; an entry is null where the extension names no (loadable) texture.
    private static readonly Dictionary<ThingDef, Graphic[]> lightGraphicsByDef = new Dictionary<ThingDef, Graphic[]>();

    /// <summary>
    /// MapMeshAndRealTime: the base sprite is already in the map mesh, so only the light is drawn here.
    /// Runs every frame, so it only reads fields and a cached graphic.
    /// </summary>
    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        base.DrawAt(drawLoc, flip);

        Graphic lightGraphic = CurrentLightGraphic();
        if (lightGraphic != null)
        {
            // Half an altitude step: just above the base sprite and still below items lying on the cell.
            lightGraphic.Draw(drawLoc + Altitudes.AltIncVect * 0.5f, Rot4.North, this);
        }
    }

    /// <summary>
    /// Null = no light (the off look is the base sprite). Operational: green, or red when blocked.
    /// </summary>
    private Graphic CurrentLightGraphic()
    {
        ElevatorStatus status = Status;
        if (status == ElevatorStatus.Disconnected || status == ElevatorStatus.NoPower || status == ElevatorStatus.PartnerUnpowered)
        {
            return null;
        }

        return GetLightGraphics()[status == ElevatorStatus.Blocked ? RedLightIndex : GreenLightIndex];
    }

    /// <summary>Lazily loads the def's light graphics (first use is in DrawAt, on the main thread after content loading).</summary>
    private Graphic[] GetLightGraphics()
    {
        if (lightGraphicsByDef.TryGetValue(def, out Graphic[] lightGraphics))
        {
            return lightGraphics;
        }

        lightGraphics = new Graphic[LightCount];
        lightGraphics[GreenLightIndex] = ElevatorUtil.LoadOverlayGraphic(def, Settings.greenTexPath);
        lightGraphics[RedLightIndex] = ElevatorUtil.LoadOverlayGraphic(def, Settings.redTexPath);
        lightGraphicsByDef[def] = lightGraphics;
        return lightGraphics;
    }
}
