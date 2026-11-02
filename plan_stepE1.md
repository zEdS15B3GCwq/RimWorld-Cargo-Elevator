# DeepElevator – Step E1: transport loader (manifest loading, items only)

Handoff spec for the implementing agent. Background: `plan.md`. RimWorld 1.6 only.
The human tests in game and makes decisions about implementation.
Log findings, decisions and anything beyond this spec in `results_stepE.md`.
**Do not edit other documentation files** , such as `plan.md` and `plan_stepE.md` - updates should all be logged in the results document, including decisions that modify the original plan. These will be reviewed by another agent and incorporated into the plan documentation.

Build with MSBuild (0 errors) before handing over.

Logging stays unconditional, like the existing code (`ElevatorUtil.Log`).

As a first step, create a new git branch "stepE" and create step-by-step commits as you proceed with implementation.

## 1. Goal

Manifest loading moves off the elevator into a new building, the **transport loader**: in effect a vanilla transport pod that can't launch. It stands next to an elevator, and the elevator pulls finished items out of it.

- **Elevator:** storage plus one plain container (its buffer). It loses its transporter, "loading" (manifest loading) state, and related orange light texture (texture file will be removed by human).
- **Loader:** vanilla `CompTransporter`: vanilla Load dialog, loading jobs and two-column contents tab. Needs no power.
- **Relationship:** think of a conveyor putting items on the elevator. The only special rules are that the elevator pulls from the loader, ignores its own filter and "allow insert" when pulling, and pulls before it absorbs from its cells.
- **Scope:** E1 = items only. E2 (later) = pawns and animals. No temporary guards for pawns: the dialog's Pawns tab stays, pull skips pawns, and a pawn loaded during testing stays inside until the load is cancelled.
- **No new Harmony patches.** No column hiding in the dialog for now. No ASF support.

## 2. Terms

- **Normal delivery:** a pawn or conveyor puts an item on an elevator cell; it is absorbed into the buffer. Never counts toward a manifest.
- **Loading delivery:** a pawn carries a manifest item into a loader's container.
- **Pull:** the elevator moves items from a linked loader's container into its buffer.
- **Linked:** a loader's permanent reference to one adjacent elevator.

## 3. Verified vanilla facts (decompiled 1.6)

- `CompTransporter`: `LoadingInProgressOrReadyToLaunch` = `groupID >= 0`. `AnythingLeftToLoad` = `FirstThingLeftToLoad != null` (also false when the listed things no longer exist).
- **Vanilla cancel button:** one `Command_Action` (icon `CompTransporter.CancelLoadCommandTex`, public static), shown only while loading **and** with a non-empty container. Label `CommandCancelLoad` while items are left, otherwise `CommandUnload`. Both call `CancelLoad()`.
- `CancelLoad(map)` = `TryRemoveLord` + `CleanUpLoadingVars` (`groupID = -1`, `TryDropAll`, clears the list). No message. `PostDeSpawn` cancels and drops by itself (with a destroyed message).
- **Nothing is delivered after a load ends:** `JobDriver_HaulToContainer` fails on `TransporterUtility.WasLoadingCanceled`. `FindThingToLoad` subtracts hauls in progress, and the list only shrinks on deposit, so the list can't be empty while a needed haul is running.
- `Dialog_LoadTransporters`: `forcePause = true`, so the game is paused while it is open and nothing is pulled meanwhile. `CheckForErrors` allows an empty manifest when `canChangeAssignedThingsAfterStarting` is true, which leaves a loading state with an empty list. `TryAccept` sets `groupID` and the list in one call, so no tick runs between them. A reopened dialog shows remaining list + container + things being hauled; pulled items are not in it.
- `showOverallStats = false` removes the caravan info bar. The launch and fuel checks are skipped when there is no `CompLaunchable`.
- `ITab_ContentsTransporter`: left "Items to load", right container, visible while loading or non-empty. Can be used directly in the def.
- `ITab_ContentsBase`: abstract `container`, `DoItemsLists` draws one list; `OnDropThing` is virtual. There is no vanilla tab for a building's own container, so it must be subclassed.
- `ThingOwner<T>.TryAdd(item, canMerge: true)` fills all existing stackable entries up to the stack limit, then adds the rest as a new entry. It knows nothing about our slots. **So merge order already works as wanted; only the slot limit is new** (§6.4).
- `CompTransporter` saves `innerContainer` directly in the thing's save node. The elevator's own container under the same name should load old buffers (test).
- Manifest loading ignores storage priority, and the transporter has none. `StoragePriority` + `.Label()` (`StoragePriorityHelper`) are public.

## 4. Decisions (human)

| Topic | Decision |
| --- | --- |
| Name | "transport loader". 1x1, not rotatable, single graphic. A linked building of our own, not a vanilla facility link |
| Placement | No refusal; green shading on cells next to elevators. Adjacent = cardinal only, no diagonals |
| Links | 1 elevator : many loaders; 1 loader : 1 elevator. Permanent while both exist. "Exactly one candidate" links automatically, otherwise the "Connect to..." gizmo. Auto-unlink when the elevator is gone |
| Power | The loader needs none, and has no power-related state |
| Starting a manifest | Requires a link, regardless of the elevator's state |
| Partner lost | Loading continues. Manifest change, cancel and unload still work. The transfer toggle turns off and stays off after relinking |
| Transfer toggle | "Transfer items to elevator". Default on for a new loader, including one with no link yet |
| Pull conditions | Basically, elevator pulls from loader under the same conditions when it would absorb items dropped on its cells: connected and powered on both ends, with room. Being blocked does not stop pulling while there is room. |
| Pull rules | No rate limit. Ignores the elevator filter and "allow insert". No overfilling from loaders. Loaders before cells |
| Loader priority | Same levels as storage priority. Elevator pulls from higher priority first; ties in list order |
| Load end | When nothing is left to load and the container is empty: end the load. No launch button of any kind |
| Manifest change while running | Allowed; pulled items are not refunded. **Also while transfer is on:** the human considered forbidding this if the list could go stale, but the dialog pauses the game (§3), so it can't |
| Cancel button | Our own always; vanilla's hidden, because items appearing briefly would make it flicker |
| Elevator | No loading state or orange; contents tab with the plain buffer list; no "Contents:" inspect line (held items line and tab are enough) |
| Dialog | `showOverallStats` off. No column hiding for now |

## 5. Loader

### 5.1 Def (new file `Defs/ThingDefs/DeepElevator_TransportLoader.xml`)

`DeepElevator_TransportLoader`, parent `BuildingBase`:

- `thingClass` `DeepElevator.Building_TransportLoader`; `size` (1,1); `rotatable` false; `tickerType` Normal; `altitudeLayer` Building; `passability` PassThroughOnly; `fillPercent` 0.4; `pathCost` 42; `designationCategory` Misc; `canOverlapZones` false; `constructEffect` ConstructMetal.
- `graphicData`: `Graphic_Single`, `texPath` `Things/Buildings/DeepElevator/TransportLoader`, `drawSize` (1,1).
- `inspectorTabs`: `ITab_ContentsTransporter`.
- `placeWorkers`: `DeepElevator.PlaceWorker_TransportLoader`.
- Cost and stats (first values, tunable): Steel 50, ComponentIndustrial 1, WorkToBuild 800, MaxHitPoints 150, Mass 20, Flammability 0.
- `CompProperties_Transporter`: `massCapacity` 100000, `max1PerGroup` true, `canChangeAssignedThingsAfterStarting` true, `shouldTickContents` true, `showMassInInspectString` false, `showOverallStats` false.
- Description: says it loads a chosen list of items and passes them to the adjacent elevator; needs no power.

**Texture:** `DeepElevator/Textures/Things/Buildings/DeepElevator/TransportLoader.png` is in place (supplied by the human). Use it as it is.

### 5.2 Class (new file `Source/Building_TransportLoader.cs`)

`public class Building_TransportLoader : Building`

Saved: `Building_Elevator partner`; `bool transferAllowed = true`; `StoragePriority priority = StoragePriority.Normal`. **Only the loader saves the link.**

Runtime: `CompTransporter transporter` (cached in `SpawnSetup`), `int lastPullTick = -99999`.

Public: `Partner`, `TransferAllowed`, `Priority`, `Container` (`transporter.innerContainer`), `Notify_Pulled(Thing item)` (sets `lastPullTick`; calls `transporter.Notify_ThingRemoved(item)`, which keeps the mass cache correct).

**Lifecycle**
- `SpawnSetup`: base; cache the comp. If `respawningAfterLoad`: `partner?.RegisterLoader(this)` (references are resolved before spawning; this only touches a runtime list, so spawn order doesn't matter). Otherwise `TryAutoLink()`.
- `DeSpawn`: `partner?.UnregisterLoader(this); partner = null;` before base (vanilla then cancels and drops).
- `Notify_MyMapRemoved`: same clean-up, no relinking.
- `Tick`: `base.Tick()` (the comp ticks the contents), then the finish check, then every `Building_Elevator.PeriodicInterval` ticks validate the link: partner destroyed, not spawned, on another map, or no longer adjacent means `Notify_PartnerLost()`.

**Finish check** (every tick; runs regardless of link and toggle):
```csharp
if (transporter.LoadingInProgressOrReadyToLaunch && !transporter.AnythingLeftToLoad && !transporter.innerContainer.Any)
{
    transporter.CancelLoad(Map); // nothing to drop: same two steps as the end of a launch
}
```
Log it as load finished, not cancelled.

**Linking**
- `Candidates()`: distinct `Building_Elevator`s on `GenAdj.CellsAdjacentCardinal(this)`, spawned, excluding one being removed.
- `TryAutoLink()`: exactly one candidate and no partner: `LinkTo(candidate)`. It does not change `transferAllowed`.
- `LinkTo(elevator)`: set `partner`, `elevator.RegisterLoader(this)`, log, message `DeepElevator_MsgLoaderLinked`.
- `Notify_PartnerLost()`: `partner = null; transferAllowed = false;`, log, message `DeepElevator_MsgLoaderUnlinked`, then `TryAutoLink()`. Items stay in the container.

**Gizmos** (`GetGizmos`). Pass through base gizmos, with two changes:
- Skip the vanilla cancel: `gizmo is Command_Action { icon: var icon } && icon == CompTransporter.CancelLoadCommandTex`. Check this works in game.
- `Command_LoadToTransporter`: if not loading and `partner == null`, call `.Disable("DeepElevator_LoaderNeedsLink".Translate())`. While loading it stays enabled, also with no partner.

Then add:
1. **Cancel/unload**: shown while loading or when the container has items. Label and description use vanilla's keys: `CommandCancelLoad`/`CommandCancelLoadDesc` when `AnythingLeftToLoad`, else `CommandUnload`/`CommandUnloadDesc` (with `LabelShort`). Icon `CompTransporter.CancelLoadCommandTex`. Action: `SoundDefOf.Designate_Cancel.PlayOneShotOnCamera()`; if loading `transporter.CancelLoad()`, otherwise `Container.TryDropAll(Position, Map, ThingPlaceMode.Near)` (safety path).
2. **Transfer items to elevator**: `Command_Toggle` on `transferAllowed`; disabled with a reason while not linked. Placeholder icon.
3. **Priority**: `Command_Action` opening a `FloatMenu` of `Low, Normal, Preferred, Important, Critical` (`priority.Label()`); on change call `partner?.Notify_LoaderPriorityChanged()`. The label shows the current level. Placeholder icon.
4. **Connect to...**: when no partner and at least one candidate. Reuse the `DeepElevator_Connect` label and `ElevatorTextures.Connect`; a new description key. Same-map neighbours can show the same level label, so the menu shows `LabelCap` plus the direction from the loader (north/east/south/west, own keys; `Rot4.FromIntVec3` of the adjacent cell minus `Position`). Re-check the state in the option's action, as `OpenConnectMenu` does.
5. Dev gizmo `DEV: log loader state` behind `DebugSettings.ShowDevGizmos`.

**Inspect string**: base text, then:
- `Linked to: <elevator label> (<direction>)` when linked (planner addition, needed when two elevators are adjacent).
- A status line joined with `; `, with whichever parts apply. **No elevator states such as buffer full, no power or blocked.**
  - Loading part: `loading` (items left) or `loading complete` (nothing left, items still inside). Omitted when not loading.
  - Transfer part, first match wins:
    1. `not linked`
    2. `transfer off`
    3. `elevator cannot accept`: `!partner.CanPullFrom(this)` and no pull within the last 120 ticks.
    4. `transferring items`: while loading or the container has items.
    5. Otherwise omitted (idle).
  - The 120-tick grace keeps the line steady: in steady transfer the buffer is full between sends, and without it the line would flicker.
  - Example: `loading complete; not linked`.

### 5.3 Placement (new file `Source/PlaceWorker_TransportLoader.cs`)

`AllowsPlacing` is not overridden. `DrawGhost`: green `GenDraw.DrawFieldEdges` around all cells cardinally adjacent to spawned elevators on the map. Cached and rebuilt every 60 frames, with the same pattern as `PlaceWorker_Elevator.EnsureCache`, plus an `ElevatorProfiler.Measure`. `DrawPlaceMouseAttachments`: for the hovered cell, "Connects to: <label>" for one candidate (reuse `DeepElevator_HelperConnectsTo`), or a new line for several: "Several elevators: use Connect to...". Sharing code with the elevator's worker is not expected, because that one searches other maps; move small helpers such as `DrawMouseTextLine` only if it stays simple.

## 6. Elevator changes

### 6.1 Plain container

`Building_Elevator` gets `IThingHolder` and `ISearchableContents` (added to the class list; `SearchableContents => Buffer`).

- Field `private ThingOwner<Thing> buffer;`, created in a constructor: `new ThingOwner<Thing>(this)`. `Buffer` returns it and is never null, so remove the null checks and the `"no buffer"` / "cannot be buffered" branch for the null case (keep the pawn check).
- `GetDirectlyHeldThings() => buffer`; `GetChildHolders(...)` => `ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, buffer)`.
- `ExposeData`: `Scribe_Deep.Look(ref buffer, "innerContainer", this);` (same name as the transporter's; old buffers should load).
- `Tick`: add `buffer.DoTick();` after `base.Tick()` and fix its comment (rot).
- `DeSpawn`: keep the map, call base, then unless `mode == DestroyMode.WillReplace`: `buffer.TryDropAll(Position, map, ThingPlaceMode.Near)`. Dropping after base keeps the items off the elevator's own storage, and matches when the comp used to do it. Fix the "vanilla drops them" log text.
- Remove: `transporter` field, `CacheTransporter`, the `Command_LoadToTransporter` filter, the transporter comp in the XML, `hasTransporterComp` in `ElevatorDiagnostics`.
- Without the comp the "Contents:" inspect line goes away. **Do not replace it.** Fix the `DescribePausedTransfer` comment that mentions it.
- Update the class comments that mention the transporter comp.

### 6.2 Contents tab

`ITab_ElevatorContents : ITab_ContentsBase`: `container => ((Building_Elevator)SelThing).Buffer`; `UseDiscardMessage => false`; `IsVisible` = player faction and buffer non-empty (same as today's non-loading behaviour); constructor sets `labelKey = "TabTransporterContents"`, `containedItemsKey = "ContainedItems"`. Keep the existing `OnDropThing` → `DropFromBuffer`.

### 6.3 Loading state removal

Remove: `IsManifestLoading`, the "loading, ..." wrapping in `DescribeStatus`, key `DeepElevator_StatusLoading`, the orange blink and `ManifestBlinkHalfPeriodSeconds`, `OrangeLightIndex`, the orange slot in the light array, `orangeTexPath` (extension, XML, diagnostics). **Keep the `DeepElevator_orange*.png` files:** the human may repurpose them. They are simply no longer referenced.

### 6.4 Pull (new file `Source/Building_Elevator.Loaders.cs`)

Runtime only (not saved): `List<Building_TransportLoader> linkedLoaders`, ordered by priority descending (`OrderByDescending` is stable, so ties keep link order). Rebuild it only on register, unregister and priority change.

```csharp
// Items a slot-limited buffer can still take of this thing. The order (fill stacks, then a new entry) is TryAdd's.
private int RoomFor(Thing thing)
{
    int freeSlots = Mathf.Max(0, maxSlots - UsedSlots());
    int room = freeSlots * thing.def.stackLimit;
    if (mergeStacks)
    {
        for each buffer entry that CanStackWith(thing):
            room += Mathf.Max(0, entry.def.stackLimit - entry.stackCount);
    }
    return room;
}

private void PullFromLoaders()
{
    if (linkedLoaders.Count == 0 || !IsOperational) return;
    for each loader in linkedLoaders with TransferAllowed:
        ThingOwner container = loader.Container;
        for (index = 0; index < container.Count; )
        {
            Thing item = container[index];
            int count = item is Pawn ? 0 : Mathf.Min(item.stackCount, RoomFor(item));
            if (count <= 0) { index++; continue; } // another kind may still merge
            bool takesAll = count == item.stackCount;
            Thing portion = container.Take(item, count);
            loader.Notify_Pulled(portion);
            AddToBuffer(portion, "loader");
            if (!takesAll) index++;
        }
}

/// True if this elevator could take something from the loader right now.
public bool CanPullFrom(Building_TransportLoader loader) => IsOperational and a non-pawn item in loader.Container has RoomFor > 0;
```
Do not use `GetRefusalReason`/`Accepts` here: they apply the filter, and they answer yes to any merge, which would overfill.

Do not mark the transfer active on pull (active power follows sends).

**Order in `Tick`:** after the `base.Tick()` / `buffer.DoTick()` lines, call `PullFromLoaders()` **before** the `absorbPending` block and before `TrySend`.

Why this gives loaders precedence without any event logic: `Notify_ReceivedThing` only sets `absorbPending`, and event and backstop both run `AbsorbIntake` from this same `Tick`. So the event never absorbs on its own, and a pull in the same tick always comes first. A buffer kept full by loaders makes `AcceptsPawnDelivery` refuse new hauls. **Unchanged:** a haul already on its way is still absorbed into a full buffer and can overfill it (existing comment in `AbsorbIntake`).

**Register/unregister:** `RegisterLoader`, `UnregisterLoader`, `Notify_LoaderPriorityChanged` maintain the list.

**Elevator lifecycle:**
- New elevator (`SpawnSetup`, not loading): for each adjacent loader with no partner, call `TryAutoLink()`.
- `DeSpawn`: after base (so this elevator no longer counts as a candidate), for a copy of the list call `loader.Notify_PartnerLost()`, then clear the list.
- `Notify_MyMapRemoved`: clear the list only; the loaders go with the map.
- Keep this independent of the elevator-elevator `partner` link: a loader can be linked to a disconnected elevator.

### 6.5 Allow insert hint

Add to `DeepElevator_ToggleInsertDesc`: "Transport loaders are not affected: this elevator still pulls from them."

### 6.6 Keep unchanged

`Patch_StoreUtility_IsGoodStoreCell`, `AcceptsPawnDelivery`, the Unload gizmo, sending, holds, power. Elevator's Unload drops the buffer; a loader may immediately refill it (expected, not a bug).

## 7. Language keys (new)

`DeepElevator_MsgLoaderLinked`, `DeepElevator_MsgLoaderUnlinked`, `DeepElevator_LoaderNeedsLink`, `DeepElevator_ToggleTransfer` + `Desc`, `DeepElevator_LoaderPriority` + `Desc`, `DeepElevator_LoaderConnectDesc`, `DeepElevator_LoaderLinkedTo`, `DeepElevator_LoaderStatusLoading`, `...StatusComplete`, `...StatusNotLinked`, `...StatusTransferOff`, `...StatusCannotAccept`, `...StatusTransferring`, direction keys, `DeepElevator_HelperSeveralElevators`. Wording of vanilla pod messages is not changed (§9).

## 8. Tests for the human

**Elevator without transporter**
1. Normal hauling, send, power, lights as before. No orange, no "loading". No "Contents:" line.
2. Contents tab: one list; dropping an item works and lands off the intake cell. Food in the buffer rots.
3. Unload works. Deconstruct with a full buffer: items drop. Save and reload with a full buffer. Load a save made **before** this step: buffer contents survive.

**Linking**
4. Ghost shows green next to elevators; building elsewhere is allowed.
5. Loader beside one elevator: auto-link. Diagonal: no link. Beside two: "Connect to..." with directions.
6. Elevator built beside waiting loaders: they link. Several loaders on one elevator, also on the 2x2.
7. Destroy the elevator: load continues, toggle off. New elevator: links, nothing moves until the toggle is turned on.
8. Save/reload: links, toggle and priority kept, elevator pulls without relinking.

**Loading**
9. Load dialog: no caravan bar. Disabled when never linked. Start a manifest with the elevator unpowered.
10. Items are hauled into the loader and pulled right away, respecting slots; merge fills the partial stack first, with the rest in a new slot. Slots never exceed the limit because of a pull (a hauled item may).
11. Elevator filter empty and "allow insert" off: pull still works. Hint text shows it.
12. No power, partner unpowered, disconnected: no pull, status `elevator cannot accept`, manifest continues. Blocked with free slots: pulls until full, then stops.
13. Hauling and manifest together: while a loader has items, the buffer stays full and new hauls wait.
14. Two loaders with different priorities: higher one empties first. Same priority: no errors.
15. Load finishes: state ends by itself; nothing further is hauled; status empty. Empty manifest accepted: ends at once.
16. Toggle off: items stay; `loading complete; transfer off` at the end; button says "Unload all" and drops them.
17. Cancel button while the container is empty (pull on) and while it holds items: works, no flicker. No duplicate vanilla button.
18. Change the manifest mid-load: dialog shows only what's left. Reduce and add. Pulled items not refunded, no errors. Also after losing the partner.
19. Destroy the loader mid-load: items drop, haulers stop.
20. Status strings: all states from §5.2, no elevator states.
21. Pawn in the dialog (E1): stays in the loader, not pulled, cancel releases it; no errors.

## 9. Known and accepted

- Vanilla wording stays for now (low priority, `plan.md` §7): "Transport pod loading complete. Launch when ready." (shown when the last item reaches the loader), the started message, and "because the shuttle has been destroyed".
- Dialog columns (mass, market value, nutrition, rot) stay visible for now.
- E2 (pawns and animals) gets its own spec: placement rules from the earlier discussion, slots, lord handling on load end.
