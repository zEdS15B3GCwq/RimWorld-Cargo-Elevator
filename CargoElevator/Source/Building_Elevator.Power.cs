using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CargoElevator;

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
/// Light: a lights-only overlay (lamp pixels only, drawn after lighting) over the base sprite every frame; plus the glow.
/// </summary>
public partial class Building_Elevator
{
    /// <summary>How often (in ticks) the draw state and the logged status are re-evaluated.</summary>
    private const int PowerUpdateInterval = 30;

    // Runtime only (except blockedSinceTick, which is saved); power state is recomputed after load.
    private CompPowerTrader powerComp;
    private int lastActiveTick = -99999;   // last transfer involving this pair
    private bool powerActive;              // current draw state
    private int blockedSinceTick = -1;     // when the current uninterrupted hold started; -1 = not holding (saved: an absolute game tick)

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
        string statusText = ("CargoElevator_Status" + status).Translate();

        // The unlinked state hides the power state in Status (disconnected wins); the text shows both.
        if (status == ElevatorStatus.Disconnected && !IsPowered)
        {
            return "CargoElevator_StatusUnpoweredDisconnected".Translate("CargoElevator_StatusNoPower".Translate(), statusText).ToString();
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

    /// <summary>What the lamp shows. The single rule for the sprite light and the glow, so they cannot diverge.</summary>
    private enum LightState
    {
        Off,
        Green,
        Red
    }

    private LightState CurrentLightState
    {
        get
        {
            switch (Status)
            {
                case ElevatorStatus.Disconnected:
                case ElevatorStatus.NoPower:
                case ElevatorStatus.PartnerUnpowered:
                    return LightState.Off;
                case ElevatorStatus.Blocked:
                    return LightState.Red;
                default:
                    return LightState.Green;
            }
        }
    }

    private const int GreenLightIndex = 0;
    private const int RedLightIndex = 1;
    private const int LightCount = 2;

    // Per def: light graphics by index above; an entry is null where the extension names no (loadable) texture.
    // The lights are lights-only overlays (transparent except the lamp) drawn with a shader that ignores darkness.
    private static readonly Dictionary<ThingDef, Graphic[]> lightGraphicsByDef = new Dictionary<ThingDef, Graphic[]>();

    /// <summary>
    /// MapMeshAndRealTime: the base sprite is already in the map mesh, so only the light is drawn here.
    /// Runs every frame, so it only reads fields and cached graphics.
    /// </summary>
    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        base.DrawAt(drawLoc, flip);

        LightState lightState = CurrentLightState;
        if (lightState == LightState.Off)
        {
            return;
        }

        int lightIndex = lightState == LightState.Red ? RedLightIndex : GreenLightIndex;

        Graphic lightGraphic = GetLightGraphics()[lightIndex];
        if (lightGraphic != null)
        {
            // Half an altitude step: just above the base sprite and still below items lying on the cell.
            lightGraphic.Draw(drawLoc + Altitudes.AltIncVect * 0.5f, Rot4.North, this);
        }
    }

    /// <summary>Lazily loads the def's light graphics (first use is in DrawAt, on the main thread after content loading).</summary>
    private Graphic[] GetLightGraphics()
    {
        if (lightGraphicsByDef.TryGetValue(def, out Graphic[] lightGraphics))
        {
            return lightGraphics;
        }

        lightGraphics = new Graphic[LightCount];
        lightGraphics[GreenLightIndex] = ElevatorUtil.LoadOverlayGraphic(def, Settings.greenLightTexPath, ShaderDatabase.TransparentPostLight);
        lightGraphics[RedLightIndex] = ElevatorUtil.LoadOverlayGraphic(def, Settings.redLightTexPath, ShaderDatabase.TransparentPostLight);
        lightGraphicsByDef[def] = lightGraphics;
        return lightGraphics;
    }

    // ---------------------------------------------------------------- glow

    // Runtime only. Null = not applied yet, so the first UpdateGlow after spawning always writes the comp.
    private CompGlower glowComp;
    private LightState? lastGlowState;

    // The glower asks whether the lamp is on (alongside its own checks: spawned, flick, power).
    bool IThingGlower.ShouldBeLitNow() => CurrentLightState != LightState.Off;

    private void CacheGlow()
    {
        glowComp = GetComp<CompGlower>();
        if (glowComp == null)
        {
            ElevatorUtil.LogWarn($"{LogId}: the def has no CompGlower; the elevator does not glow.");
        }
    }

    /// <summary>
    /// Brings the glower in line with the light state. Does nothing while the state is unchanged. Order matters:
    /// UpdateLit first (makes the registration agree with ShouldBeLitNow), then the colour (its setter
    /// re-registers a lit light). Called from the periodic tick and on spawn and link changes; never per frame.
    /// </summary>
    private void UpdateGlow()
    {
        if (glowComp == null || !Spawned)
        {
            return;
        }

        LightState state = CurrentLightState;
        if (state == lastGlowState)
        {
            return;
        }

        glowComp.UpdateLit(Map);
        if (state != LightState.Off)
        {
            ColorInt color = state == LightState.Red ? Settings.redGlowColor : Settings.greenGlowColor;
            if (glowComp.GlowColor != color)
            {
                glowComp.GlowColor = color;
            }
        }

        ElevatorUtil.Log($"Glow: {LogId} {(lastGlowState.HasValue ? lastGlowState.Value.ToString() : "-")} → {state}");
        lastGlowState = state;
    }
}
