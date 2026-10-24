using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace DeepElevator;

/// <summary>
/// Step A: an elevator that pairs with exactly one elevator on another DeepRim layer.
/// No storage or transport yet; this step only establishes, persists and tears down the link.
/// </summary>
public class Building_Elevator : Building
{
    /// <summary>Interval for periodic work; matches the Rare ticker so switching tickerType is XML-only.</summary>
    public const int PeriodicInterval = GenTicks.TickRareInterval;

    // The linked elevator on another layer, and the map it lives on. Both null when unconnected.
    private Building_Elevator partner;
    private Map partnerMap;

    public Building_Elevator Partner => partner;

    public bool IsConnected => partner != null;

    // ---------------------------------------------------------------- saving

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref partner, "partner");
        Scribe_References.Look(ref partnerMap, "partnerMap");

        if (Scribe.mode == LoadSaveMode.PostLoadInit && (partner == null) != (partnerMap == null))
        {
            ElevatorUtil.LogWarn($"{ThingID}: inconsistent link after load (partner={partner?.ThingID ?? "null"}, partnerMap={(partnerMap == null ? "null" : "set")}); clearing.");
            partner = null;
            partnerMap = null;
        }
    }

    // ---------------------------------------------------------------- lifecycle

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        ElevatorUtil.Log($"SpawnSetup: {ThingID} on {ElevatorUtil.MapDesc(map)} respawningAfterLoad={respawningAfterLoad} partner={partner?.ThingID ?? "none"}");

        // Never pair or validate while loading: the partner may live on a map that has not spawned yet.
        if (!respawningAfterLoad && partner == null)
        {
            TryAutoPair();
        }
    }

    /// <summary>Links automatically only when exactly one unambiguous candidate exists.</summary>
    private void TryAutoPair()
    {
        List<Building_Elevator> candidates = ElevatorUtil.UnconnectedCandidates(this);
        ElevatorUtil.Log($"TryAutoPair: {ThingID} found {candidates.Count} candidate(s)");
        if (candidates.Count == 1)
        {
            Link(candidates[0]);
        }
    }

    public void Link(Building_Elevator other)
    {
        if (other == null || other == this || partner != null || other.partner != null)
        {
            ElevatorUtil.LogWarn($"Link refused: {ThingID} -> {other?.ThingID ?? "null"} (self partner={partner?.ThingID ?? "none"}, other partner={other?.partner?.ThingID ?? "none"})");
            return;
        }

        partner = other;
        partnerMap = other.Map;
        other.partner = this;
        other.partnerMap = Map;

        ElevatorUtil.Log($"Linked: {ThingID} ({ElevatorUtil.MapDesc(Map)}) <-> {other.ThingID} ({ElevatorUtil.MapDesc(other.Map)})");
        Messages.Message("DeepElevator_MsgLinked".Translate(ElevatorUtil.LayerLabel(other.Map)), this, MessageTypeDefOf.PositiveEvent, historical: false);
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

        ElevatorUtil.Log($"Unlinked ({reason}): {ThingID} x {formerPartner.ThingID} (partner map {ElevatorUtil.MapDesc(formerPartnerMap)})");

        // Tell the player about whichever end survived, if any.
        Building_Elevator survivor = Spawned ? this : (formerPartner.Spawned ? formerPartner : null);
        if (survivor != null)
        {
            Map lostLayerMap = survivor == this ? formerPartnerMap : Map;
            Messages.Message("DeepElevator_MsgUnlinked".Translate(ElevatorUtil.LayerLabel(lostLayerMap)), survivor, MessageTypeDefOf.NeutralEvent, historical: false);
        }
    }

    /// <summary>
    /// Shared handler for every callback that can end this elevator's life on its map.
    /// Called before the base implementation, because the base calls change state
    /// (DeSpawn clears the map, Notify_MyMapRemoved makes Map null).
    /// </summary>
    private void OnLifecycleEnd(string callbackName, string details)
    {
        ElevatorUtil.Log($"{callbackName}: {ThingID} on {ElevatorUtil.MapDesc(Map)} spawned={Spawned} {details} partner={partner?.ThingID ?? "none"}");
        Unlink(callbackName);
    }

    // Deconstructed or destroyed while the map stays.
    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        OnLifecycleEnd(nameof(DeSpawn), $"mode={mode}");
        base.DeSpawn(mode);
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
        base.Notify_MyMapRemoved();
    }

    // ---------------------------------------------------------------- periodic work

    // tickerType Rare: called every 250 ticks.
    public override void TickRare()
    {
        base.TickRare();
        DoPeriodicWork();
    }

    // tickerType Normal: called every tick, throttled to the same interval.
    protected override void Tick()
    {
        base.Tick();
        if (this.IsHashIntervalTick(PeriodicInterval))
        {
            DoPeriodicWork();
        }
    }

    /// <summary>
    /// All periodic logic lives here so it is independent of the ticker type.
    /// Currently only a backstop: the lifecycle callbacks should already have caught every broken link.
    /// </summary>
    private void DoPeriodicWork()
    {
        if (partner != null && !IsValidPartner(out string invalidReason))
        {
            ElevatorUtil.LogWarn($"Validation: {ThingID} partner {partner.ThingID} invalid ({invalidReason}); unlinking.");
            Unlink("validation: " + invalidReason);
        }
    }

    public bool IsValidPartner(out string invalidReason)
    {
        if (partner == null) { invalidReason = "no partner"; return false; }
        if (partnerMap == null) { invalidReason = "partnerMap null"; return false; }
        if (!Find.Maps.Contains(partnerMap)) { invalidReason = "partner map removed"; return false; }
        if (partner.Destroyed) { invalidReason = "partner destroyed"; return false; }
        if (!partner.Spawned) { invalidReason = "partner not spawned"; return false; }
        if (partner.Map != partnerMap) { invalidReason = "partner on different map"; return false; }
        if (partner.partner != this) { invalidReason = "partner links elsewhere (" + (partner.partner?.ThingID ?? "none") + ")"; return false; }

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

        if (partner == null)
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

        if (Prefs.DevMode)
        {
            yield return new Command_Action
            {
                defaultLabel = "DEV: log link state",
                action = LogLinkState
            };
        }
    }

    /// <summary>Lets the player choose which of several ambiguous candidates to link with.</summary>
    private void OpenConnectMenu(List<Building_Elevator> candidates)
    {
        List<FloatMenuOption> options = candidates
            .Select(candidate => new FloatMenuOption(ElevatorUtil.LayerLabel(candidate.Map), () =>
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
        string keyText = ElevatorUtil.TryGetKey(this, out IntVec3 key) ? key.ToString() : "n/a";
        string validityText = partner == null ? "-" : (IsValidPartner(out string invalidReason) ? "yes" : invalidReason);
        ElevatorUtil.Log($"State: {ThingID} on {ElevatorUtil.MapDesc(Map)} key={keyText} partner={partner?.ThingID ?? "none"} partnerMap={ElevatorUtil.MapDesc(partnerMap)} valid={validityText} candidates={ElevatorUtil.UnconnectedCandidates(this).Count}");
    }

    public override string GetInspectString()
    {
        var text = new StringBuilder();
        string baseText = base.GetInspectString();
        if (!baseText.NullOrEmpty())
        {
            text.AppendLine(baseText);
        }

        if (partner != null)
        {
            text.AppendLine("DeepElevator_ConnectedTo".Translate(ElevatorUtil.LayerLabel(partnerMap)));
        }
        else
        {
            text.AppendLine("DeepElevator_NotConnected".Translate());
            // Auto-pairing only runs on build, so show the hint for any candidate count above zero.
            int candidateCount = ElevatorUtil.UnconnectedCandidates(this).Count;
            if (candidateCount > 0)
            {
                text.AppendLine("DeepElevator_CandidatesAvailable".Translate(candidateCount));
            }
        }

        return text.ToString().TrimEndNewlines();
    }
}
