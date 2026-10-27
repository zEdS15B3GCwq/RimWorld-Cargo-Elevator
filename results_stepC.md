# Step C results

## Status
- `dotnet build DeepElevator.csproj` (Debug): 0 errors, 0 warnings.
- `DeepElevator\1.6\Assemblies\` contains only `DeepElevator.dll` and `DeepElevator.pdb`.
- **Not tested in game.** The human checklist (plan_stepC.md §9) is still to be run. No git commits were made.

## What was implemented
- **Def** (`DeepElevator_Elevator.xml`): `tickerType Normal`, `surfaceType Item`, `ITab_Storage` + `DeepElevator.ITab_ElevatorContents`, storage building settings (empty default filter, no `storageGroupTag`), `CompProperties_Transporter`, `ElevatorSettingsExtension`. Language keys added to `DeepElevator.xml` (one extra key beyond the spec: `DeepElevator_HoldUndeliverable`, plus `DeepElevator_UnloadEmpty` for the disabled Unload tooltip).
- **`ElevatorSettingsExtension`**: the tunables as specified; `Building_Elevator.Settings` falls back to a static default instance.
- **`Building_Elevator`** is now `partial` and derives from `Building_Storage`, re-listing `ISlotGroupParent, IHaulDestination` so interface calls reach the `new Accepts`:
  - `Building_Elevator.cs`: link, lifecycle, `Tick`/`DoPeriodicWork`, gizmo and inspect composition (the Load gizmo is filtered out). `TickRare` removed.
  - `Building_Elevator.Buffer.cs`: settings fields, `Accepts`/`Refuse`, `Notify_ReceivedThing`, `AbsorbIntake`, `AddToBuffer`, slots, Unload, `DropFromBuffer`, `ReturnPortionToBuffer`, buffer gizmos, inspect lines, DEV buffer dump.
  - `Building_Elevator.Send.cs`: `TrySend`, entry selection (FIFO or first deliverable), delivery planning (`DeliveryTarget`), Case A/B/C, hold state.
- **`ElevatorUtil`**: `OutputCells`, `StorageCellRoom`, `DeliveryDropRoom`, `UnloadValidator`, `TryDropNear`, `Describe`.
- **`ITab_ElevatorContents`**: drops go through `DropFromBuffer`.
- **`ElevatorDiagnostics`**: startup line now also prints surfaceType, maxItemsInCell, transporter comp presence and the extension values.

## Implementation notes / small decisions
- `Buffer` and the transporter comp are cached in `SpawnSetup`; a missing comp logs a warning once and the elevator refuses everything.
- Hold state keeps `holdReasonKey` (a translation key) plus `heldThing`; the log text for a hold is separate English text (`Held: … <detail>`).
- Hold and target logs use change detection (`(def, key)` pair; target signature string). Clearing the hold resets the detection, so a later identical hold logs again.
- `Accepts` does not allocate; its refusal log compares `(def, reason)` with the last logged pair.
- Failed placement (not expected) goes through `ReturnPortionToBuffer`: a partial take is merged back into its entry; a whole-entry take is re-added (may land at the end, logged as `Order changed`). The same helper is used when a contents-tab drop finds no room.
- Slot buttons are disabled at the min/max bound; lowering below the used slots is allowed.
- Gizmo icons are vanilla placeholders: Unload `TexButton.Drop`, FIFO `TexCommand.HoldOpen`, merge `TexCommand.GatherSpotActive`, drop `TexCommand.ForbidOff`, slots `TexButton.Minus/Plus`.
- `PeriodicInterval` still equals the rare tick interval (250), now driven from `Tick`.

## Test results
Build only. In-game tests (§9 items 1–17) have not been run; open points to check first:
1. The empty `<filter></filter>` default parses and allows nothing (§9 item 1).
2. Interface calls from haul searches reach our `Accepts` (look for `Rejected:` log lines on a disconnected elevator, and no hauling to it).
3. No `Absorbed (backstop)` lines in normal operation.
