# DeepElevator – plan (rev 7)

Target: RimWorld 1.6 only (Assembly-CSharp 1.6.4871). **No hard mod dependency.** Supported level mods: DeepRim (Mlie, `Mlie.DeepRim`) and MultiFloors (telardo, `telardo.MultiFloors`), each optional and in any combination. Soft interaction: Vanilla Furniture Expanded - Factory (VFE) conveyors. There is no compile-time reference to any of these mods.

- Rev 4: reviewed against DeepRim, VFE and the decompiled 1.6 assembly.
- Rev 5: Step A results folded in (`results_stepA.md`). The DeepRim hard dependency is removed and MultiFloors support added: pairing is now center-relative, with no anchor building. MultiFloors facts verified in its decompiled 1.6 assembly. Steps renumbered.
- Rev 7: storage/request design decided: the vanilla transporter container is the single buffer, requests use the vanilla Load dialog (A), tickerType Normal, buffer control gizmos. To-do-later and to-decide-later lists added (§7). Step C = storage, buffer, transport.
- Rev 6: Step B results folded in (`results_stepB.md`): label format changed (map prefix, no MultiFloors "G", "surface"), "Go to partner" gizmo. Step C facts verified (holder ticking, storage accept path, contents tab, drop API).

The facts in §2 are verified in code unless marked otherwise. Step details live in separate files (`plan_stepA.md`, …).

## 1. Requirements / decisions

### Scope
- **Design principle: the elevator is a simple elevator, not a smart controller.** It passes things between levels. No map-wide resource statistics, no "keep N items stocked on the other level" logic, no automatic demand detection. Requests are explicit player orders with fixed amounts.
- Item elevator between exactly two **levels** = maps on the same world tile: the ground map, DeepRim layers, MultiFloors levels (above or below, also those opened from a DeepRim layer), and maps of other mods that put their maps on the same tile. The connection is fixed once made; no reconfiguring.
- ONE building type, 1x1, single non-rotating graphic. Players and conveyors put items into it as if it were storage (with a storage filter). Items are absorbed into an **internal buffer** (they "vanish" into the elevator, the same way a transporter's contents do). Output goes to an **adjacent storage** at the partner, or onto the floor (see delivery rule).

### Placement and pairing (rev 5)
- **Level group** (agreed): two maps are in the same group when their **group root** is the same.
  - Group root of a map: follow `PocketMapParent.sourceMap` while the map has no valid tile (`!map.Tile.Valid && map.Parent is PocketMapParent { sourceMap: not null }`), up to the first map with a valid tile (or a map with no source); guard against cycles.
  - Two roots with valid tiles match when the tiles are equal; roots without a valid tile match only if they are the same map.
  - Result: surface, DeepRim layers and MultiFloors levels on one tile form one group (they all have the valid tile). A vanilla pocket map (e.g. Anomaly undercave) joins the group of the map it was opened from, and so do DeepRim/MultiFloors levels created from it (they inherit the invalid tile, §2).
  - Two unrelated pocket maps never match just because both tiles are `Invalid`.
  - Cost: a short loop per map; only used in candidate search, never in hot paths.
- **Pairing key = center-relative position:** `offset = elevator.Position - map.Center` (`Map.Center = (Size.x/2, 0, Size.z/2)`). The matching cell on another level is `otherMap.Center + offset`; if it isn't `InBounds`, there's no match on that level.
  - Same-sized maps (all MultiFloors levels of one stack; DeepRim layers when its map-size setting is off or matches): this is simply the **same absolute cell**.
  - Differently sized maps (DeepRim map-size setting): the centers coincide, and the smaller map is overlaid 1:1 on the center of the larger one; elevators on the larger map's outer ring have no partner there.
  - **Why not DeepRim's proportional scaling** (decision). DeepRim scales the shaft position onto the layer: center maps to center, but on a 250 surface and 200 layer, 100 cells east of center becomes 80 east. We use a 1:1 overlay instead, because:
    1. Scaling maps several surface cells onto one layer cell, so a position would no longer identify one partner.
    2. Deciding whether two elevators line up gets complex when DeepRim and MultiFloors maps mix. For example, how would a MultiFloors level opened from a DeepRim layer match a MultiFloors level opened from the surface? With the 1:1 overlay every map uses the same rule: offset from its own center.
    3. Scaling breaks physics: two connections near opposite edges of the surface would end up much closer together on a small DeepRim layer.
  - Downsides, accepted:
    - The elevator's position relative to the DeepRim shaft/lift can differ between levels, which may look strange.
    - Elevators can only connect within the smaller map's area, so they can't cover all of a larger map.
    - Both disappear if the player sets DeepRim's layer size equal to the surface map size.

  **Mod description / instructions text** (keep for the Steam description):
  > Elevators connect when they stand at the same position measured from the center of their maps. All levels on the same world location count: the surface, DeepRim layers, MultiFloors levels, and pocket maps together with the map they were opened from. If maps have different sizes (DeepRim's layer size setting), the smaller map is placed over the center of the larger one, cell for cell. Elevators outside the smaller map's area can't connect to it, and an elevator's position relative to the DeepRim shaft may differ between levels. To avoid both, set DeepRim's layer size to the same size as your surface map. DeepElevator doesn't stretch positions like DeepRim does: stretching would let several surface cells share one layer cell, make matching levels from different mods ambiguous, and move connections at opposite map edges closer together underground.
  - The mapping is symmetric, so pairing from either side finds the same cell.
  - The DeepRim shaft/lift position plays no role any more. DeepRim *scales* the lift position, it doesn't center it (§2), so lift and elevator alignment may differ on different-sized layers. That's acceptable: the elevator is independent of the shaft.
- **Placement rules (agreed):** no building is used as an anchor, and there's no extra placement rule. The elevator can be built on any map, also before another level exists. It stays "Not connected" until a partner appears. `PlaceWorker_Elevator.AllowsPlacing` is removed, so there is zero cost in the hot path (results_stepA §3.2). The class stays for the later placement helper (`DrawGhost`).
- Candidates are unconnected, spawned elevators at the matching cell on other maps of the level group.
  - Exactly one candidate when built: auto-pair.
  - Several candidates: stay unconnected and offer the **"Connect to…" gizmo** listing them by level label.
  - Deconstructing or destroying one end, or removing its map, makes the other end unconnected again.
  - Multiple connected pairs may share a position, as long as they are on different levels.
- Adjacency to anything (a DeepRim shaft/lift, other elevators) is allowed. The player must know that DeepRim pulls items out of storage adjacent to its shaft/lift every 250 ticks (§2); some may even want that.
- **Labels** (inspect string, Connect menu, messages) are UI text only. Pairing never depends on them. Vanilla labels alone don't help: every DeepRim layer is "underground mining camp" (its world object def label) and every MultiFloors level is a hidden vanilla "pocket map". Agreed solution, both parts in Step B:
  - **Mod-aware level labels, tagged with the source mod.**
    - **Format (as built, Step B):** `map<id> - <label>`, e.g. "map27 - 90m (DeepRim)", "map25 - B1 (MultiFloors)", "map0 - surface". The id prefix tells apart levels with equal labels.
    - DeepRim: depth "70m" (+ the player's layer name if set) + " (DeepRim)", e.g. "70m Mines (DeepRim)".
    - MultiFloors: its own level symbol (`LevelUtility.CurrentLevelString`, upper / basement symbols) + " (MultiFloors)", e.g. "B1 (MultiFloors)". **Level 0 gets no MultiFloors label:** MultiFloors calls every stack origin "G", including a DeepRim layer with stairs, which was confusing.
    - A map can get both: a MultiFloors level on a DeepRim layer becomes "70m (DeepRim), B1 (MultiFloors)".
    - No mod label: a non-pocket map is "surface" (also covers other non-pocket maps on a tile, e.g. quest sites). A pocket map keeps the generic "<parent label> (map N)" without the prefix (untested; no pocket map was available).
    - Both are read at runtime by looking up those mods' classes by name (reflection; members resolved once at startup; no compile-time reference). If a mod is missing or its code changed, fall back to the vanilla label + map id ("pocket map (map 5)", no tag), logging once.
    - Tag texts are language keys (`DeepElevator_TagDeepRim`, `DeepElevator_TagMultiFloors`).
  - **Labels are computed on demand, with no periodic refresh.** They're only needed occasionally: Connect menu, link/unlink messages, inspect string while selected, placement helper text. The inspect string runs every frame while selected, but that is one object and a few cached-reflection calls, which is acceptable. There's no stale-label problem either (e.g. after renaming a DeepRim layer).
  - **Player-named elevators.** The elevator implements vanilla `IRenameable` with a Rename gizmo (vanilla `Dialog_Rename<T>`). If named, the elevator's name is shown first in the Connect menu, inspect string and messages: "Ore lift on map27 - 90m (DeepRim)" (`DeepElevator_ElevatorOnLevel` = `{0} on {1}`). Log lines use `LogId` (thing id + name).
  - **"Go to partner" gizmo** (Step B, while connected): `CameraJumper.TryJumpAndSelect(partner)`; placeholder icon `UI/Widgets/Search`. Works for any mod. The name is saved (`Scribe_Values`).
- **Placement helper (Step B).** While the elevator is being placed, the build designator calls `PlaceWorker.DrawGhost` (world pass) and `PlaceWorker.DrawPlaceMouseAttachments(curX, ref curY, …)` (GUI pass) once per frame (verified in `Designator_Place`).
  - **Highlight** (`DrawGhost`): mark the cells on the current map that match an **unconnected** elevator on another level of the group (`GenDraw.DrawFieldEdges`, one color). Also outline the **overlap area** with each other level of a different size (the cells that have a counterpart there), so the player sees where connections are possible on smaller maps.
  - **Text under the cursor** (`DrawPlaceMouseAttachments`): when the mouse is on a highlighted cell, show "Connects to: <level label>", with more than one line if ambiguous. When it's outside the overlap area of some level, show "No counterpart on: <level label>" (only for levels in the group).
  - **Cost:** cell lists and rects are cached in the place worker and rebuilt only when the current map changes or every ~60 frames while placing, reusing lists (no per-frame allocation). The text lookup is a dictionary/HashSet check on the mouse cell. No work at all when not placing an elevator.

### Storage handling and requests (rev 6 discussion)
Background facts: §2 "Hauling system" and "Transporter loading".

**Roles the elevator must cover:**
1. Pawns haul anything allowed into it (e.g. underground farm → surface), via high storage priority.
2. VFE conveyors push into it; priority matters little.
3. On-demand delivery of chosen items (e.g. construction materials for a level where pawns don't haul across), and later possibly animals and pawns.

**Input for roles 1 and 2 (decided): storage cell as intake + one internal container.** The elevator stays a shelf-like `Building_Storage`; basic operation doesn't change. The one container is the vanilla transporter's (`CompTransporter.innerContainer`); normal deliveries are pulled into it too.
- The elevator is a `Building_Storage` with its cell(s) as intake, so vanilla hauling and VFE conveyors work unchanged. Items placed on the cell are moved into the internal container on the next normal tick, so the cell is (almost) always empty.
- The game reserves each storage cell for **one pawn at a time** (`IsGoodStoreCell` → `CanReserveNew(cell)`). One cell therefore means one haul under way at a time; more intake cells (a larger building) would allow parallel hauls. Throughput is also limited by our send rate.
- The game pre-filters with the storage **filter**, then asks **`Accepts`** per cell. With an always-empty cell the elevator stays a valid target, and `Accepts` alone decides what it takes (filter + connected + buffer room + optional first-in-first-out/single-type rules). A refused item is simply hauled elsewhere or waits.
- Why a container rather than a plain shelf: items on a shelf can be taken out (hauled to better storage, used for construction, cooking or eating), VFE's stack merging bypasses `Accepts`, the game controls stack merging and order, and pawns/animals can't sit on a shelf. A shelf-only variant (forbid items on arrival, unforbid on delivery) was considered and rejected: forbidden markers, no order control, and a second path would still be needed for pawns and manifests.

**Requests (role 3): options considered**

| Option | Verdict | Reason |
| --- | --- | --- |
| A. Vanilla transporter (`CompTransporter` + `Dialog_LoadTransporters`), its container used as **the** buffer | **Primary (decided)** | Vanilla UI and load jobs. Pawns, prisoners, downed pawns and animals are supported (carried in by haulers; free colonists may need extra handling). Low–medium effort. Downsides: caravan-oriented dialog (mass bar, travel/food info; cleanup via Harmony is on the to-do-later list), no pause when the buffer is full (bounded by the request amount), takes items from storage of any priority. |
| D. Own request dialog + own `WorkGiver` issuing vanilla `HaulToContainer` jobs into the same container | **Alternative** (lower on the feasibility list: carrying pawns/animals in would need custom job handling) | UI-supporting vanilla functions: `TransporterUtility.AllSendableItems` / `AllSendablePawns` (lists items, colonists, prisoners, downed pawns, animals) and `TransferableOneWayWidget` (the list with counts and +/- buttons). The work giver is a standard pattern (like `WorkGiver_LoadTransporters`) and ignores storage priority, with our own rules: pause when full, optional "don't take from priority ≥ X". Medium effort. |
| C. Quotas on the storage path (`Accepts` allows requested types until a counter runs out) | **Dropped** | Storage priority applies to the whole storage, not per item type. Pulling requested items from other storage needs a higher priority, which also pulls every other allowed type and conflicts with normal operation. Items in equal/higher (e.g. Critical) storage never move. |
| B. Workbench bills (recipe "send", ingredients moved into the buffer) | **Dropped** | No pawns/animals (ingredients are items only). Runtime-created recipes aren't saved. Amounts are fixed per recipe, and the bill UI only edits the filter. All ingredients must be available and are held until the run completes (no streaming). Needs an interaction spot and a working pawn. |

- Requests ignore the elevator's storage filter and intake cells: they load straight into the container. Whether a request should respect the filter is to be decided in the manifest step.
- Requests and normal input share one buffer, and one send path streams it out. Nothing needs to arrive together, since the elevator doesn't "leave" like a shuttle.

### Links
- Connections are stored as direct references (`partner` + `partnerMap`, `Scribe_References`). No GameComponent is needed.
- At load-spawn, `VerifyPartnerLinksBack()` checks that the partner links back (references are resolved before spawning). Full validity (spawned, map alive) is checked periodically. Log a warning on every repair.
- Lifecycle: `Destroy`, `DeSpawn` and `Notify_MyMapRemoved` all go through one handler (log + idempotent unlink). Confirmed in game for deconstruct, destroy, DeepRim abandon, Dev map removal and shaft destruction.

### Ticking, input, delivery
- **Ticking (decided, Step C): `tickerType Normal`.** The elevator handles its own content (no central MapComponent). `Tick()` exits early when there's nothing to do. Pulling inserted items happens on the next tick; sending runs at the configured rate; backstop work every 250 ticks (`IsHashIntervalTick(250)`). Normal is also required for `CompTransporter.CompTick`, which ticks the contents.
- **Hot paths:** anything vanilla or other mods may call very often (`Accepts`, place workers, `DrawGhost`, gizmo/inspect builders) must be O(1) and allocation-free, or cached. Background: results_stepA §3.2, where ~20000 `AllowsPlacing` calls per frame came from Designator Shapes.
- **Input handling.** Event-driven: `Notify_ReceivedThing` sets a flag; on the next tick the elevator moves items from its intake cell(s) into the container, never inside the spawn call chain. Backstop: a cell scan every 250 ticks. `Accepts()` = filter + connected + room under the slot/merge rules, and must stay cheap. Transfer rate limited per elevator (XML settings).
- **Delivery rule** (per buffer entry, evaluated by the *sender* before anything leaves its buffer; piece = at most `def.stackLimit`):
  - **output cells = the ring of cells adjacent to the partner only** (a big stockpile or long shelf is used only where it touches the partner; vanilla haulers also target one specific cell, never "the storage" as a whole). Collect the storages among those cells (stockpiles, shelves, other elevators, all handled alike; not the partner itself), highest priority first. Every one whose `Accepts` takes the item is a candidate. Fill the first candidate's output cells; any remainder goes to the next. **Partial delivery is fine** (4000 corn, room for 2000 → deliver 2000).
  - some candidate exists but all are full → **hold at origin** (stays in the sender's buffer, retry later);
  - no adjacent storage accepts it → drop on an output cell that is **plain floor** (not a storage cell): empty, or holding a same-kind partial stack with room (e.g. an earlier drop, which gets topped up). A partial stack in a storage cell that doesn't accept the item is never a drop target. Storage cells (stockpile, shelf, elevator) are never drop targets; storage is used only when it accepts the item. Never walls, doors, other buildings, blueprints/frames or fogged cells. Partial drops are fine. Placement only through `GenPlace.TryPlaceThing`, never `GenSpawn.Spawn` (which destroys walls, §2). No room → hold at origin. Toggle: drop-on-floor off → hold instead.
- **Unexpected items on the intake** (dropped, spawned or left there without passing `Accepts`) are absorbed only if the elevator is connected and its filter allows them. Otherwise they stay, and the game hauls them away as misplaced.
- **Disconnected elevators take nothing in** (`Accepts` false, nothing absorbed), but can be unloaded. The Load (manifest) gizmo must be disabled while disconnected (Step E).
- **Buffer control (Step C, per-elevator gizmos; the modes are independent):** slots (+/−; a slot holds one max-size stack, `def.stackLimit` read at runtime). **Merge** toggle governs *input* only: on → an arrival joins any buffered entry of the same kind (a waiting stack may grow); off → every arrival is its own entry. **FIFO** toggle governs *sending* only: on → strictly the first entry is sent, so a held first entry blocks the queue; off → the first deliverable entry is sent. **Drop-on-floor** toggle: off → items no adjacent storage accepts are held instead of dropped. With 1 slot and merge on, the first item sets the type until the stack is full. Held items retry at the send rate (`heldRetryTicks` defaults to `ticksPerStack`).
- **Loops are the player's responsibility.** Adjacent elevators (and their storage) may be delivery targets. Conveyor loops are possible; the rate limit keeps them from being instant. A "pawn bounce" (dropped output hauled back into the partner) is avoided by not letting the elevator's input filter accept its own output. No extra mechanism.
- **Atomic transfer.** Removing the item from the sender's buffer and placing it at the destination happen in one synchronous call; on failure the item goes back into the buffer. Nothing is ever "in transit" across a save. If the sender's map is removed, its buffer goes with it. If the partner is lost, the buffer stays and can be unloaded.
- **Unload gizmo:** like vanilla transporter/shuttle unloading: `ThingOwner.TryDrop(…, ThingPlaceMode.Near, validator)` around the elevator (vanilla search radius ~12.9; may stack onto same-kind items; never destroys). Never onto elevator cells; into a stockpile/shelf only if it accepts the item. If items are still inside afterwards, show a message. Items are NOT forbidden: pawns haul them to normal storage. Works while disconnected. Later, with manifests: also `CancelLoad()`. (Delivery drops, in contrast, use adjacent plain-floor cells only.)
- **Contents UI:** the vanilla transporter contents tab (`ITab_ContentsTransporter`), subclassed so that its drop uses the Unload rules (vanilla-like Near placement, never onto elevators or shelves; the vanilla drop would start at the intake cell). Plus slots used / total in the inspect string.
- **Manifest (Step E):** vanilla Load dialog (option A). The loaded things go into the same container and are streamed out by the same send path; nothing has to arrive together. In Step C the vanilla Load gizmo is hidden.
- Save/load: partner + partnerMap, custom name, storage settings (base class), the container with its order (saved by `CompTransporter`), buffer settings (slots, FIFO, merge).
- Minimal implementation; avoid Harmony where possible; no mod settings for now. Logging: unconditional while probing, later gated on `Prefs.DevMode`.

### Steps
After each step the human user tests, then the next step is planned.
- **Step A (done):** building, pairing via DeepRim anchor key, connect gizmo, lifecycle callbacks with logging, load-time link check. → `plan_stepA.md`, `results_stepA.md`
- **Step B (done):** mod-agnostic levels. → `plan_stepB.md`, `results_stepB.md`. Untested: vanilla pocket maps (needs e.g. an Anomaly undercave) and the label-provider fallback with a broken provider. Original scope: Remove the DeepRim reference and dependency. Level group by group root (tile / pocket-map source chain), center-relative key, no placement rule, mod-aware level labels with source-mod tags and vanilla fallback (computed on demand), renameable elevators, placement helper (highlight + cursor text + overlap outline). Probes stay (user decision) and are extended to log the key and matched cells. Test with: DeepRim only, MultiFloors only, both, a MultiFloors level opened from a DeepRim layer, DeepRim with a different map-size setting, vanilla only, an Anomaly pocket map (undercave) and its source map, a DeepRim layer or MultiFloors level opened from a pocket map, renaming, label fallback with a mod missing, placement helper (highlights, cursor text, overlap outline, no lag with Designator Shapes).
- **Step C: storage, buffer, transport** → `plan_stepC.md`. `Building_Storage` intake + `CompTransporter` container, tickerType Normal, absorb on next tick, `Accepts` rules, streaming send with rate limit, delivery rule, adjacent drop, Unload, contents tab, buffer gizmos (slots ±, FIFO, merge, drop on floor), logs.
- **Step D:** VFE conveyor integration checks (push into elevator, delivery into hoppers).
- **Step E:** manifest via the vanilla Load dialog (items; then animals/pawns), plus the related to-do items.
- **Later:** graphics/polish; see §7.

## 2. Verified facts

From the decompiled 1.6 `Assembly-CSharp` and `MultiFloors.dll` (ilspycmd), the DeepRim source in `Examples/`, and Step A in-game tests.

### Map removal (all paths: DeepRim abandon, shaft destroyed, surface gone, Dev tools)
- `Game.DeinitAndRemoveMap(map)`: `map.Parent.Notify_MyMapAboutToBeRemoved()` → `MapDeiniter.Deinit` → `maps.Remove(map)` → `MapComponentUtility.MapRemoved(map)` (virtual `MapComponent.MapRemoved()`) → `map.Parent.Notify_MyMapRemoved(map)` → `map.Dispose()`. Pocket maps whose `sourceMap` is the removed map are destroyed too if their generator says so (`destroyOnParentMapAbandoned`).
- `MapDeiniter.NotifyEverythingWhichUsesMapReference` calls **`Thing.Notify_MyMapRemoved()` (public virtual)** on every thing on the map, recursively. It sets `mapIndexOrState = -3` (`Destroyed`, `Discarded` true, `Spawned` false).
- **`Destroy()` / `DeSpawn()` are NOT called on map removal.** Confirmed in game (results_stepA, round 2).
- Things on later maps get `DecrementMapIndex()`. `Map` object references are stable; never store a map index.

### World tile / map grouping
- `Map.Tile => info.Tile => parent?.Tile ?? PlanetTile.Invalid`. `WorldObject.tile` defaults to `PlanetTile.Invalid`. `PlanetTile.Valid => tileId >= 0`; `==` compares by value.
- Vanilla `PocketMapUtility.GeneratePocketMap` sets `sourceMap` but **not** `Tile`, so vanilla pocket maps have an invalid tile (they are also hidden on the world map). Comparing tiles alone would put all of them into one group, hence the group-root rule (§1).
- Neither DeepRim nor MultiFloors refuses to create levels on a pocket map (from code; not tested). DeepRim's placement only refuses deep layers and second shafts. MultiFloors' stair placement checks level, roof, room size and bedrock. Their new maps copy the source's tile: DeepRim `mapParent.Tile = shaft.Tile`, MultiFloors `Tile = sourceMap.Tile`. So levels of a pocket map also have an invalid tile. DeepRim groups layers via its per-surface `UndergroundManager`; MultiFloors groups levels via the `sourceMap` stack in `MF_LevelMapComp`. Neither uses the tile.
- **Storage building must have `surfaceType Item`:** `StoreUtility.NoStorageBlockersIn` rejects a cell when a thing there has `surfaceType None`, isn't standable and `GetMaxItemsAllowedInCell <= 1`. Vanilla shelves (`ShelfBase`) set `surfaceType Item` and use `ITab_Storage`.
- Vanilla `IRenameable` (`RenamableLabel`, `BaseLabel`, `InspectLabel`) and `Dialog_Rename<T>` exist.
- Placement draw hooks: `Designator_Place` calls `placeWorker.DrawGhost(…)` (via `GhostDrawer.DrawGhostThing(drawPlaceWorkers: true)`) and `placeWorker.DrawPlaceMouseAttachments(curX, ref curY, PlacingDef, UI.MouseCell(), placingRot)` / `DrawMouseAttachments(PlacingDef)` (GUI).
- `Map.Center = new IntVec3(Size.x / 2, 0, Size.z / 2)`.

### DeepRim (optional)
- Layers are `UndergroundMapParent` world objects on the surface's tile (`mapParent.Tile = shaft.Tile`), so they share the level group.
- Layer size: `DeepRimSettings.SpawnedMapSize` if ≥ 50, else `Find.World.info.initialMapSize`. A layer can be smaller or larger than the surface.
- Lift position = `HarmonyPatches.ConvertParentDrillLocation(shaft position, parentSize, childSize)`: **proportional scaling**. Centers coincide (center maps to center), but other cells stretch. Same size → same cell. (`drillToOldLayer` spawns an extra lift at the unscaled shaft position; DeepRim edge case, irrelevant to us now.)
- No public "layer removed" event; the vanilla hooks cover every path. Layer depth: `UndergroundMapParent.depth` (public field); names: `UndergroundManager` (MapComponent on the surface map), `GetLayerName(int depth)`. Displayed depth = `depth * 10` m.
- DeepRim transfers items from **any** `ISlotGroupParent` adjacent to a shaft/lift every TickRare (`Building_ShaftLiftParent.NearbyStorages` / `Transfer()`, a usable reference pattern for delivery). Adjacency is allowed by decision; this is player knowledge.
- `PlaceWorker_AboveGround` prevents shafts on deep layers and a second shaft (unless Shift is held).

### MultiFloors (optional, `telardo.MultiFloors` 1.6.1.3; needs Harmony + Prepatcher)
- New level: `LevelMapGenerator.GenerateLevelMap` makes a vanilla `PocketMapParent` with `sourceMap = current map` **and `Tile = sourceMap.Tile`**. So MultiFloors levels share the level group (unlike vanilla pocket maps).
- **Size = size of the map the stairs were built on** (`StairEntrance` passes `Map.Size`). The stair exit is spawned at the **same position** as the entrance (plus a rotation offset for multi-cell stairs). So MultiFloors itself assumes absolute cell correspondence between levels.
- Level index: Prepatcher field `Map.Level()` (injected; not accessible without a reference). The stack is kept by `MF_LevelMapComp` (MapComponent on every map) on the stack's **ground map**: `public Dictionary<int, Map> MapByLevel`, `GetAllLevelMaps()`. `LevelUtility.CurrentLevelString(int currentLevel, bool addParenthesis = false)` gives its level label (0 = ground symbol). `LevelUtility.TryGetLevelControllerOnCurrentTile(this Map, out MF_LevelMapComp)` is how MultiFloors itself finds a tile's controller. These are reachable via reflection (type `MultiFloors.MF_LevelMapComp`).
- **MultiFloors on a DeepRim layer:** a DeepRim layer has `Level() == 0` (default), so MultiFloors treats it as the ground of **its own** stack (its own `MF_LevelMapComp`). New levels get the DeepRim layer's size and the same tile. So all of them end up in one level group, and the center-relative key still lines up within that stack. (Derived from the code paths `GetOrGenerateLevelMap` → `currentMap.Level() != 0 ? GroundMap() : currentMap`. Not tested in game; Step B test.)
- MultiFloors has its own `MultiFloors.Elevator` (pawn/vehicle transfer between levels) and cross-level hauling. There's no name clash (namespaces differ). Its cross-level hauling may also move items around our elevators; to observe in Step C/D tests.
- Gravship landing (Odyssey) also generates level maps (`HarmonyPatch_OnGravshipLanding`, size = landing map size). Untested edge case.

### Save/load
- `Game.LoadGame`: all data read → `Scribe.loader.FinalizeLoading()` resolves **all cross-references, cross-map included, before anything spawns** → maps spawn one by one. A saved `partner` is valid after load; its *spawned state* is not reliable during `SpawnSetup(respawningAfterLoad: true)`.
- `Scribe_References.Look` on a destroyed thing saves null.

### Storage
- `Thing.SpawnSetup` calls `slotGroup.parent.Notify_ReceivedThing(this)` for items spawned onto a storage cell (virtual in `Building_Storage`; `Notify_LostThing` too). Merging into an existing stack sends no notification.
- `Building_Storage.Accepts(Thing)` is **not virtual**. Override without Harmony by re-implementing the interfaces: `class Building_Elevator : Building_Storage, ISlotGroupParent, IHaulDestination { public new bool Accepts(Thing t) … }`. Interface callers (vanilla hauling, VFE) get our version.
- Vanilla `ITab_ContentsBase`: `abstract IList<Thing> container { get; }`, `bool canRemoveThings`, `virtual IntVec3 DropOffset`, `protected virtual void OnDropThing(Thing t, int count)` (default: `GenDrop.TryDropSpawn(t.SplitOff(count), SelThing.Position + DropOffset, Map, Near)`, which could land on the elevator's own cell, so override it).
- Hauling reaches our `Accepts`: `StoreUtility.IsGoodStoreCell` → `IsValidStorageFor` → `slotGroup.parent.Accepts(t)` (interface call). `TryFindBestBetterStoreCellForWorker` pre-filters with `slotGroup.Settings.AllowedToAccept(t)` (the filter only) and then checks cells through `Accepts`. `IHaulDestination.Accepts` is used for haul-destination searches.
- **Thing holders tick every tick:** `TickManager.TickListFor` puts any `IThingHolder` thing into the Normal tick list regardless of `tickerType`. `Thing.DoTick` then runs `TickRare()` on its hash interval (for `tickerType Rare`) and **ticks the held contents itself** every tick (`GetDirectlyHeldThings().DoTick()`), unless the holder is `IThingHolderTickable` with `ShouldTickContents == false`. So buffered items keep rotting/deteriorating like in a shuttle, with no code of ours; **never** call `buffer.DoTick()` ourselves (that would double-tick).
- `ThingOwner<T>`: ctor `(IThingHolder owner, bool oneStackOnly, LookMode contentsLookMode = Deep, bool removeContentsIfDestroyed = true)`; `TryAdd(Thing, bool canMergeWithExistingStacks = true)`, `TryAddOrTransfer`, `Take(Thing[, int])`, `TryDrop(thing, dropLoc, map, mode, out resulting, placedAction, nearPlaceValidator)`, `TryDropAll(dropLoc, map, mode, placeAction, nearPlaceValidator)`.
- `GenPlace.TryPlaceThing(thing, center, map, mode, out Thing lastResultingThing, placedAction, extraValidator, rot, squareRadius)`; `Direct` may merge partially into a stack and leave a remainder in `thing`.
- **Pawns/animals can't use storage.** `ThingDef.EverStorable` needs `category == Item` with thing categories (or minifiable). Live pawns and animals are `ThingCategory.Pawn` and don't appear in storage filters; corpses are items and do. So the storage input path can never take live animals; only a `CompTransporter` manifest (pawns/animals walk in) could.
- **`GenSpawn.Spawn(thing, loc, map, wipeMode = WipeMode.Vanish)`** destroys whatever conflicts at `loc` (the cause of DeepRim breaking walls when it spawns transferred items at `RandomAdjacentCell8Way`). Use `GenPlace.TryPlaceThing` on pre-validated cells, never `GenSpawn.Spawn`, for delivered items.
- `ThingOwner<T>.TryAdd(item, canMergeWithExistingStacks: true)` merges with `TryAbsorbStack(thing, respectStackLimit: false)`, so a buffered stack can exceed `def.stackLimit` (e.g. 4000 corn). Output must split to `def.stackLimit`, read at runtime: mods that change stack sizes edit `def.stackLimit`.
- `ThingOwner` keeps insertion order (an inner list, saved in order by `Scribe_Deep`).
- **`CompTransporter` loading doesn't spawn anything:** pawns put items straight into `comp.innerContainer` (`ThingOwner<Thing>`). `ThingOwner.NotifyAdded` calls `CompTransporter.Notify_ThingAdded(t)` / `Notify_ThingAddedAndMergedWith` (not virtual), which subtract from `leftToLoad`. Completion = `!comp.AnythingLeftToLoad`. `CompTransporter` has a `CompTick()` that only runs if the parent's `Tick()` runs (`tickerType Normal`). `CancelLoad()` and the `MassCapacity` (`Props.massCapacity`) exist.
- `Building_Storage` has storage groups (`IStorageGroupMember`, `StorageGroupTag => def.building.storageGroupTag`) and gizmos for copy/paste settings, group linking and "select stored thing". Leave `storageGroupTag` unset so elevators can't be linked into a shared group.
- Ticking: `tickerType Rare` → only `TickRare()`; `tickerType Normal` → only `Tick()`.

### Hauling system (vanilla 1.6, decompiled)
- **Which items get hauled:** `ListerHaulables` keeps a list of items that should be hauled. These are items outside storage with a haul designation, and stored items that are not in their *valid best* storage (`IsInValidBestStorage` → `TryFindBestBetterStorageFor(..., needAccurateResult: false)`). Updated on events (spawn, forbid, `Notify_SlotGroupChanged`, `Notify_AddedThing`, `Notify_HaulSourceChanged`) plus a slow round-robin recheck: every tick 4 cells of one slot group (`CellsCheckTick`) and haul sources.
- **Finding a target happens per haul job, not batched:** `HaulAIUtility.HaulToStorageJob` → `StoreUtility.TryFindBestBetterStorageFor(t, carrier, map, currentPriority, faction, ...)`. It checks two kinds of destination and takes the better priority:
  1. **Slot groups** (stockpiles, shelves; `ISlotGroupParent`), in priority order (`AllGroupsListInPriorityOrder`). It stops at groups whose priority is not strictly above the item's current one. Per group, it pre-filters with **`slotGroup.Settings.AllowedToAccept(t)`** (the filter only, priority aside). Then per cell, nearest first, `IsGoodStoreCell`: not forbidden, `IsValidStorageFor` (= `NoStorageBlockersIn`: a stackable stack with room, or item count < `GetMaxItemsAllowedInCell`; plus **`slotGroup.parent.Accepts(t)`**), not reserved, reachable.
  2. **Non-slot-group haul destinations** ("containers": `IHaulDestination` that isn't `ISlotGroupParent`, e.g. `Building_Bookcase`, graves, outfit stands), in priority order (`AllHaulDestinationsListInPriorityOrder`). These are checked only with **`Accepts(t)`**, plus faction, forbidden, reservation (`IHaulEnroute.GetSpaceRemainingWithEnroute`), and reach. The pawn gets a `HaulToContainer` job that puts the item **directly into the holder's `ThingOwner`** (`HaulAIUtility.HaulToContainerJob`: `job.count = min(stack, owner.GetCountCanAccept(t))`). Nothing is spawned on a cell.
- So the filter is used by the game too (as a group pre-filter); `Accepts` is the final per-cell / per-container check. An elevator that refuses an item its filter allows is safe: that cell is just skipped and the item goes elsewhere or waits. When `Accepts` turns true again, items lying outside storage are considered at the next haul job. Items in other storage are found by the slow recheck, faster after `listerHaulables.Notify_SlotGroupChanged` / `Notify_HaulSourceChanged`.
- **Priority rule:** stored items only move to *strictly higher* priority storage. Items in a Critical stockpile are never hauled to other storage. Transporter loading and bills ignore storage priority.
- **Capacity in the game:** storage cells count stacks (`maxItemsInCell`, `GetMaxItemsAllowedInCell`; mods like deep-storage change it), each up to `def.stackLimit` (stack mods change it). There's no mass. `ThingOwner` has a native stack-slot capacity (`protected int maxStacks`; `GetCountCanAccept` = free slots × `stackLimit` + room in existing stacks), used by `HaulToContainer` to cap job counts. Mass capacity exists only for transporters/caravans (`CompTransporter.MassCapacity`, checked by the load dialog).
- **Request-style APIs:** `IBillGiver { Map; BillStack; IngredientStackCells; LabelShort; CurrentlyUsableForBills(); UsableForBillsAfterFueling(); Notify_BillDeleted(Bill) }`; `RecipeWorker.ConsumeIngredient(Thing, RecipeDef, Map)` is virtual; `RecipeWorkerCounter.CanCountProducts` / `CountProducts(Bill_Production)` are virtual (drive "do until you have X").

### Transporter loading (vanilla `CompTransporter`, decompiled)
- `CompTransporter`: `public ThingOwner innerContainer` (a `ThingOwner<Thing>` owned by the comp, unlimited stacks, insertion order kept). `CompTick()` ticks the contents only when `Props.shouldTickContents` (default true) and only runs if the parent's `Tick()` runs (`tickerType Normal`). The building itself is not an `IThingHolder`, so the game doesn't tick the contents otherwise. `PostDeSpawn` drops all contents `Near` the position (except `WillReplace`), so deconstruct/destroy needs no code of ours. Not loading → its only gizmo is `Command_LoadToTransporter` ("Load"). `CompProperties_Transporter`: `massCapacity` (default 150), `restEffectiveness`, `max1PerGroup`, `canChangeAssignedThingsAfterStarting`, `showOverallStats`, `shouldTickContents`, `showMassInInspectString`. `CompInspectStringExtra` shows "Contents: …".
- `ITab_ContentsTransporter : ITab_ContentsBase` (`container => Transporter.innerContainer`; visible when the container has items or loading is in progress). It inherits the base drop (`GenDrop.TryDropSpawn(..., Position + DropOffset, Near)`).
- `ThingOwner.Take(thing, count)`: with `count < stackCount` it splits off the count and leaves the original in place (order kept).
- `ThingOwner<T>` has no insert/move/sort API (adds append or merge into the first stackable entry; every add/remove maintains `holdingOwner` and notifies the owner). Zero-count things can't be added (`GetCountCanAccept` = 0; `stackCount 0` marks a destroyed thing). `InnerListForReading` returns the actual inner list. **Decision: don't reorder** (no remove/re-insert, no list manipulation). A failed delivery of a whole entry may move it to the end; that's accepted and logged. A separate order list would be possible if strict order is ever needed (not implemented).
- Vanilla unloading: `CompTransporter` cancel/unload and `PostDeSpawn` use `innerContainer.TryDropAll(parent.Position, map, ThingPlaceMode.Near)`; shuttles (`ShipJob_Unload`) use `TryDrop(thing, interactionCell, map, Near, …, validator: not fogged / downed pawn not onto a pawn)`. `Near` searches up to radius ~12.9 (`GenPlace.PlaceNearMaxRadialCells`).
- `WorkGiver_LoadTransporters` scans `ThingRequestGroup.Transporter` and uses `LoadTransportersJobUtility`. A job exists while `AnythingLeftToLoad`. `FindThingToLoad` picks the **closest reachable, reservable, non-forbidden** wanted thing anywhere on the map (including items in storage of any priority, Critical too; `lookInHaulSources: true`). Pawns that aren't free colonists (downed, prisoners, animals, …) are **carried** in by haulers. The job (`HaulToTransporter`) puts things straight into `comp.innerContainer`. There's no capacity check in the job; mass is only checked by the dialog.
- `Dialog_LoadTransporters(Map, List<CompTransporter>)`: Pawns and Items tabs (`TransferableOneWayWidget`), mass bar and caravan info (travel speed, food days, visibility), and it refuses when over mass capacity. Candidate lists come from `TransporterUtility.AllSendablePawns` / `AllSendableItems`, which are reusable for our own dialog too.
- Bills (`JobDriver_DoBill`) need **all** ingredients of one recipe available before a job starts. The pawn brings them to the bench and they're consumed together at the end (`RecipeWorker.ConsumeIngredient`). Recipe ingredient counts are fixed in the `RecipeDef`; the bill UI only edits the ingredient *filter*. Runtime-created defs aren't saved, so a bill would reference a def that doesn't exist after loading unless we recreate it.

### VFE conveyor compatibility (Building_Conveyor.cs)
- **Push (conveyor → elevator):** into any forward `Building_Storage`. It first merges into existing stacks on the storage cells by editing `stackCount` directly (~1418-1437: no `Accepts`, no notification), then uses `Accepts(item)` + `GenPlace.TryPlaceThing(Direct)` (~1441-1446). With the internal buffer the cell stays empty, so only the second path is used.
- **Pull (storage → conveyor):** only from `Building_FactoryHopper` or a `Building_Storage` with `PipeSystem.FactoryHopperExtension { isfactoryHopper = true }`. So the **output target must be a VFE hopper** next to the destination elevator. Do NOT mark the elevator itself as a hopper.
- VFE's hopper "allow taking" uses Harmony patches on `ForbidUtility.IsForbidden`; we don't need that, thanks to the buffer.

### Performance (Step A, in game)
- Other mods (Designator Shapes) can make the game call `PlaceWorker.AllowsPlacing` ~20000 times per frame. `listerBuildings.AllBuildingsColonistOf*` scans all colonist buildings; `listerThings.ThingsOfDef` is indexed.

## 3. Design

| Def | Class | Notes |
| --- | --- | --- |
| `DeepElevator_Elevator` (1x1, `Graphic_Single`, not rotatable) | `DeepElevator.Building_Elevator` | Steps A/B: `: Building`. Step C: `: Building_Storage` + interface re-implementation (`Accepts`), `CompTransporter` (container), `ITab_Storage` + contents tab, `surfaceType Item` |

Code:
- `Building_Elevator`: links, lifecycle, gizmos, inspect.
- `ElevatorUtil`: logging, level group, key, candidate search, later delivery.
- `LevelLabels` (Step B): `LevelLabel(map)` combines the providers (`ILevelLabelProvider { string ProviderName; string Tag; bool TryGetLabel(Map, out string label, out int sortValue) }`: `DeepRimLabelProvider`, `MultiFloorsLabelProvider`); `SortKey(map)` returns a `long` (DeepRim depth, MultiFloors level, map id); each registered at startup only when `ModsConfig.IsActive` and its members resolve (`GenTypes.GetTypeInAnyAssembly` + cached reflection members), then the vanilla fallback. Computed on demand, not cached (only the reflection members are cached).
- `Building_Elevator` implements `IRenameable` (Step B).
- `PlaceWorker_Elevator`: no `AllowsPlacing` override from Step B. `DrawGhost` + `DrawPlaceMouseAttachments` for the placement helper, with cached cell sets.
- Temporary probes (`ElevatorProfiler`, `ElevatorDiagnostics`, hitch detector in `MapComponent_ElevatorProbe`): **kept** (user decision; removal still pending).
- Gizmos are plain `Command_Action`.

Saved fields: `partner` (Reference), `partnerMap` (Reference), from Step B `customName` (Value). Step C adds buffer settings (slots, FIFO, merge; Value). The container is saved by `CompTransporter`; storage settings come from the base class. The pairing key is never saved (computed from position and map size).

Partner validity: `partnerMap != null && Find.Maps.Contains(partnerMap) && partner != null && !partner.Destroyed && partner.Spawned && partner.Map == partnerMap && partner.partner == this`.

UI: inspect string "Connected to <level>" / "Not connected" (+ candidates hint), items in buffer; gizmos Connect to… (when ambiguous), Unload, later Load/Cancel load; vanilla storage tab for the input filter.

## 4. Files

| Path | Contents |
| --- | --- |
| `DeepElevator.csproj` (SDK-style, `net472`), `DeepElevator.slnx` | repo root; OutputPath `DeepElevator\1.6\Assemblies\`; compiles `DeepElevator\Source\**\*.cs`; references Assembly-CSharp, UnityEngine.CoreModule and UnityEngine.IMGUIModule (for `GUI`) only; no mod DLLs |
| `DeepElevator/About/About.xml` | packageId `FT.deepelevator`; from Step B: no `modDependencies`; `loadAfter` DeepRim and MultiFloors (harmless when absent); description lists supported mods |
| `DeepElevator/loadFolders.xml` | `/`, `1.6` |
| `DeepElevator/Defs/ThingDefs/DeepElevator_Elevator.xml` | ThingDef |
| `DeepElevator/Languages/English/Keyed/DeepElevator.xml` | strings |
| `DeepElevator/Source/*.cs` | code |
| `DeepElevator/Textures/Things/Buildings/DeepElevator/DeepElevator.png` | 256x256 building sprite, also used as the build-menu icon (`DeepElevator_MenuIcon.png` is no longer referenced) |
| `DeepElevator/1.6/Assemblies/DeepElevator.dll` | build output |

`Examples/` (DeepRim and VFE source, MultiFloors assemblies) is git-ignored and not shipped. When shipping, also exclude `Source/`.

## 5. Assets

Available: `DeepElevator.png`. Later: gizmo icons (`UI/Unload.png`, `UI/Connect.png`, 64x64), `About/Preview.png` (640x360), optional disconnected/blocked look, optional send animation (overlay lerp or vanilla effecter), vanilla SoundDefs.

## 6. Verification

The AI builds with `dotnet build` (or MSBuild from VS 2026 Community). The human tests in game after each step. Use `dotnet ilspycmd` (local tool; run from the repo root) to check vanilla or mod behaviour when in doubt.

## 7. To do later / to decide later

**To do later**
- Harmony patches to clean up `Dialog_LoadTransporters` for the elevator: remove travel/food supply, destination and caravan info, and the mass bar if mass doesn't matter (Step E or polish).
- Load (manifest) gizmo disabled while the elevator is disconnected (Step E).
- Free colonists walking into the elevator by themselves (vanilla pods use a lord; check in Step E).
- Pause manifest loading while the buffer is full (no vanilla hook; e.g. temporarily moving `leftToLoad` aside), if the missing pause proves annoying.
- Pocket-map tests (results_stepB §7); broken label-provider fallback test.
- Remove the temporary probes; gate logs on `Prefs.DevMode`.
- Custom icons (Connect, Go to partner, Unload, buffer gizmos).
- Optionally prompt haulers when the elevator starts accepting again (e.g. `listerHaulables` notifications), if items in other storage react too slowly.

**To decide later**
- Building size: 2x2 (four intake cells) for parallel hauling (the game reserves each storage cell for one pawn).
- Whether requests (manifests) must respect the elevator's storage filter.
- Pawn and animal sending details (Step E): which pawns, spawn placement at the partner.
- Whether requests may take items from high-priority (e.g. Critical) storage.
