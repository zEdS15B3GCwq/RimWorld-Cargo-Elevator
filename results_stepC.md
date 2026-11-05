# CargoElevator – Step C results

Spec: `plan_stepC.md`. Overall: **step concluded.** The elevator is a working item elevator: items go into a buffer, are streamed to the linked partner and are delivered to adjacent storage, held, or dropped on the adjacent floor. Three rounds of in-game testing by the human led to the changes in section 4. The final wording of the status line ("FIFO on") was set by the human directly in the files. Build: `dotnet build CargoElevator.csproj` (Debug) gives 0 errors / 0 warnings; `CargoElevator/1.6/Assemblies/` contains only `CargoElevator.dll` + `.pdb`. No git commits were made by the agent.

## 1. What was implemented

- **Def** (`CargoElevator_Elevator.xml`): `tickerType Normal`, `surfaceType Item`, inspector tabs `ITab_Storage` + `CargoElevator.ITab_ElevatorContents`, building storage settings (`maxItemsInCell` 1, empty default filter, no `storageGroupTag`), `CompProperties_Transporter` (the buffer), `ElevatorSettingsExtension`.
- **`ElevatorSettingsExtension`** (new): `defaultSlots` 4, `minSlots` 1, `maxSlots` 4, `ticksPerStack` 60, `heldRetryTicks` -1 (= `ticksPerStack`), `defaultFifo` / `defaultMerge` / `defaultAllowDrop` true. `Building_Elevator.Settings` falls back to a static default instance.
- **`Building_Elevator`** is `partial` and derives from `Building_Storage`; it re-lists `ISlotGroupParent, IHaulDestination` so interface calls reach the `new Accepts`:
  - `Building_Elevator.cs`: link, lifecycle, `Tick` / `DoPeriodicWork` (absorb pending → periodic partner validation and backstop every 250 ticks → `TrySend` when the buffer is non-empty and `nextSendTick` is reached), gizmo and inspect composition (the Load gizmo is filtered out), explicit `IStorageGroupMember` members, DEV "log buffer state" gizmo.
  - `Building_Elevator.Buffer.cs`: settings fields, `Accepts` / `Refuse`, `Notify_ReceivedThing`, `AbsorbIntake`, `AddToBuffer`, slots, Unload, `DropFromBuffer`, `ReturnPortionToBuffer`, buffer gizmos, inspect lines, DEV buffer dump.
  - `Building_Elevator.Send.cs`: `TrySend`, entry selection (FIFO considers only the head; otherwise the first deliverable), delivery planning (`DeliveryTarget`), delivery to storage and floor drop, hold state (`HoldReason`).
- **`ElevatorUtil`**: `OutputCells`, `StorageCellRoom`, `DeliveryDropRoom`, `UnloadValidator`, `TryDropNear`, `Describe`.
- **`ITab_ElevatorContents`** (new): derives from `ITab_ContentsTransporter`; drops go through `DropFromBuffer`.
- **`ElevatorDiagnostics`**: the startup line also prints surfaceType, maxItemsInCell, transporter-comp presence and the extension values.
- **Language keys** (`CargoElevator.xml`): buffer, paused-status, Unload, toggle and slot keys.

### How it works (final behaviour)

- **Intake:** the elevator cell is the intake. `Accepts` is O(1) and allocation-free (Step A rule); refusals are logged with change detection. Arriving items are absorbed into the buffer (`CompTransporter.innerContainer`); a periodic backstop absorbs anything missed.
- **Buffer:** capacity is counted in stacks ("slots"). With *merge* on, arrivals join a buffered stack of the same kind; with merge off each arrival needs its own slot.
- **Sending:** one piece per `ticksPerStack` (60 ticks = 1/s), to the partner. *FIFO* on: only the head entry is tried, everything waits behind it. FIFO off: the first deliverable entry is sent. FIFO is independent of merge and of drop-on-floor.
- **Delivery targets:** storage buildings in the ring adjacent to the receiving elevator, highest priority first (stable order), only those whose settings accept the item. If none accepts and *drop on floor* is on, the item is placed on a free adjacent cell: first onto cells that hold a partial stack of the same kind, then onto empty cells. The item is held (not lost) when there is no storage, the storage is full, or the elevator is not connected.
- **Unload / contents tab:** vanilla `CompTransporter` behaviour, left as is (see section 5).

### Deviations from the spec

| Change | Reason |
| --- | --- |
| `HoldUndeliverable` language key dropped again | The pawn message was removed (4.3). Pawn entries still have an internal `HoldReason.Undeliverable`, which is not shown. |
| `CargoElevator_UnloadEmpty` key added | Tooltip of the disabled Unload gizmo. |
| `IStorageGroupMember` re-implemented explicitly | See 3.1. |
| `Dialog_RenameElevator` and the Rename gizmo removed | The vanilla `IRenameable` button in the inspect pane replaces them (3.1). |

## 2. Implementation notes / small decisions

- `Buffer` and the transporter comp are cached in `SpawnSetup`; a missing comp logs a warning once and the elevator refuses everything.
- `Accepts` does not allocate; its refusal log compares `(def, reason)` with the last logged pair.
- Hold state is a list of `(Thing, HoldReason)` for every buffer entry plus change-detection fields (`lastLoggedHoldDef`, `lastLoggedHoldReason`). The `Held:` log line follows the first held entry only and carries the exact cause; the player sees only the short reason. Clearing the hold resets the detection, so a later identical hold logs again. Target and refusal logs use change detection as well.
- Entries that were not evaluated on the last attempt (everything behind the head with FIFO on) get `HoldReason.FifoWaiting`, except when the head is held because the elevator is not connected: then all entries get `NotConnected`.
- Failed placement (not expected) goes through `ReturnPortionToBuffer`: a partial take is merged back into its entry; a whole-entry take is re-added (may land at the end, logged as `Order changed`). The same helper is used when a contents-tab drop finds no room.
- Delivery and unload use `GenPlace.TryPlaceThing` (Direct for delivery, Near for unload); `GenSpawn.Spawn` is never used.
- Slot buttons are disabled at the min/max bound; lowering below the used slots is allowed. A saved capacity above the def maximum is kept, but "Fewer slots" brings it down to the maximum (4.3). Old saves showing e.g. 19 slots are the stored value loading unchanged; new elevators start at 4.
- Gizmo icons are vanilla placeholders: Unload `TexButton.Drop`, FIFO `TexCommand.HoldOpen`, merge `TexCommand.GatherSpotActive`, drop `TexCommand.ForbidOff`, slots `TexButton.Minus/Plus`.
- `PeriodicInterval` still equals the rare tick interval (250), now driven from `Tick` (`TickRare` removed).

## 3. Test results (human, in game)

### Round 1 (full checklist, `plan_stepC.md` §9)

The first report covered basics, adding items, merging, unloading, slots, filters, the drop-on-floor setting, transport, FIFO, save/load, destruction and pawn hauling. Findings that needed action are listed in 4.1; everything else in the report needed no change.

| Area | Finding | Outcome |
| --- | --- | --- |
| Rename | The rename button in the inspect pane created a storage group ("name already in use" error); renaming did nothing. | Fixed (3.1, 4.1). Confirmed in round 2. |
| Storage-group gizmos (link / unlink / select) | Visible on the elevator. | Left as the game provides them (decision, 4.1). Confirmed present in round 2. |
| Dev-spawned items land beside the elevator | Happens when its filter or connection refuses them. | Not a bug: vanilla `GenPlace` Near placement avoids storage cells that do not accept the item. |
| Refusal log repeats | A repeated refusal is not logged again. | By design (change detection). |
| `Received x1` for a stack spawned by the dev tool | The tool spawns one item and raises the stack afterwards. | Not a bug; the count was removed from the line anyway (4.1). |
| Merge off rejects same-kind items | Each arrival needs its own slot. | By design. |
| Max slots showed 20 / "slots" wording | | Max slots 4; wording "stacks" (4.1). |
| Hold message "nothing can be delivered right now" | Says nothing about the cause. | Reworded; see 4.1–4.3. |
| Contents tab: "x" and partial "x" icons, "To load" column | Provided by the game's tab. | Left as is; the "To load" column stays for the future manifest (Step E). |
| Unload tops up partial stacks / destroys items in a walled-in spot | Game-provided `CompTransporter` routine. | Left as vanilla (decision). |

### Round 2

| Scenario | Result |
| --- | --- |
| Rename icon in the description pane | Works as expected; the Rename gizmo is gone. |
| Storage-group gizmos | Present. |
| Mode line (order / stacks / drop) | Removed as requested (4.2). |
| "Empty. (x / 1 stacks)" line above "Connected to" | Not produced by our code or by `Assembly-CSharp` (searched with ilspycmd); presumably another mod. Left alone (decision). |

### Round 3

| Scenario | Result |
| --- | --- |
| Capacity above the def maximum | The "Fewer slots" gizmo stays active and sets it to the maximum (requested change; implemented). |
| Paused-status wording and grouping | Specified by the human; implemented in 4.3 and 4.4. |

### Not re-tested in game

- The final paused-status line (grouping by reason, the "FIFO on" reason, the "not connected" case) and the reduce-to-maximum slot behaviour were implemented and built after the last reported test; a re-test was not reported. The wording is the human's own (including the final "FIFO on" edit).
- No `Absorbed (backstop)` lines were reported in normal operation, and no hitches.

## 4. Changes made after testing (all implemented, build 0 errors / 0 warnings)

### 4.1 After round 1
- Rename: `Building_Elevator` re-implements `IStorageGroupMember` with `ShowRenameButton => false` (and `DrawConnectionOverlay => false`), so the inspect pane shows the normal `IRenameable` button. Rename gizmo and `Dialog_RenameElevator` deleted. The vanilla link/unlink/select-linked gizmos are left alone (an earlier attempt to hide them was reverted on request).
- Max slots 4; the buffer line says "stacks".
- Floor drops at the target top up same-kind partial stacks first (pass 1), then use empty cells (pass 2). This is our own placement, not a game routine.
- The `Received` log no longer prints a count; `Targets` and `Held` logs name the receiver (`{LogId} → {partner.LogId}`).
- Hold wording for delivery-blocked cases replaced the placeholder (listing variants "storage full", "no storage accepts", "no storage or empty cell", then simplified).

### 4.2 After the second report
- Inspect: the order/stacks/drop mode line was removed; unused keys (`BufferMode`, `ModeFifo`, `ModeAnyOrder`, `MergeOn`, `MergeOff`) deleted.

### 4.3 Slots and paused-status line
- Slot gizmos: a saved capacity above the def maximum can still be reduced; "Fewer slots" then sets it to the maximum. "More slots" is disabled at or above the maximum.
- Status line replaced by **"Transfer paused: <held stacks> (<reason>); …."** with grouped stacks. Reasons:
  - *storage full*: storage that accepts the item exists next to the destination but has no room.
  - *no storage*: nothing accepts the item, or drop is off, or drop is on but no free cell exists.
  - *not connected*: the elevator has no valid partner.
- The pawn message was dropped (to be added back if pawn sending is ever supported). Old `Hold*` keys deleted.

### 4.4 FIFO reason
- With FIFO on, stacks that are not first in the queue are listed under their own reason instead of sharing the head's. The human set the text to **"FIFO on"** (key `CargoElevator_PausedFifoWaiting`; it tells the player what to switch off). Example: `Transfer paused: Corn x50 (storage full); Rice x20, Steel x75 (FIFO on).` Not-connected still applies to all entries.

## 5. Decisions

- Do not spend effort changing game-provided behaviour: `CompTransporter` unload (topping up partial stacks, items lost when placed in a walled-in spot), the contents tab's icons, and text not produced by our code (e.g. "Empty. (x / 1 stacks)") stay as they are.
- Vanilla storage-group gizmos stay visible.
- Transfer rate of 1 piece per second is kept (not verified by a timed test; the human was unsure and asked for no test).
- The "To load" column of the contents tab stays; it will show what remains to be loaded for manifests.
- Sprite variants are deferred to a later step.

## 6. Open items and notes for later steps

- **Sprite variants (not implemented):** lights around a corner or a single light on the elevator; off when inactive, green when connected and not held, red when items are held. The textures `CargoElevator_green/orange/red` (and `_2x2`) are already in the working tree but unused.
- **Pawn sending:** not supported; `HoldReason.Undeliverable` exists for such entries and is not shown.
- **Manifest / Load dialog (Step E):** will use the contents tab's "To load" column.
- **VFE conveyor checks (Step D)** and **mod settings**: not started.
- The probes from Step A (`ElevatorProfiler`, hitch detector) are still present; removal is still pending (decision from Step B).
- Logging stays unconditional (not gated on `Prefs.DevMode`).
