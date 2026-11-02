# DeepElevator – Step D implementation spec (handoff)

Read `plan.md` (rev 8) first, especially §1 "Steps" → Step D (decisions, status table), §2 "Power and drawing", and `results_stepC.md` §1 (how the code is organised now). This file is self-contained for Step D. **Don't implement anything beyond it:** no VFE-specific code (Step E), no manifest / Load dialog (Step F; the Load gizmo stays hidden), no Harmony, no mod settings, no on/off switch, no probe removal.

**Goal of Step D:**
- A **2x2 elevator** that behaves exactly like the 1x1: 4 intake cells, a 12-cell output ring, 4x faster sending, 8 slots.
- **Power:** idle/active draw, synced across the pair; a pair works only when both ends are powered.
- A combined **status line**.
- **State lights** drawn over the base sprite, with blinking prepared for the manifest step.

**Done when:** `dotnet build DeepElevator.csproj` (Debug) gives 0 errors / 0 warnings; `DeepElevator\1.6\Assemblies\` contains only `DeepElevator.dll` (+ `.pdb`); the human test checklist (§9) can be run. **No git commits** (the human commits). Backwards compatibility with Step C saves is not required.

## 0. Environment / conventions
- Repo root `C:\Code\Rimworld\DeepElevator`. SDK-style `DeepElevator.csproj` (`net472`, LangVersion latest) references `include\Assembly-CSharp.dll`, `include\UnityEngine.CoreModule.dll`, `include\UnityEngine.IMGUIModule.dll`. These are symlinks to `D:\G\RimWorld\RimWorldWin64_Data\Managed\…`.
- Decompiler: `dotnet ilspycmd -t <Type> "D:\G\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll"`, **run from the repo root**.
- Code layout (Step C): `Building_Elevator` is `partial`: `Building_Elevator.cs` (link, lifecycle, tick, UI), `.Buffer.cs` (intake, buffer, gizmos, inspect lines), `.Send.cs` (sending, delivery, hold state). Add a new part **`Building_Elevator.Power.cs`** for power, status and drawing.
- Style: file-scoped namespace, XML doc comments, section comments, logging via `ElevatorUtil.Log` / `LogWarn` with `LogId`.
- **No code duplication between sizes.** Everything size-dependent comes from the footprint (`OccupiedRect()`, `AllSlotCellsList()`) or from the def's `ElevatorSettingsExtension`. There's no `if (is2x2)`.
- **Hot paths** (`Accepts`, `DrawAt`, place-worker hooks, gizmo/inspect builders): no allocations, no logging except change-detected lines, O(1) or O(buffer entries).

## 1. Facts this step relies on (verified)

| Fact | Source |
| --- | --- |
| `CompProperties_Power`: `basePowerConsumption` (= active, shown as "active: X W"), `idlePowerDraw` (default -1). `CompPowerTrader.PowerOutput` is settable (negative = consumption). `PowerOn` is the grid result. `SetUpPowerVars` uses `-idlePowerDraw` only while not `PowerOn`. **Nothing switches idle/active automatically**: buildings do it themselves, e.g. `Building_GeneAssembler.Tick`: every 250 ticks `PowerOutput = workedRecently ? -Props.PowerConsumption : -Props.idlePowerDraw` | decompiled `RimWorld.CompPowerTrader`, `CompProperties_Power`, `Building_GeneAssembler` (also GeneExtractor, GrowthVat, MechGestator) |
| `CompPowerTrader.CompInspectStringExtra` prints "Power needed: X W (active: Y W)" when `idlePowerDraw > 0`; the vanilla "needs power" overlay shows automatically | decompiled |
| `drawerType`: `MapMeshOnly` = baked into the section mesh; `RealtimeOnly` = `DrawAt` every frame; `MapMeshAndRealTime` = both. The thing is printed into the mesh **and** `DynamicDrawPhaseAt` → `protected virtual void DrawAt(Vector3 drawLoc, bool flip = false)` runs every frame (base `DrawAt` draws the graphic only for `RealtimeOnly`). Non-`MapMeshOnly` things register with the dynamic draw manager | decompiled `Verse.Thing` |
| `Graphic.Draw(Vector3 loc, Rot4 rot, Thing thing, float extraRotation = 0f)`; `GraphicDatabase.Get<Graphic_Single>(path, shader, drawSize, color)`; `Altitudes.AltInc` = 0.0366 | decompiled `Verse.Graphic`, `Verse.Altitudes` |
| XML inheritance: a child's `<li>` lists are **appended** to the parent's, unless the child node has `Inherit="False"` (then it replaces) | decompiled `Verse.XmlInheritance.RecursiveNodeCopyOverwriteElements` |
| `CompTransporter.LoadingInProgressOrReadyToLaunch` (public) = a load is set up | decompiled |
| The game reserves each storage cell for one hauling pawn, so 4 intake cells = up to 4 parallel hauls | `StoreUtility.IsGoodStoreCell` (plan.md §2) |
| Size-agnostic already: `AbsorbIntake` (loops `AllSlotCellsList()`), `ElevatorUtil.OutputCells` (rect expanded by 1 minus the rect), `StorageCellRoom`, `DeliveryDropRoom`, delivery and drop loops, Unload (`Near` from `Position`) | current source |
| **Not** size-agnostic yet: `UnconnectedCandidates` (`GetFirstThing<Building_Elevator>` at the matching cell, any def), `PlaceWorker_Elevator.AddTargets` (`ThingsOfDef(DeepElevator_Elevator)`, highlights one cell), `ElevatorDiagnostics` (one def), `LogLinkState` (`GetFirstThing`) | current source |

## 2. Defs

### 2.1 `DeepElevator\Defs\ThingDefs\DeepElevator_Elevator.xml`: abstract parent + two children
- `<ThingDef Name="DeepElevatorBase" ParentName="BuildingBase" Abstract="True">` holds **everything shared**:
  - `thingClass`, `graphicData` (`graphicClass Graphic_Single`; `texPath` / `drawSize` are set per child);
  - `rotatable false`, `altitudeLayer Building`, `passability PassThroughOnly`, `fillPercent`, `pathCost`;
  - `tickerType Normal`, **`drawerType MapMeshAndRealTime`**, `surfaceType Item`;
  - `inspectorTabs`, `canOverlapZones false`, `designationCategory`, `placeWorkers`, `constructEffect`;
  - the whole `building` block (storage settings, `maxItemsInCell 1`, …);
  - the `CompProperties_Transporter` comp.
- **Lists that differ must only appear in the children:** `costList`, `statBases`, the `CompProperties_Power` comp, `modExtensions`. Children's `<li>` lists are appended to the parent's. A power comp or extension in both would give two entries, and `GetModExtension` returns the first. If a list must be both inherited and replaced, use `Inherit="False"` on the child's node.
- `<ThingDef ParentName="DeepElevatorBase">` **`DeepElevator_Elevator`** (keep the defName; 1x1):
  - label "deep elevator", `size (1,1)`, `graphicData texPath Things/Buildings/DeepElevator/DeepElevator`, `drawSize (1,1)`;
  - `costList` Steel 75, ComponentIndustrial 2; `statBases` as now;
  - power comp: active 200, idle 10;
  - extension: `defaultSlots 4, minSlots 1, maxSlots 4, ticksPerStack 60`, overlays `DeepElevator_green / _red / _orange`.
- `<ThingDef ParentName="DeepElevatorBase">` **`DeepElevator_Elevator2x2`**:
  - label "large deep elevator", description mentions 4 intake cells and faster transfer;
  - `size (2,2)`, `texPath Things/Buildings/DeepElevator/DeepElevator_2x2`, `drawSize (2,2)`;
  - `costList` Steel 250, ComponentIndustrial 6 (placeholder; human adjusts); `statBases`: MaxHitPoints 400, WorkToBuild 4000, Mass 100, Flammability 0;
  - power comp: active 500, idle 25;
  - extension: `defaultSlots 8, minSlots 1, maxSlots 8, ticksPerStack 15`, overlays `…_2x2`.
- Power comp (children only):
  ```xml
  <li Class="CompProperties_Power">
    <compClass>CompPowerTrader</compClass>
    <basePowerConsumption>200</basePowerConsumption>  <!-- active -->
    <idlePowerDraw>10</idlePowerDraw>
    <shortCircuitInRain>false</shortCircuitInRain>
  </li>
  ```
- No `CompProperties_Flickable` (decision: no on/off switch).

### 2.2 `ElevatorSettingsExtension`: new fields
```csharp
// Ticks a transfer keeps both ends in the active power state; -1 = ticksPerStack + ActiveMarginTicks.
public int activeHoldTicks = -1;
public const int ActiveMarginTicks = 30;
public int ActiveHoldTicks => activeHoldTicks > 0 ? activeHoldTicks : ticksPerStack + ActiveMarginTicks;

// "Blocked" (red) needs this many send intervals without a delivery while items are held.
public int blockedAfterSendIntervals = 2;

// State light overlays (full sprites on the same canvas as the base; drawn over it). Null = no light.
public string greenTexPath, redTexPath, orangeTexPath;
```
Default `maxSlots` in the C# class stays a fallback only; the XML values rule.

### 2.3 `DeepElevatorDefOf`
Add `public static ThingDef DeepElevator_Elevator2x2;`.

### 2.4 Language keys
- Remove: `DeepElevator_ConnectedTo`, `DeepElevator_NotConnected`, `DeepElevator_PausedNotConnected`.
- Add:
```xml
<!-- Status line: {0} = status text, {1} = partner label (ElevatorLabel). -->
<DeepElevator_StatusLinked>Status: {0}, connected to {1}</DeepElevator_StatusLinked>
<DeepElevator_StatusUnlinked>Status: {0}</DeepElevator_StatusUnlinked>
<DeepElevator_StatusDisconnected>disconnected</DeepElevator_StatusDisconnected>
<DeepElevator_StatusNoPower>no power</DeepElevator_StatusNoPower>
<DeepElevator_StatusPartnerUnpowered>partner unpowered</DeepElevator_StatusPartnerUnpowered>
<DeepElevator_StatusBlocked>blocked</DeepElevator_StatusBlocked>
<DeepElevator_StatusActive>active</DeepElevator_StatusActive>
<DeepElevator_StatusIdle>idle</DeepElevator_StatusIdle>
<!-- {0} = blocked/active/idle -->
<DeepElevator_StatusLoading>loading, {0}</DeepElevator_StatusLoading>
```
The "no power" and "partner unpowered" texts are used with the linked format (the partner exists).

## 3. 2x2 support in shared code (generalise, don't copy)

### 3.1 `ElevatorUtil.UnconnectedCandidates(self)`
Replace `matchingCell.GetFirstThing<Building_Elevator>(otherMap)` with a new helper:
```csharp
/// The elevator of def <paramref name="def"/> whose anchor (Position) is exactly <paramref name="cell"/>, or null.
public static Building_Elevator ElevatorAnchoredAt(IntVec3 cell, Map map, ThingDef def)
// loop cell.GetThingList(map): thing is Building_Elevator e && e.def == def && e.Position == cell
```
Candidate = `ElevatorAnchoredAt(matchingCell, otherMap, self.def)` with the existing checks (not self, spawned, unconnected).
- **No pairing between different sizes** (same def only).
- A 2x2 matches only at its anchor cell, never through one of its other cells.
- Matching stays anchor-based (`Position`, `CenterOffset`). For a non-rotatable even-sized building, `Position` is a fixed cell of the footprint, so the same def maps footprint to footprint.

### 3.2 `PlaceWorker_Elevator`
- The cache is keyed by `(map, def)`: add `cachedDef` and rebuild when it differs. Both hooks get `def`: `DrawGhost(ThingDef def, …)`, `DrawPlaceMouseAttachments(…, BuildableDef def, …)`.
- `AddTargets(map, otherMap, ThingDef def)`: `otherMap.listerThings.ThingsOfDef(def)`. For each unconnected elevator, `cellHere` = matching anchor cell. `targetsByCell[cellHere]` (the anchor, since the ghost and `center` are anchored at the mouse cell). `targetCells` gets **every cell of** `GenAdj.OccupiedRect(cellHere, Rot4.North, def.Size)` clipped to the map, so the whole footprint is outlined.
- Overlap outlines are unchanged (size-independent).
- Cursor text lookup by `center` (the anchor) is unchanged.

### 3.3 `Building_Elevator.LogLinkState`
Use `ElevatorUtil.ElevatorAnchoredAt(matchingCell, otherMap, def)`.

### 3.4 `ElevatorDiagnostics`
Loop over all defs whose `thingClass` is `Building_Elevator` (or a subclass): `DefDatabase<ThingDef>.AllDefsListForReading.Where(d => typeof(Building_Elevator).IsAssignableFrom(d.thingClass))`. For each, print the existing lines plus `size`, `drawerType`, power comp values (`basePowerConsumption`, `idlePowerDraw`) and the new extension fields.

### 3.5 Everything else
- `AbsorbIntake`, `OutputCells`, cell-room and drop functions, delivery, drops and Unload are already footprint-based. Only check that nothing else uses `GetFirstThing<Building_Elevator>` or assumes one cell (`grep`).
- `Notify_ReceivedThing` fires for any intake cell (fine).

## 4. Power — new `Building_Elevator.Power.cs`

```csharp
private CompPowerTrader powerComp;      // cached in SpawnSetup (CacheTransporter → also CachePower)
private int lastActiveTick = -99999;    // last transfer involving this pair (runtime only)
private bool powerActive;               // current draw state (runtime only)
private const int PowerUpdateInterval = 30;

/// Powered = has a power comp and the grid powers it. Elevators without a power comp count as powered.
public bool IsPowered => powerComp == null || powerComp.PowerOn;

/// Linked to a valid partner and both ends powered. Everything that moves items requires this.
public bool IsOperational => IsConnected && IsPowered && partner.IsPowered;   // O(1); partner non-null when connected

/// Called on a successful delivery: both ends are working, so both draw active power.
private void MarkTransferActive()
{
    int now = Find.TickManager.TicksGame;
    lastActiveTick = now;
    if (partner != null) partner.lastActiveTick = now;
    UpdatePowerDraw(); partner?.UpdatePowerDraw();       // immediate, so the draw follows the first transfer
}

/// Active while the pair's last transfer is within the hold window (≥ one send interval + margin → no flicker at the max rate).
private void UpdatePowerDraw()
{
    if (powerComp == null) return;
    bool active = Find.TickManager.TicksGame - lastActiveTick <= Settings.ActiveHoldTicks;
    if (active == powerActive && powerComp.PowerOutput != 0f) return;   // nothing to change (PowerOutput 0 = not set up yet)
    powerActive = active;
    powerComp.PowerOutput = active ? -powerComp.Props.PowerConsumption : -powerComp.Props.idlePowerDraw;
    ElevatorUtil.Log($"Power: {LogId} {(active ? "active" : "idle")} ({-powerComp.PowerOutput:F0} W)");
}
```
- `Tick()`: `if (this.IsHashIntervalTick(PowerUpdateInterval)) UpdatePowerDraw();`.
- Note: `PowerOutput` is set like the vanilla gene assembler does, regardless of `PowerOn`. While unpowered the grid simply doesn't supply it.
- If `idlePowerDraw` is not set in a def (-1), treat idle as `PowerConsumption` (guard: `idle < 0 ? consumption : idle`).
- `MarkTransferActive()` is called once per successful `TrySend` (in `Send.cs`, where `sent` is true), so **both ends** go active together. The partner's own `UpdatePowerDraw` keeps it active by reading its `lastActiveTick`, which `MarkTransferActive` set too.
- `CachePower()` in `SpawnSetup` next to `CacheTransporter()`; `PowerActive` exposed read-only for the status.

### 4.1 Operational rule (replaces "connected" in movement code)
- `Accepts`: replace the `!IsConnected` check with three refusals: `!IsConnected` → "not connected", `!IsPowered` → "no power", `!partner.IsPowered` → "partner unpowered". All O(1).
- `ShouldAbsorb`: same three reasons (ignore and leave on the cell; the game hauls it away, as with disconnected).
- `TrySend`:
  - If `!IsValidPartner(out _) || !IsOperational`: **don't set hold entries** (the status line explains it). Call `ClearHold()` if any are set, set `nextSendTick = now + PeriodicInterval`, and log once on change: `Send paused: {LogId} → {partner?.LogId}: {not connected | no power | partner unpowered}` (change-detected field).
  - The current `SetHold(..., HoldReason.NotConnected, ...)` branch is removed.
- Remove `HoldReason.NotConnected` and its uses (`SetHold` fill rule: entries not evaluated are `FifoWaiting` only; `DescribePausedTransfer` reason list).
- Unload and the contents tab keep working regardless.

## 5. Blocked state
- Add `private int blockedSinceTick = -1;` (runtime). In `SetHold(...)`: if `blockedSinceTick < 0`, set it to `now`. In `ClearHold()`: `blockedSinceTick = -1`.
- `public bool IsBlocked => blockedSinceTick >= 0 && BufferEntryCount > 0 && Find.TickManager.TicksGame - blockedSinceTick >= Settings.blockedAfterSendIntervals * Settings.HeldRetryTicks;`
- So red appears only after holds persist for ~2 send attempts, and a delivery clears it at once. With FIFO off, a partly moving queue isn't blocked, since each successful send clears the hold. Not operational → never "blocked" (the off states win; `TrySend` clears the hold).

## 6. Status (shared by the inspect line and the light)
```csharp
public enum ElevatorStatus { Disconnected, NoPower, PartnerUnpowered, Blocked, Active, Idle }

public ElevatorStatus Status
// first match: !IsConnected → Disconnected; !IsPowered → NoPower; !partner.IsPowered → PartnerUnpowered;
//              IsBlocked → Blocked; powerActive → Active; else Idle.
public bool IsManifestLoading => transporter != null && transporter.LoadingInProgressOrReadyToLaunch;   // never true in Step D
```
- **Inspect** (`GetInspectString` in `Building_Elevator.cs`): replace the "Connected to" / "Not connected" lines.
  - Connected: `"DeepElevator_StatusLinked".Translate(statusText, LevelLabels.ElevatorLabel(partner))`.
  - Unconnected: `"DeepElevator_StatusUnlinked".Translate("DeepElevator_StatusDisconnected".Translate())`, followed by the existing candidates hint.
  - `statusText` = the status key, wrapped in `DeepElevator_StatusLoading` when `IsManifestLoading` and the status is Blocked/Active/Idle.
  - The vanilla power line ("Power needed: …") comes from the base inspect string.
- **Paused line:** `DescribePausedTransfer` lists only `StorageFull`, `NoStorage`, `FifoWaiting`.

## 7. Drawing the state light (`Building_Elevator.Power.cs`)
- Static per-def cache: `Dictionary<ThingDef, Graphic[]>` (index 0 green, 1 red, 2 orange; null where the extension has no path). Filled lazily on first use **in `DrawAt`** (main thread, after content loading) with `GraphicDatabase.Get<Graphic_Single>(path, ShaderDatabase.Cutout, def.graphicData.drawSize, Color.white)`. A missing path or texture → null → no light (log one warning per def).
- `protected override void DrawAt(Vector3 drawLoc, bool flip = false)`:
  ```csharp
  base.DrawAt(drawLoc, flip);                    // MapMeshAndRealTime: the base sprite is already in the map mesh
  Graphic light = CurrentLightGraphic();         // null = draw nothing (off look = base sprite)
  if (light != null) light.Draw(drawLoc + Altitudes.AltIncVect * 0.5f, Rot4.North, this);
  ```
  The half altitude step puts the light just above the base sprite and still below items on the cell (Item altitude).
- `CurrentLightGraphic()`:
  - Status `Disconnected`, `NoPower`, `PartnerUnpowered` → null.
  - Otherwise `color = Status == Blocked ? red : green`.
  - If `IsManifestLoading`: blink between `orange` and that color, 1 s each, from **real time** (`Time.realtimeSinceStartup % 2f < 1f` → orange). Real time keeps the blink steady at any game speed and while paused.
  - Cheap: a few field reads per frame.
- No `MapMeshDirty` calls are needed: the mesh holds only the base sprite, which never changes.
- The menu icon, blueprint and ghost use `graphicData.texPath` (the base sprite = off look).

## 8. Logging (additions)
- `Power: … active/idle (W)` on change.
- `Send paused: …` on change of the not-operational reason.
- `Status: {LogId} {old} → {new}` when `Status` changes, checked every `PowerUpdateInterval` in `Tick` (change-detected; don't log from `DrawAt` or the inspect string).
- Startup diagnostics for both defs.

## 9. Human test checklist (Dev mode, watch the log)
1. **Defs:** both elevators appear in the build menu with their base sprite (off look); the 2x2 ghost and blueprint use `DeepElevator_2x2`. The diagnostics show both defs with size, power and extension values.
2. **Pairing:**
   - A 1x1 and a 2x2 at the same position never link (no candidate, no placement highlight).
   - Two 2x2 at the same center offset link automatically.
   - The placement helper outlines the full 2x2 footprint of unconnected 2x2s on other levels while placing a 2x2, and 1x1 cells only while placing a 1x1.
3. **2x2 intake:** with high priority and several pawns, more than one pawn delivers at the same time (up to 4). Items on any of the 4 cells are absorbed.
4. **2x2 output:** the 12 surrounding cells are used for delivery and drops; about 4 pieces per second; 8 slots (gizmo bounds 1–8).
5. **Power draw:**
   - Unconnected or idle: the inspect pane shows "Power needed: 10 W (active: 200 W)" (2x2: 25/500).
   - During a continuous transfer both ends show active (200 / 500 W) without flickering between pieces.
   - About half a second to a second after the last piece both drop back to idle.
   - Log `Power:` lines appear on changes only.
6. **No power:**
   - Cut power to the sender → status "no power", light off, pawns stop hauling to it, nothing absorbed (`Ignored … no power`), nothing sent; Unload works.
   - Cut power to the receiver → the sender shows "partner unpowered", the receiver "no power", both lights off.
   - Restore power → transfers resume.
7. **Status line:**
   - "Status: disconnected" (+ candidates hint) when unlinked.
   - "Status: idle, connected to …" when linked and idle; "active" while sending.
   - "blocked" (red light) when the destination storage stays full for ~2 attempts.
   - The "Transfer paused" line shows only storage full / no storage / FIFO on.
8. **Lights:**
   - Green when operational (idle or active); red when blocked; back to green on the next delivery.
   - No red/green flicker with FIFO off when some items move and others wait.
   - Off look (no light) when disconnected or unpowered.
   - Both sizes.
9. **Rot / deterioration (from Step C, not yet tested):** raw food in a buffer keeps rotting at the normal rate; compare with the same food on the floor and on a shelf. Note whether items deteriorate.
10. **Regression:** Step C transfer behaviour (targets, drops, FIFO, merge, slots, Unload, contents tab), Step B linking/labels/placement helper, save/load (power state recomputes after load), no hitch lines.

Report per item; log excerpts for failures; hitch lines; red errors.

## 10. Out of scope / later
- VFE checks (Step E); manifest and the orange blink in practice (Step F); on/off switch (decided against); probe removal; custom gizmo icons.
- **Lights-only overlay PNGs** (transparent except the lights, same canvas as the base): optional later if the sprites are redrawn. The code supports them unchanged, since it only draws whatever texture the extension names.
