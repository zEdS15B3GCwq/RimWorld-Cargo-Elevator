# CargoElevator – Step C implementation spec (handoff)

> **Status: implemented and tested** (see `results_stepC.md`). Kept as the original spec. Differences from the as-built state:
> - The rename goes through the vanilla `IRenameable` inspect-pane button (via an explicit `IStorageGroupMember` re-implementation); the Rename gizmo is gone.
> - `maxSlots` is 4.
> - The mode line was removed; the status line is "Transfer paused: … (reason)".
> - Floor drops top up partial stacks first.
> - Unload and contents-tab drops are vanilla `CompTransporter` behaviour.
> - Steps were renumbered afterwards: VFE checks are now Step E, manifest Step F.

Read `plan.md` (rev 7) first, especially §1 "Storage handling and requests", "Ticking, input, delivery", and §2 "Storage", "Hauling system", "Transporter loading". This file is self-contained for Step C. **Don't implement anything beyond it:** no VFE-specific code (Step D), no manifest/Load dialog (Step E), no Harmony, no mod settings, no probe removal, no pawn/animal sending.

**Goal of Step C:** the elevator becomes a working item elevator.
- It's a shelf-like `Building_Storage` whose cell is the intake. Pawns (and later conveyors) put items there.
- Items are pulled on the next tick into the single internal container, the vanilla `CompTransporter.innerContainer`.
- They are streamed to the partner at a limited rate and delivered into adjacent storage, held at the origin when that storage is full, or dropped on safe empty cells.
- Per-elevator gizmos control buffer slots, FIFO and stack merging.
- There's Unload and a contents tab.
- Every main step is logged.

**Done when:** `dotnet build` (Debug) gives 0 errors / 0 warnings; `CargoElevator\1.6\Assemblies\` contains only `CargoElevator.dll` (+ `.pdb`); the human test checklist (§9) can be run. The human tests in game; the agent only builds. **No git commits.** Backwards compatibility with Step B saves is not required (start a new game, or remove and rebuild elevators).

## 0. Environment / conventions
- Same as Step B: SDK-style `CargoElevator.csproj` (`net472`, LangVersion latest; references Assembly-CSharp, UnityEngine.CoreModule, UnityEngine.IMGUIModule). Build: `dotnet build CargoElevator.csproj`. Decompiler: `dotnet ilspycmd -t <Type> "C:\Games\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll"`, **run from the repo root**.
- Follow the existing code style (file-scoped namespace `CargoElevator`, XML doc comments, section comments). Log through `ElevatorUtil.Log` / `LogWarn`, using `LogId` for the elevator, `LevelLabels.LevelLabel` / `ElevatorUtil.MapDesc` for maps, and `thing.LabelCap` + `stackCount` for items.
- **Performance rule:** `Accepts` is called by the game during haul searches; keep it O(number of buffer entries) at most, with no allocations and **no logging** except the change-detected refusal log (§5.2). `Tick()` must exit early when idle.

## 1. Facts this step relies on (verified, see plan.md §2)

| Fact | Source |
| --- | --- |
| Haul target search: per slot group, highest priority first, only strictly better than the item's current priority. Pre-filter `slotGroup.Settings.AllowedToAccept(t)`, then per cell `IsGoodStoreCell` → `IsValidStorageFor` → `NoStorageBlockersIn` (room) + `slotGroup.parent.Accepts(t)` (interface call), cell reservable (**one pawn per cell**), reachable | `RimWorld.StoreUtility` |
| `Building_Storage.Accepts` is **not virtual**; re-implement the interfaces to override it | `RimWorld.Building_Storage` |
| A storage building needs `surfaceType Item`, otherwise `NoStorageBlockersIn` rejects its cell (non-standable building, `maxItemsInCell <= 1`). Vanilla shelves use `ShelfBase`: `surfaceType Item`, `ITab_Storage` | `StoreUtility.NoStorageBlockersIn`, Core `Buildings_Furniture.xml` |
| `Thing.SpawnSetup` calls `slotGroup.parent.Notify_ReceivedThing(this)` for items spawned on a storage cell; don't despawn inside that call | `Verse.Thing` |
| `CompTransporter.innerContainer` (public `ThingOwner`, a `ThingOwner<Thing>` owned by the comp; unlimited stacks; insertion order kept; saved by the comp). `CompTick` ticks the contents when `Props.shouldTickContents` (default true), and only runs when the parent's `Tick()` runs (`tickerType Normal`; `ThingWithComps.Tick` → `CompTick`). The building itself isn't an `IThingHolder`, so nothing else ticks the contents | `RimWorld.CompTransporter`, `Verse.TickManager.TickListFor` |
| `CompTransporter.PostDeSpawn` drops all contents `Near` the position unless `DestroyMode.WillReplace`. Map removal drops nothing (things are discarded with the map) | `RimWorld.CompTransporter` |
| Not loading → the comp's only gizmo is `Command_LoadToTransporter` ("Load"); it must be hidden in Step C | `RimWorld.CompTransporter.CompGetGizmosExtra` |
| `CompProperties_Transporter`: `massCapacity`, `restEffectiveness`, `max1PerGroup`, `canChangeAssignedThingsAfterStarting`, `showOverallStats`, `shouldTickContents`, `showMassInInspectString` | decompiled |
| `ITab_ContentsTransporter : ITab_ContentsBase`, `container => Transporter.innerContainer`, visible when non-empty or loading. Base `OnDropThing(Thing t, int count)` drops `Near` the building's own position (our intake cell), so override it | decompiled |
| `ThingOwner.Take(thing, count)`: with `count < stackCount` it splits and leaves the original in place (order kept). `TryAdd(item, canMergeWithExistingStacks)` appends (or merges into the first stackable entry when true, `respectStackLimit: false`) | `Verse.ThingOwner` |
| `GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode mode, out Thing lastResultingThing, Action<Thing,int> placedAction = null, Predicate<IntVec3> extraValidator = null, ...)`. `Direct` only uses that cell (merges into a stack there). `Near` searches outward, filtered by `extraValidator`. Never destroys anything. **`GenSpawn.Spawn` must not be used** for delivered items (its default `WipeMode.Vanish` destroys walls etc.) | `Verse.GenPlace`, `Verse.GenSpawn` |
| Live pawns/animals are never storable (`EverStorable` needs category Item) | `Verse.ThingDef` |

## 2. Def / data changes

### 2.1 `CargoElevator\Defs\ThingDefs\CargoElevator_Elevator.xml`
Change / add (keep everything else from Step B):
```xml
<tickerType>Normal</tickerType>
<surfaceType>Item</surfaceType>
<inspectorTabs>
  <li>ITab_Storage</li>
  <li>CargoElevator.ITab_ElevatorContents</li>
</inspectorTabs>
<building>
  <destroySound>BuildingDestroyed_Metal_Medium</destroySound>
  <maxItemsInCell>1</maxItemsInCell>
  <preventDeteriorationOnTop>true</preventDeteriorationOnTop>
  <ignoreStoredThingsBeauty>true</ignoreStoredThingsBeauty>
  <!-- no storageGroupTag: elevators must not be linked into shared storage groups -->
  <fixedStorageSettings>
    <filter>
      <disallowNotEverStorable>true</disallowNotEverStorable>
      <categories><li>Root</li></categories>
    </filter>
  </fixedStorageSettings>
  <defaultStorageSettings>
    <priority>Normal</priority>
    <filter>
    </filter>  <!-- nothing allowed by default; same pattern as VFE hoppers (empty filter) -->
  </defaultStorageSettings>
</building>
<comps>
  <li Class="CompProperties_Transporter">
    <massCapacity>100000</massCapacity>  <!-- mass is not our capacity measure; slots are -->
    <max1PerGroup>true</max1PerGroup>
    <canChangeAssignedThingsAfterStarting>true</canChangeAssignedThingsAfterStarting>
    <shouldTickContents>true</shouldTickContents>  <!-- buffered food keeps rotting -->
    <showMassInInspectString>false</showMassInInspectString>
  </li>
</comps>
<modExtensions>
  <li Class="CargoElevator.ElevatorSettingsExtension">
    <defaultSlots>4</defaultSlots>
    <minSlots>1</minSlots>
    <maxSlots>20</maxSlots>
    <ticksPerStack>60</ticksPerStack>     <!-- one piece per second -->
    <heldRetryTicks>-1</heldRetryTicks>   <!-- -1 = same as ticksPerStack -->
    <defaultFifo>true</defaultFifo>
    <defaultMerge>true</defaultMerge>
    <defaultAllowDrop>true</defaultAllowDrop>
  </li>
</modExtensions>
```
- The empty `<filter></filter>` matches VFE's hopper defs (`Buildings_Hoppers.xml`: `<priority>Low</priority><filter></filter>`), so it parses and allows nothing. Still check it in game (§9 item 1).
- Check that `ParentName="BuildingBase"` + `thingClass` stay unchanged. Verify in the startup diagnostic line (§8) that `tickerType=Normal`.
- `passability PassThroughOnly` stays (pawns walk around it). `surfaceType Item` is what makes the cell a valid storage cell.

### 2.2 New `Source\ElevatorSettingsExtension.cs`
```csharp
/// Tunables for the elevator, set in the ThingDef (no mod settings).
public class ElevatorSettingsExtension : DefModExtension
{
    public int defaultSlots = 4, minSlots = 1, maxSlots = 20;
    public int ticksPerStack = 60;
    public int heldRetryTicks = -1;          // -1 = ticksPerStack
    public bool defaultFifo = true, defaultMerge = true, defaultAllowDrop = true;
    public int HeldRetryTicks => heldRetryTicks > 0 ? heldRetryTicks : ticksPerStack;
}
```
`Building_Elevator.Settings => def.GetModExtension<ElevatorSettingsExtension>() ?? DefaultSettings` (static fallback instance, cached field).

### 2.3 Language keys (add to `CargoElevator.xml`)
```xml
<CargoElevator_BufferSlots>Buffer: {0} / {1} slots</CargoElevator_BufferSlots>
<CargoElevator_BufferMode>Order: {0}, stacks: {1}</CargoElevator_BufferMode>
<CargoElevator_ModeFifo>first in, first out</CargoElevator_ModeFifo>
<CargoElevator_ModeAnyOrder>first deliverable</CargoElevator_ModeAnyOrder>
<CargoElevator_MergeOn>merged</CargoElevator_MergeOn>
<CargoElevator_MergeOff>separate</CargoElevator_MergeOff>
<CargoElevator_Holding>Holding {0}: {1}</CargoElevator_Holding>
<CargoElevator_HoldTargetsFull>all accepting storage at the destination is full</CargoElevator_HoldTargetsFull>
<CargoElevator_HoldNoDropCell>no free cell to put it down at the destination</CargoElevator_HoldNoDropCell>
<CargoElevator_HoldNotConnected>not connected</CargoElevator_HoldNotConnected>
<CargoElevator_HoldDropDisabled>no storage accepts it and dropping on the floor is off</CargoElevator_HoldDropDisabled>
<CargoElevator_Unload>Unload</CargoElevator_Unload>
<CargoElevator_UnloadDesc>Put everything in this elevator's buffer down nearby. Pawns will haul it to storage.</CargoElevator_UnloadDesc>
<CargoElevator_UnloadIncomplete>{0}: no room nearby for {1} item stack(s); they stay in the elevator.</CargoElevator_UnloadIncomplete>
<CargoElevator_ToggleFifo>First in, first out</CargoElevator_ToggleFifo>
<CargoElevator_ToggleFifoDesc>On: the buffer is sent strictly in order; if the first entry can't be delivered, everything waits. Off: the first entry that can be delivered is sent.</CargoElevator_ToggleFifoDesc>
<CargoElevator_ToggleMerge>Merge stacks</CargoElevator_ToggleMerge>
<CargoElevator_ToggleMergeDesc>On: arriving items join any buffered stack of the same kind. Off: every arrival is kept as its own entry.</CargoElevator_ToggleMergeDesc>
<CargoElevator_ToggleDrop>Drop on floor</CargoElevator_ToggleDrop>
<CargoElevator_ToggleDropDesc>On: items that no storage next to the linked elevator accepts are put down on a free cell next to it. Off: such items wait in this elevator.</CargoElevator_ToggleDropDesc>
<CargoElevator_SlotsLess>Fewer slots</CargoElevator_SlotsLess>
<CargoElevator_SlotsMore>More slots</CargoElevator_SlotsMore>
<CargoElevator_SlotsDesc>Buffer size in slots. A slot holds one full stack. Currently {0}.</CargoElevator_SlotsDesc>
```

## 3. `Building_Elevator` becomes storage

```csharp
public class Building_Elevator : Building_Storage, IRenameable, ISlotGroupParent, IHaulDestination
```
- Re-listing `ISlotGroupParent` and `IHaulDestination` re-implements their members. Only `Accepts` gets a new implementation: `public new bool Accepts(Thing t)` (§5.2); every other member stays inherited from `Building_Storage`. Check with the compiler that interface calls bind to the new method: `((ISlotGroupParent)elevator).Accepts(x)` must hit ours. Optionally add a one-line DEV gizmo/log check.
- Everything from Step B stays: linking, lifecycle, labels, rename, Go to partner, Connect menu, placement helper, probes.
- Remove the `TickRare()` override (not called with `tickerType Normal`). `DoPeriodicWork()` is now called from `Tick()` (§4).
- Add `private CompTransporter transporter;` cached in `SpawnSetup` (`GetComp<CompTransporter>()`), and `public ThingOwner Buffer => transporter.innerContainer;` (null-safe: if the comp is missing, `LogWarn` once in `SpawnSetup` and treat the buffer as unavailable).
- Saved fields (`ExposeData`, after `base.ExposeData()`): `Scribe_Values.Look(ref maxSlots, "maxSlots", Settings.defaultSlots)`, `fifoMode` (`defaultFifo`), `mergeStacks` (`defaultMerge`), `allowDrop` (`defaultAllowDrop`).
- **The two modes are independent:** `mergeStacks` only governs how *input* is added to the buffer (§5.3); `fifoMode` only governs which entry is *sent* (§6.1). Initialise these from `Settings` in `PostMake()` (call `base.PostMake()` first; it creates the storage settings). The container itself is saved by `CompTransporter`.
- Runtime-only fields: `bool absorbPending; int nextSendTick; string holdReason; Thing heldThing;` plus the refusal-log state (§5.2).

## 4. Tick structure
```csharp
protected override void Tick()
{
    base.Tick();                                   // ticks comps → CompTransporter ticks contents (rot etc.)
    if (absorbPending) { absorbPending = false; AbsorbIntake("event"); }
    if (this.IsHashIntervalTick(PeriodicInterval)) DoPeriodicWork();     // 250: validation + AbsorbIntake("backstop")
    if (Buffer != null && Buffer.Count > 0 && Find.TickManager.TicksGame >= nextSendTick) TrySend();
}
```
- `DoPeriodicWork()`: the existing partner validation, then `AbsorbIntake("backstop")`. The backstop logs only if it actually absorbed something; that means the event path missed an item, which is worth knowing.
- `TrySend()` sets `nextSendTick = TicksGame + (sent ? Settings.ticksPerStack : Settings.HeldRetryTicks)`. When not connected, `nextSendTick = TicksGame + PeriodicInterval` (no need to retry faster).

## 5. Input

### 5.1 `Notify_ReceivedThing`
```csharp
public override void Notify_ReceivedThing(Thing newItem)
{
    base.Notify_ReceivedThing(newItem);
    ElevatorUtil.Log($"Received: {LogId} got {newItem.LabelCap} x{newItem.stackCount}");
    absorbPending = true;   // never despawn here: we are inside the item's SpawnSetup
}
```

### 5.2 `Accepts` (hot path)
```csharp
public new bool Accepts(Thing t)
{
    if (t is Pawn) return Refuse(t, "pawn");
    if (!IsConnected) return Refuse(t, "not connected");
    if (!GetStoreSettings().AllowedToAccept(t)) return false;        // filter: game pre-filters anyway; no log
    if (Buffer == null) return Refuse(t, "no buffer");
    if (CanMergeIntoBuffer(t)) return true;                          // partial slot of a mergeable entry has room
    if (UsedSlots() < maxSlots) return true;
    return Refuse(t, "buffer full");
}
```
- `UsedSlots()` = Σ over buffer entries of `ceil(stackCount / def.stackLimit)` (stack limit read at runtime). Pawns count 1 slot each (later).
- `CanMergeIntoBuffer(t)`: only when `mergeStacks` (independent of `fifoMode`). True if **any** entry `CanStackWith(t)` and has `entry.stackCount % entry.def.stackLimit != 0` (its last slot is partly empty).
- Room is checked for "at least something": the cell holds at most one stack (`maxItemsInCell 1`), and a slight overfill by an in-flight haul is accepted (§5.3).
- `Refuse(t, reason)`: returns false and logs `Rejected: {LogId} refuses {t.def.defName}: {reason}` **only when `(t.def, reason)` differs from the last logged pair** (two fields, no allocation in the repeat case).

### 5.3 `AbsorbIntake(string trigger)`
- For each cell in `AllSlotCellsList()` (one cell), copy the item things (`def.category == ThingCategory.Item`) from `cell.GetThingList(Map)` into a reused static list, then for each: if `ShouldAbsorb(thing)` → `thing.DeSpawn(); AddToBuffer(thing);`.
- **Unexpected items must be handled.** Items can land on the cell without going through `Accepts`: things dropped by pawns, explosions, other mods' spawning, a placement that slipped past our checks, items left from before a filter change. `ShouldAbsorb(t)` = `IsConnected && !(t is Pawn) && GetStoreSettings().AllowedToAccept(t)` (connected + the **filter**).
  - Not allowed or **disconnected** → **not absorbed**. It stays on the cell; the game treats it as misplaced (`IsInValidStorage` → our `Accepts`, which is false), so pawns haul it away like from any storage with the wrong item. Log once per thing (remember the last ignored thing id): `Ignored: {LogId} leaves {label} x{count} on the cell ({"not allowed by the filter" | "not connected"})`.
  - A disconnected elevator takes nothing in. Items already in its buffer stay there and can be unloaded (§7.2). The future Load (manifest) gizmo must also be disabled while disconnected (Step E).
  - Allowed and connected → absorbed even if the buffer is full: an in-flight haul may slightly overfill. `Accepts` keeps new hauls away.
- `AddToBuffer(Thing t)` (shared later by other sources):
  - `mergeStacks`: `Buffer.TryAdd(t, canMergeWithExistingStacks: true)`. `ThingOwner` merges into the first stackable entry, wherever it is (`respectStackLimit: false`); otherwise it appends. An entry still waiting may grow, which is intended.
  - `!mergeStacks`: `Buffer.TryAdd(t, canMergeWithExistingStacks: false)` (always appends).
  - `fifoMode` plays no part here.
  - If `TryAdd` fails (shouldn't happen), put the thing down with `TryDropNear` (§7.1) and `LogWarn`.
- Log: `Absorbed ({trigger}): {LogId} {label} x{count} → entry #{index} ({"merged" | "new"}); slots {used}/{max}`.

## 6. Output (`TrySend`)

### 6.1 Entry selection
- If not `IsValidPartner(out reason)` → set hold reason "not connected", schedule, return (no log spam: log only when the hold reason changes; same change-detection as `Refuse`).
- This is the only place `fifoMode` matters. `fifoMode`: consider only entry 0. `!fifoMode`: consider entries in order; send the first for which a delivery plan exists (stop after one successful send per call).
- Pawn entries (not expected in Step C): `LogWarn` once per pawn and treat them as undeliverable. In FIFO mode that blocks; acceptable, since there is no pawn input in Step C.

### 6.2 Delivery plan for an entry `e` (evaluated before anything leaves the buffer)
- `pieceCount = Min(e.stackCount, e.def.stackLimit)`.
- **Output cells = the ring of cells adjacent to the partner** (`partner.OccupiedRect().ExpandedBy(1)` minus the rect, in bounds, fixed order). **Only these cells are used, for delivery and for drops.** A big adjacent stockpile or a long shelf is used only where it touches the partner; its far cells are never targeted.
  - Rationale: vanilla haulers pick one specific, reachable, reserved cell (`TryFindBestBetterStoreCellForWorker` picks the cell closest to the item) and walk there. Nothing is "put into a storage" as a whole, so an elevator reaching into a far cell would be a teleport.
  - Consequence: the output capacity is the touching storage cells (up to 8 for a 1x1 elevator). Items move on when pawns haul them to better storage (players give the output storage a low priority), as with VFE hoppers.
  - **Decided (user):** never use non-adjacent cells of an adjacent storage.
- **Candidates:** distinct `ISlotGroupParent`s among the output cells (`cell.GetSlotGroup(partnerMap)?.parent`), excluding the partner itself. This includes stockpile zones, shelves and other elevators; stockpiles and storage buildings are handled the same way. Order: storage priority descending (`GetStoreSettings().Priority`), ties by first-seen cell order. A candidate is **accepting** if `candidate.Accepts(e)` (interface call; for another elevator that's its own rules).
- **Room per accepting candidate:** over **its output cells only**, for cells where `cell.IsValidStorageFor(partnerMap, e)`:
  - `room += free item slots in cell * stackLimit + Σ (stackLimit - stackCount) of stackable same-kind stacks in the cell`;
  - free item slots = `cell.GetMaxItemsAllowedInCell(map) - cell.GetItemCount(map)`, clamped ≥ 0.
  - Stop summing once ≥ `pieceCount`.
- **Case A, some accepting candidate has room:** deliver `min(pieceCount, total room)` by walking the candidates in order and their output cells. Per cell, take `n = min(remaining, roomInCell)`: `portion = Buffer.Take(e, n)`; `portion.SetForbidden(false, warnOnFail: false)` if forbiddable; `GenPlace.TryPlaceThing(portion, cell, partnerMap, ThingPlaceMode.Direct, out _)`.
  - **Defensive only:** if placement fails, or leaves a remainder (`!portion.Destroyed && !portion.Spawned`), put the remainder back and `LogWarn`.
    - This shouldn't happen: the room was computed from the same cells a moment earlier, in the same tick.
    - Possible causes: another mod changing cell capacity or placement rules (deep-storage mods, Harmony patches on `GenPlace` / `IsValidStorageFor`), a destination whose `Accepts` changes as items arrive, or a bug in our room calculation.
    - If the entry was only partly taken, merge the remainder back into it (`entry.TryAbsorbStack(remainder, respectStackLimit: false)`); its position doesn't change.
    - If the whole entry was taken, put it back with `Buffer.TryAdd(remainder, canMergeWithExistingStacks: mergeStacks)`. It lands at the end (or in another same-kind entry), so **the delivery order changes**. That's accepted (decision); log it: `Order changed: {LogId} {label} x{n} returned to the buffer at #{newIndex} (was #{oldIndex}) after a failed delivery`. No reordering hacks (§6.3).
  - Log per placement: `Delivered: {LogId} → {partner.LogId}: {label} x{n} into {candidate label / "stockpile"} at {cell}; remaining in entry {e.stackCount or 0}`.
  - Partial delivery is normal: the remainder stays at the same buffer position.
- **Case B, accepting candidates exist but none has room:** hold (`HoldTargetsFull`). Log on change: `Held: {LogId} {label}: all {k} accepting storages full`.
- **Case C, no accepting candidate:** if `!allowDrop` (the sender's setting) → hold (`HoldDropDisabled`). Otherwise drop onto **adjacent drop cells** (§7.1) with `ThingPlaceMode.Direct`:
  - Walk the output cells that are **not** storage cells (no slot group). Storage cells are never drop targets: whatever can act as storage is handled as a target (Case A/B) and must accept the item first, and in Case C none does.
  - Place `n = min(remaining, DeliveryDropRoom(cell))` per cell, until the piece is placed or the cells run out. A partial drop is fine; the rest stays in the entry.
  - No room anywhere → hold (`HoldNoDropCell`).
  - Log `Dropped: {LogId} → {partner.LogId}: {label} x{n} at {cell}` or `Held: … no drop cell`.
  - The defensive failure handling is the same as in Case A.
- Before choosing, log the candidate list once per send attempt **only when it differs from the previous attempt's list** (`Targets: {LogId} for {label}: [label(priority, accepts=yes/no, room=n), …]`), to keep logs readable with a 1-second send rate.
- On a successful send, clear `holdReason` / `heldThing`.

### 6.3 Buffer order: what the container allows
- **The container (`ThingOwner<Thing>`) supports:**
  - add: `TryAdd(thing, merge)` (appends, or merges into the *first* stackable entry), `TryAdd(thing, count, merge)`, `TryAddOrTransfer`, `TryAddRangeOrTransfer`, `TryTransferToContainer`;
  - remove: `Take(thing[, count])` (partial take splits and leaves the entry in place), `Remove`, `RemoveAll(predicate)`;
  - drop: `TryDrop…`, `TryDropAll`;
  - read: `Count`, indexer, `IndexOf`, `Contains`, `GetCountCanAccept`, `InnerListForReading`.
- **No insert-at-index, move or sort.** Adds append (or merge into the first stackable entry). Every add and remove maintains each thing's `holdingOwner` and calls the owner's notifications. Zero-count placeholders are impossible: `TryAdd` refuses `stackCount <= 0`, and `stackCount 0` marks a destroyed thing.
- **Decision:** the buffer order is whatever the container gives. A failed delivery of a whole entry may move it to the end. That's accepted and logged (`Order changed`, §6.2).
  - **Don't** reorder by removing and re-inserting entries.
  - **Don't** manipulate `InnerListForReading`.
- A separate order list (e.g. `List<Thing>` saved with `Scribe_References`, reconciled against the container every tick) would be possible if strict order is ever needed. Not implemented.

### 6.4 Rate
One piece per `ticksPerStack` per elevator (a piece is at most one full stack). A held attempt retries after `HeldRetryTicks` (default = `ticksPerStack`).

## 7. Dropping, Unload, contents tab, despawn

### 7.1 Drop cell rules (`ElevatorUtil`)
**Delivery drops (Case C)** use only the partner's **output cells** (the adjacent ring, §6.2) that are **not storage cells**. Storage of any kind (stockpile zone, shelf, elevator) is only ever used as a target that accepts the item (Case A).

`int DeliveryDropRoom(IntVec3 c, Map map, Thing piece)`, for how much of `piece` this cell can take:
- 0 if: out of bounds; fogged; a storage cell (`c.GetSlotGroup(map) != null`); not standable; any edifice (walls, doors, other buildings); a blueprint/frame on the cell.
- Otherwise: `stackLimit` if the cell has no items; `stackLimit - stackCount` if its only item is a same-kind partial stack (`CanStackWith`; e.g. an earlier drop), which gets topped up; else 0.
- A partial stack lying in a storage cell (stockpile, shelf, elevator) is never topped up by a drop: the storage cell rule above already gives 0. If that storage accepts the item, it was handled as a target in Case A.
- Placement only through `GenPlace.TryPlaceThing(…, Direct)`, which never destroys anything; `GenSpawn.Spawn` is never used.

**Unload and contents-tab drops** use vanilla-like `Near` placement with `UnloadValidator` (§7.2). `ElevatorUtil.TryDropNear(Thing thing, IntVec3 center, Map map)` = `GenPlace.TryPlaceThing(thing, center, map, ThingPlaceMode.Near, out _, null, c => UnloadValidator(c, map, thing))`. On failure it returns false and the caller keeps the thing in the buffer. The absorb fallback (§5.3) uses it too.

### 7.2 Unload gizmo (`Command_Action`, `TexButton.Drop` or a vanilla placeholder icon)
Disabled when the buffer is empty (`Disable("…")`). It works while disconnected. It works **like vanilla unloading**:
- Vanilla transporter unload/cancel and despawn use `innerContainer.TryDropAll(parent.Position, map, ThingPlaceMode.Near)`.
- Vanilla shuttle unload uses `innerContainer.TryDrop(thing, interactionCell, map, ThingPlaceMode.Near, …, validator: not fogged, and a downed pawn not onto a cell with a pawn)`.
- `Near` searches outward up to radius ~12.9 (`GenPlace.PlaceNearMaxRadialCells`), may stack onto same-kind items, and never destroys anything.

Implementation: for each entry, from the back, as `TryDropAll` does: `Buffer.TryDrop(e, Position, Map, ThingPlaceMode.Near, out _, null, UnloadValidator)`.
- `UnloadValidator(c, map, thing)` = `!c.Fogged(map)`; never a cell of any `Building_Elevator` (it would absorb and send the item); and if `c` is a storage cell (stockpile zone or shelf), only if that storage accepts the item (`c.GetSlotGroup(map).parent.Accepts(thing)`). That's the same principle as for delivery: storage is only used when it would take the item. Plain floor and stacking onto same-kind items are allowed, as in vanilla. Unlike delivery drops (§7.1), it isn't limited to adjacent cells.
- `TryDrop` splits stacks above `def.stackLimit` itself (it places stack by stack).
- Afterwards, if the buffer still has entries: `Messages.Message("CargoElevator_UnloadIncomplete".Translate(LabelCap, Buffer.Count), this, MessageTypeDefOf.RejectInput, historical: false)` + `LogWarn`.
- Log `Unloaded: {LogId} {placed} entr(y/ies) dropped, {left} left`.

### 7.3 New `Source\ITab_ElevatorContents.cs`
```csharp
public class ITab_ElevatorContents : ITab_ContentsTransporter
{
    protected override void OnDropThing(Thing t, int count)
    {
        // Vanilla drops "near" our own position, which starts at the intake cell (it would be absorbed again).
        var elevator = (Building_Elevator)SelThing;
        elevator.DropFromBuffer(t, count, "contents tab");
    }
}
```
`Building_Elevator.DropFromBuffer(Thing t, int count, string source)`: `Take(t, count)` → `TryDropNear`; on failure put it back. Log `Dropped from buffer ({source}): …`.

### 7.4 Despawn / destroy
`CompTransporter.PostDeSpawn` already drops all contents `Near` the position. In `OnLifecycleEnd` (or `DeSpawn` before `base`), log `Despawn drop: {LogId} buffer has {n} entries (vanilla drops them)`. Map removal: the contents are discarded with the map (log the entry count in `Notify_MyMapRemoved`).

## 8. Gizmos and inspect

`GetGizmos()`:
- Iterate `base.GetGizmos()` and **skip `Command_LoadToTransporter`** (manifest is Step E). Keep the `Building_Storage` gizmos (copy/paste settings) and the Step B gizmos.
- Add:
  - **Unload** (§7.2).
  - **FIFO toggle:** `Command_Toggle`, `isActive = () => fifoMode`, `toggleAction` flips and logs `Mode: {LogId} fifo={…}`.
  - **Merge toggle:** same pattern. Only affects how *new* input is added; existing entries are not re-merged or split when toggled.
  - **Drop-on-floor toggle:** same pattern (`allowDrop`).
  - **Fewer slots / More slots:** two `Command_Action`, disabled at `minSlots` / `maxSlots`. Lowering below `UsedSlots()` is allowed: the elevator simply stops accepting until it drains. Logs `Slots: {LogId} {old} → {new}`.
  - Icons: vanilla placeholders (e.g. `TexButton.Drop`, `TexCommand.ForbidOff`-style). Final icons are on the to-do-later list.
  - **DEV: log buffer state:** entries with index, label, count, slots; used/max; mode; nextSendTick; hold reason; the current candidate list for the head entry.

`GetInspectString()`: base (it includes the `CompTransporter` "Contents: …" line and the `Building_Storage` text) + the Step B link lines + `CargoElevator_BufferSlots` (used, max) + `CargoElevator_BufferMode` + `CargoElevator_Holding` when `holdReason != null`. All cheap: one pass over the entries.

`ElevatorDiagnostics` startup line: also print `surfaceType`, `maxItemsInCell`, whether the def has `CompProperties_Transporter` and the extension values.

## 9. Human test checklist (Dev mode, watch the log)
Set up a linked pair (any levels). Configure the sender's storage tab: allow e.g. steel, wood, corn; priority above the colony's main stockpile for the pawn tests.
1. **Defaults:** a new elevator allows nothing (empty filter) and pawns bring nothing. The storage tab works. The inspect string shows "Buffer: 0 / 4 slots", the mode line, the contents line. No "Load" gizmo.
2. **Pawn input (role 1):** allow steel, set priority Important. Pawns haul steel onto the elevator; the log shows `Received`, then `Absorbed (event)` on the next tick; the cell stays empty; `Delivered` lines at about 1 piece per second. No `Absorbed (backstop)` lines in normal operation.
3. **Disconnected:** unlink (deconstruct the partner) → `Rejected … not connected` once. Pawns stop bringing items; buffered items stay; Unload works. An allowed item placed on the cell (Dev spawn) is **not** absorbed (`Ignored … not connected`) and gets hauled away.
4. **Delivery, one storage:**
   - A shelf next to the receiver that allows the item → items land in the shelf; partial fills when it has little room; `Held … all 1 accepting storages full` when full; delivery resumes when space frees.
   - Same with a stockpile zone touching the receiver.
   - A large stockpile or long shelf: only its cells touching the receiver are filled, never far cells.
5. **Delivery, several storages:** two adjacent storages accepting the same item, with different priorities → the higher one fills first, then the other. Hold only when both are full.
5b. **Unexpected items on the intake:**
   - Drop or spawn (Dev tools) an item the sender's filter doesn't allow onto its cell → not absorbed (`Ignored` logged once); pawns haul it away.
   - An allowed item placed the same way → absorbed and sent.
6. **No accepting storage:**
   - Items are dropped next to the receiver only, on plain floor: empty cells or same-kind partial stacks. Never into adjacent stockpiles/shelves/elevators that don't accept them, never on walls, doors, other buildings or blueprints. Nothing is destroyed.
   - Surround the receiver with walls and full cells → `Held … no drop cell`.
   - Drop-on-floor off (sender) → `Held … dropping on the floor is off`; with FIFO on this blocks the queue.
6b. **Unload:**
   - Items are put down around the sender like a vanilla shuttle unload (nearby cells, may stack onto same-kind items). It goes into a stockpile or shelf only if that storage accepts the item, never onto an elevator. Nothing is destroyed.
   - When there's no room within the vanilla search radius, the "no room nearby" message appears and the rest stays inside.
7. **Neighbouring elevator as target:** an elevator (with its own partner) next to the receiver accepts → items pass on to the third level.
8. **FIFO on:** buffer corn then rice, with only rice accepted at the destination and corn's storage full → nothing is sent (head blocked); the inspect string shows the hold reason; it resumes when corn fits.
9. **FIFO off:** same setup → rice is delivered past the blocked corn.
10. **Merge (independent of FIFO):**
    - Merge on: corn, rice, corn → 2 entries (the second corn joins the first, in both FIFO modes); a waiting corn entry keeps growing.
    - Merge off: corn, rice, corn → 3 entries.
    - Toggling merge doesn't change existing entries.
11. **Slots:** 1 slot + merge: corn accepted up to a full stack, rice refused (`Rejected … buffer full`) until the corn is sent. ± gizmos respect the bounds; lowering below used stops intake only.
12. **Stack limits:** with a stack-size mod or a large merged entry (e.g. 300 steel with stackLimit 75), each delivery piece is ≤ 75 and the remainder stays at the head.
13. **Contents tab:** shows the buffer; dropping an item puts it next to the elevator (not on it) and it isn't re-absorbed.
14. **Rot:** buffered raw food keeps rotting (compare the rot progress before and after a while).
15. **Destroy / deconstruct the sender with a full buffer:** contents are dropped near it (vanilla) and the log shows the count. **Remove the sender's map** (Dev / DeepRim abandon): contents are gone, partner unlinked.
16. **Save/load:** with a mixed buffer (several entries, FIFO off, merge off, 7 slots) → order, counts and settings survive; sending continues.
17. **Regression:** the Step B linking/labels/placement helper still work; no hitch lines while placing; no `Validation:` warnings.

Report per item; log excerpts for failures; hitch lines; red errors.

## 10. Out of scope
VFE conveyor checks (Step D). Manifest / Load dialog / pawns / animals (Step E). Harmony. 2x2 building (to decide later). Custom icons. Removing probes. Prompting haulers when accepting resumes (to-do later).
