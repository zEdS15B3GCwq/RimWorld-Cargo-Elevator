# DeepElevator – plan (rev 4)

Target: RimWorld 1.6 only (Assembly-CSharp 1.6.4871). Hard dependency: DeepRim (Mlie, `Mlie.DeepRim`, Steam 2164179860). Soft interaction: Vanilla Furniture Expanded - Factory (VFE) conveyors; no code dependency on it.

Rev 4 changes: reviewed against DeepRim source, VFE source and the decompiled 1.6 assembly (ilspycmd). The facts in §2 are verified in code, not assumed. Decisions agreed with the user are in §1. Step-by-step details live in separate files (`plan_stepA.md`, …).

## 1. Requirements / decisions

- Item elevator between exactly two DeepRim map layers (ground <-> deep, or deep <-> deep). The connection is fixed once made; no reconfiguring.
- ONE building type. Players and conveyors put items into it as if it were storage (with a storage filter). Items are absorbed into an **internal buffer** (they "vanish" into the elevator, the same way a transporter's contents do). Output goes to a separate **adjacent storage** at the partner, or onto the floor (see delivery rule). Elevators are never output targets, so there are no transport loops.
- **Pairing.** Key = `elevator.Position - anchor.Position` on its map. Anchor = DeepRim `miningshaft` (surface) or `Building_SpawnedLift` (deep). No scaling between map sizes: if the matching cell on the other layer is out of bounds, there is no match.
  - Candidates are elevators on other maps **on the same world tile** with the same key and no partner.
  - Exactly one candidate when built: auto-pair. Several candidates (possible after partners were destroyed): stay unconnected and offer a **"Connect to…" gizmo** listing the candidates (layer depth/name).
  - Deconstructing or destroying one end, or removing its map, makes the other end unconnected again.
  - Multiple connected pairs may share a key, as long as they are on different layers.
- Connections are stored as direct references (`partner` + `partnerMap`, `Scribe_References`). No GameComponent is needed: saved references are reliable (§2), and the candidate search only runs on spawn/placement (`Find.Maps` × `listerBuildings`). Log a warning when connected but the partner is invalid.
- Placement allowed only when: the map has exactly one anchor (shaft or lift), the cells are free, and the elevator is **not adjacent to the anchor** (DeepRim empties any storage adjacent to a shaft/lift, see §2). Our own check only; no patching of DeepRim or other mods. The mod description tells users not to build two DeepRim shafts (DeepRim itself refuses a second shaft unless Shift is held).
- Placement helper (nice-to-have, last): indicate locations of unpaired elevators on other layers of the same tile, and which layer a spot would connect to.
- **Ticking.** The building uses `tickerType Rare` (every 250 ticks). All periodic work sits in one method called from both `TickRare()` and a throttled `Tick()`, so switching to `Normal` is XML-only.
- **Input handling.** Event-driven: `Notify_ReceivedThing` queues the item, which is absorbed into the buffer soon afterwards, never inside the spawn call chain. With a Rare ticker, "soon" would otherwise mean up to 250 ticks. Step B option: a per-map MapComponent drains a queue of pending elevators on the next game tick and does nothing while the queue is empty. Decide in Step B. A send attempt follows. Tick backstop: a periodic cell scan absorbs anything the events missed, and held items are retried periodically. `Accepts()` is false while disconnected or when the buffer is full. Transfer rate is limited per time unit.
- **Delivery rule** (per item, evaluated by the *sender* before removing the item from its buffer):
  - an adjacent destination storage's filter allows the item and there is space → place it there;
  - a storage's filter allows it but all such storages are full → **hold at origin** (the item stays in the sender's buffer; retry later);
  - no adjacent storage's filter allows it → drop on free cells adjacent to the partner (never on elevator cells); no free cell → hold at origin.
- **Atomic transfer.** Removing the item from the sender's buffer and placing it at the destination happen in one synchronous call; on failure the item goes back into the buffer. Nothing is ever "in transit" across a save. If the sender's map is removed, its buffer goes with it. If the partner is lost, the buffer stays and can be unloaded.
- **Unload gizmo:** (later, with manifest: `CancelLoad()`) drop all buffer contents on free cells around the elevator, like a shuttle. NOT forbidden: pawns haul them to normal storage. Works while disconnected.
- **Contents UI:** buffer contents are inspectable through a "Contents" tab (based on vanilla `ITab_ContentsBase`), plus an item count in the inspect string. Added in the step where items can be put into the elevator.
- **Manifest (later step, decide then):** the player picks a list of items to send. Preferred approach: reuse the vanilla `CompTransporter` / `Dialog_LoadTransporters` UI, which also takes care of directing pawns' hauling. Pawns should not show up in the list (initial approach: don't hack the UI, ignore pawns when sending). Ideally animals can be transported. Weigh this against the CompTransporter issues (mass capacity, pawn tab, a second container) when we get there. **Architecture requirement now:** the buffer is a `ThingOwner<Thing>` (can hold pawns), and the delivery API takes `Thing` (not only items), so manifest contents and animals can feed the same buffer and delivery code later.
- Save/load: elevator, partner + partnerMap, storage settings, buffer contents (`Scribe_Deep`), rate-limit state, (later) manifest state via the comp.
- Minimal implementation; avoid Harmony where possible; no mod settings for now.
- **Stepwise implementation.** After each step the human user tests, then the next step is planned.
  - Step A: building, pairing, connect gizmo, lifecycle callbacks with logging. No storage, no transport. → `plan_stepA.md`
  - Step B: storage + internal buffer, absorb events + backstop, transport + rate limit, delivery rule, Unload, Contents tab, save/load of buffer.
  - Step C: VFE conveyor integration checks (push into elevator, delivery into hoppers).
  - Later: manifest, placement helper, graphics/polish.

## 2. Verified facts

From the decompiled 1.6 `Assembly-CSharp` (ilspycmd) and the DeepRim source in `Examples/`.

### Map removal (all paths: DeepRim abandon, shaft destroyed, surface gone, Dev tools)
- `Game.DeinitAndRemoveMap(map)`: `map.Parent.Notify_MyMapAboutToBeRemoved()` → `MapDeiniter.Deinit` → `maps.Remove(map)` → `MapComponentUtility.MapRemoved(map)` (calls virtual `MapComponent.MapRemoved()`) → `map.Parent.Notify_MyMapRemoved(map)` → `map.Dispose()`.
- `MapDeiniter.NotifyEverythingWhichUsesMapReference` calls **`Thing.Notify_MyMapRemoved()` (public virtual)** on every thing on the map, recursively including contained things. It sets `mapIndexOrState = -3`, so the thing becomes `Destroyed == true`, `Discarded == true`, `Spawned == false`.
- **`Destroy()` / `DeSpawn()` are NOT called on map removal.** The elevator must also override `Notify_MyMapRemoved()` to unlink its partner. `Destroy`/`DeSpawn` are still needed for buildings destroyed while the map stays. All of these go through one shared handler (log + idempotent unlink). Periodic validation is the backstop.
- Things on maps after the removed one get `DecrementMapIndex()`, so `Thing.Map` of surviving things stays correct. `Map` object references are stable; never store a map index.

### DeepRim
- No public "layer removed" event. Abandon: `Building_MiningShaft.abandon()` (private) → `UndergroundMapParent.AbandonLift()` → `Abandon()` sets `shouldBeDeleted` → vanilla `ShouldRemoveMapNow()` → `DeinitAndRemoveMap`. Destroying the shaft abandons all layers. `Building_SpawnedLift.Tick` removes its map if the surface map is gone. Patching DeepRim is unnecessary: the vanilla hooks above cover every path.
- Read-only API used: `ShaftThingDefOf.miningshaft` (surface anchor), `Building_SpawnedLift` (deep anchor, `.depth`), `UndergroundMapParent` (`map.Parent`, `.depth`), `UndergroundManager` (MapComponent on the surface map: `layersState`, `GetLayerName(depth)`).
- DeepRim already transfers items from **any** `ISlotGroupParent` adjacent to a shaft/lift every TickRare (`Building_ShaftLiftParent.NearbyStorages` / `Transfer`). That's the reason for the placement rule above. Its `Transfer()` is a usable reference pattern for delivery (Accepts → `IsValidStorageFor` → `TryPlaceThing(Direct)` → spawn-adjacent fallback).
- Lift position on a deep layer = `holeLocation`, scaled from the shaft position if map sizes differ. Our key is not scaled (decision).
- `PlaceWorker_AboveGround` prevents shafts on deep layers and a second shaft (unless Shift is held).

### Save/load
- `Game.LoadGame`: all data read → `Scribe.loader.FinalizeLoading()` resolves **all cross-references, cross-map included, before anything spawns** → `maps[i].FinalizeLoading()` spawns map by map.
- So a saved `partner` reference is valid after load. But during `SpawnSetup(respawningAfterLoad: true)` the partner on another map may not be spawned yet. No pairing or validation during load-spawn; validate on TickRare.
- `Scribe_References.Look` on a destroyed thing saves null (`CheckSaveReferenceToDestroyedThing`), not a dangling ID.

### Storage
- `Thing.SpawnSetup` calls `slotGroup.parent.Notify_ReceivedThing(this)` for items spawned onto a storage cell (virtual in `Building_Storage`; `Notify_LostThing` virtual too). Merging into an existing stack does not spawn, so it sends no notification.
- `Building_Storage.Accepts(Thing)` is **not virtual**. It implements `ISlotGroupParent.Accepts` / `IHaulDestination.Accepts`. Override without Harmony by re-implementing the interfaces: `class Building_Elevator : Building_Storage, ISlotGroupParent, IHaulDestination { public new bool Accepts(Thing t) … }`. Callers that use the interface (vanilla hauling via `slotGroup.parent`, VFE `slotGroupParent.Accepts`) get our version.
- Vanilla `ITab_ContentsBase` exists (transporter / genepack holder contents tabs).
- Ticking: the ticker type selects the call. `tickerType Rare` → only `TickRare()`; `tickerType Normal` → only `Tick()` (`TickRare()` not called). Hence the shared periodic method (§1).

### VFE conveyor compatibility (Building_Conveyor.cs)
- **Push (conveyor → elevator):** into any forward `Building_Storage`. It first merges into existing stacks on the storage cells by editing `stackCount` directly (lines ~1418-1437: no `Accepts`, no notification), then uses `Accepts(item)` + `GenPlace.TryPlaceThing(Direct)` (~1441-1446). With the internal buffer the elevator cell stays empty, so only the second path is used. `Accepts()` false makes conveyors stall properly.
- **Pull (storage → conveyor):** only from `Building_FactoryHopper` or a `Building_Storage` with `PipeSystem.FactoryHopperExtension { isfactoryHopper = true }` (lines 441, 1182). So the **output target must be a VFE hopper** next to the destination elevator, and the elevator delivers into it like any storage. Do NOT mark the elevator itself as a hopper.
- VFE's hopper "allow taking" switch works through Harmony patches on `ForbidUtility.IsForbidden`. We don't need that, thanks to the buffer.
- Optional later: a conditional XML patch (mayRequire VEF) for an elevator-integrated output hopper.

## 3. Design

| Def | Class | Notes |
| --- | --- | --- |
| `DeepElevator_Elevator` (1x1, rotatable, Graphic_Multi) | `DeepElevator.Building_Elevator` | Step A: `: Building`. Step B: `: Building_Storage` + interface re-implementation, internal buffer, `ITab_Storage` + Contents tab |

Other code: `ElevatorUtil` (static: anchor lookup, key, candidate search, layer label, logging, later delivery), `PlaceWorker_Elevator`, `MapComponent_ElevatorProbe` (Step A: logs map removal; may be dropped after testing). Gizmos are plain `Command_Action`.

Saved fields: `partner` (Reference), `partnerMap` (Reference). Step B adds `buffer` (Deep) and rate-limit state (Value); storage settings come from the base class. There is no `outputCell` (output isn't selectable) and no `retry` flag (retry = buffer not empty).

Partner validity: `partnerMap != null && Find.Maps.Contains(partnerMap) && partner != null && !partner.Destroyed && partner.Spawned && partner.Map == partnerMap`.

UI: inspect string "Connected to <layer>" / "Not connected" (+ candidates hint), items in buffer; gizmos Connect to… (when ambiguous), Unload, later Load/Cancel load; vanilla storage tab for the input filter.

## 4. Files

| Path | Contents |
| --- | --- |
| `DeepElevator.csproj`, `DeepElevator.slnx`, `Properties/AssemblyInfo.cs` | VS project, stays in repo root; OutputPath `DeepElevator\1.6\Assemblies\` |
| `DeepElevator/About/About.xml` | packageId `FT.deepelevator`, dependency + loadAfter `Mlie.DeepRim`, description |
| `DeepElevator/loadFolders.xml` | exists (`/`, `1.6`) |
| `DeepElevator/Defs/ThingDefs/DeepElevator_Elevator.xml` | ThingDef |
| `DeepElevator/Languages/English/Keyed/DeepElevator.xml` | strings |
| `DeepElevator/Source/*.cs` | code (compiled by the root csproj) |
| `DeepElevator/Textures/Things/Buildings/DeepElevator/` | `DeepElevator_{north,east,south,west}.png` (Graphic_Multi), `DeepElevator.png` (Graphic_Single path, spare), `DeepElevator_MenuIcon.png` |
| `DeepElevator/1.6/Assemblies/DeepElevator.dll` | build output |

DeepRim reference: `C:\Games\RimWorld\Mods\2164179860\1.6\Assemblies\DeepRim.dll` (Private=false). `Examples/` is git-ignored and not shipped. When shipping, also exclude `Source/`.

## 5. Assets

Decide during implementation (with the human user) which graphics to use. Available now: rotatable building textures + menu icon. Later: gizmo icons (`UI/Unload.png`, `UI/Connect.png`, 64x64), `About/Preview.png` (640x360), optional disconnected/blocked look, optional send animation (overlay lerp or vanilla effecter), vanilla SoundDefs.

## 6. Verification

The AI builds with MSBuil (VS 2026 Community is installed). The human tests in game after each step. Use `dotnet ilspycmd` (installed in this project) to check vanilla behaviour when in doubt.
