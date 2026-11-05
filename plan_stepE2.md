# CargoElevator – Step E2: portal loader (vanilla portal dialog; pawns and animals)

Handoff spec for the implementing agent. Background: `plan.md`, `plan_stepE1.md`, `results_stepE.md`, `comparison_multifloors.md`. RimWorld 1.6 only.
The human tests in game and makes decisions about implementation.
Log findings, decisions and anything beyond this spec in `results_stepE2.md`.
**Do not edit other documentation files**, such as `plan.md`, `plan_stepE1.md` and `plan_stepE2.md`. Log all updates in the results document, including decisions that change the original plan. Another agent will review them and fold them into the plan documentation.

Build with MSBuild (0 errors) before handing over.

Logging stays unconditional, like the existing code (`ElevatorUtil.Log`).

Continue on the git branch `stepE`, with step-by-step commits. E1 is implemented there (commits up to `eafc3e0`) but untested; E2 changes that code instead of reverting it.

## 1. Goal

The transport loader stops being a transport pod and becomes a vanilla **map portal**, the same base MultiFloors' stairs use. The player picks what to send in the vanilla portal dialog (`Dialog_EnterPortal`).

- **Items:** hauled into the loader's own container, pulled by the elevator and sent as before. Buffer, slots, send rate and storage placement at the destination don't change.
- **Pawns and animals (this is the whole of E2's pawn scope):** vanilla portal behaviour. Walking colonists and colony mechs walk in; animals, prisoners and downed pawns are carried in. They arrive at the partner elevator on the other level directly, without the buffer, slots or send rate. They're gated only by `IsEnterable` (§5.3). Routing them through the elevator later is noted in `plan.md` §7.
- **Right-click "Enter":** kept as a feature, so pawns can take the elevator.
- **Elevator:** no changes.
- **No Harmony patches.** No custom dialog, no column changes: the vanilla portal dialog is used as it is (mass column only, no rot or market value).

This replaces the earlier E2 idea (pawns through pull, slots and send on the manifest).

## 2. Terms

As in `plan_stepE1.md` §2, plus:
- **Order:** the portal's `leftToLoad` list (vanilla), set by the dialog.
- **Loader container:** the loader's own `ThingOwner<Thing>`. Items wait there until pulled. The portal itself has no storage.
- **Proxy:** what vanilla hauling deposits into (`MapPortal.containerProxy`, returned by `GetDirectlyHeldThings`).

## 3. Verified vanilla facts (decompiled 1.6)

**Portal and proxy**
- `MapPortal : Building, IThingHolder`. Public fields `leftToLoad`, `containerProxy` (type `PortalContainerProxy`, set in `SpawnSetup` after base), `notifiedCantLoadMore`, `exit`. `LoadInProgress` = `leftToLoad?.Any()`. `ExposeData` saves `pocketMap`, `exit`, `beenEntered` and `leftToLoad`, and removes entries whose things are gone on load.
- Virtual: `EnterTex` (protected), `EnterString`, `CancelEnterString`, `EnteringString`, `AutoDraftOnEnter`, `IsEnterable(out string)`, `GetOtherMap()`, `GetDestinationLocation()`, `OnEntered(Pawn)`. `GetChildHolders` and `GetDirectlyHeldThings` are public but **not** virtual. `GetChildHolders` is empty.
- Base `GetOtherMap()` generates a pocket map when there is none. Base `OnEntered` dereferences `def.portal` and `exit`, so it throws without portal def props. **Override both and never call their base.** `def.portal` is otherwise only used for pocket maps and `PocketMapExit`, neither of which applies.
- Base `IsEnterable` asks every comp (`CanEnterPortal`). Base `GetGizmos` yields: the Enter command (icon `EnterTex`, label `EnterString + "..."`, opens `Dialog_EnterPortal`, `Disabled = !IsEnterable`), the Cancel command while `LoadInProgress` (`CancelLoad`, label `CancelEnterString`), and a pocket-map view command only when a pocket map exists.
- `CancelLoad()` removes the `LordJob_LoadAndEnterPortal` lord and calls `leftToLoad.Clear()`. **It throws if `leftToLoad` is null**, so only call it when `LoadInProgress`.
- `MapPortal.Tick`: every 60 ticks while loading, if nobody can load anything, one message `MessageCantLoadMoreIntoPortal` (text uses our label).
- `MapPortal` does not override `DeSpawn`. On removal it leaves the lord and order as they are, so we cancel ourselves.
- `PortalContainerProxy : ThingOwner`: `Count` 0. `TryAdd(item, count, merge)` calls the virtual `TryAdd(item, merge)`. That calls `portal.Notify_ThingAdded(item)` (subtracts `item.stackCount` from the order, matched by `TransferableMatchingDesperate`), then `GenDrop.TryDropSpawn` near `GetDestinationLocation()` on `GetOtherMap()`. **Notify must run before the item is added anywhere:** a merge can consume the item.
- `ThingRequestGroup.MapPortal` matches any def whose `thingClass` is a `MapPortal`, so the work giver finds our loader without def changes.

**Hauling and entering**
- `WorkGiver_HaulToPortal`: Hauling work type, priority 105. Any colonist with Hauling enabled hauls while the order isn't empty, with no lord needed. `HasJobOnPortal`: order non-empty, manipulation, reachable, `FindThingToLoad` finds something. It does **not** check `IsEnterable`. Jobs set `ignoreForbidden`.
- `JobDriver_HaulToPortal : JobDriver_HaulToContainer`: **90-tick deposit wait per trip** (hard-coded `Duration`; accepted by the human), then `DepositHauledThingInContainer`. That finds the target through `IThingHolder.GetDirectlyHeldThings()`, which is the proxy.
- Carried in (`FindThingToLoad`): pawns that are not colonists or colony mechs (animals, prisoners), or downed or self-shut-down ones. They're deposited into the proxy like items.
- Walking in: `EnterPortalUtility.MakeLordsAsAppropriate` puts selected colonists and colony mechs (not downed) into `LordJob_LoadAndEnterPortal`. They help haul, then `JobDriver_EnterPortal`: `FailOn(!IsEnterable)`, walk to touch, wait 90 ticks, then `DeSpawnOrDeselect` and `GenSpawn` on `GetOtherMap()` at `GetDestinationLocation()`. If that cell isn't standable, it uses `StandableCellNear(5)`; failing that, the message `UnableToEnterPortal`. Then `OnEntered(pawn)`, draft restore, `inventory.UnloadEverything = !otherMap.IsPocketMap`, and the lord's `Notify_PawnLost(ExitedMap)`.
- Right-click (`FloatMenuOptionProvider_EnterMapPortal`): drafted and undrafted, multi-select, mechs. Greyed out with "Cannot enter {0}: <reason>" when `IsEnterable` is false. The option text is `EnterString`.

**Dialog**
- `Dialog_EnterPortal`: `forcePause`. Title `EnterString`. Pawns and Items tabs. Columns fixed in a private method: mass; for pawns also equipped weapon. Reopening shows the remaining order plus map items plus items being carried there, so loaded or pulled items can't be counted again. Accept replaces the order and rebuilds the lord. No mass limit.
- Items listed: `CaravanFormingUtility.AllReachableColonyItems(map, isPocketMap, isPocketMap)`. On a non-pocket map, only items in the home area or in storage are listed (§8 test).

**Tabs, ticking, searches**
- `ITab_ContentsMapPortal` (title "Travel"): lists the order, visible only while `LoadInProgress`. Usable directly in the def.
- `ITab_ContentsBase`: abstract `container`. Each row has a drop button calling the virtual `OnDropThing(thing, count)`, which by default drops near `SelThing.Position + DropOffset`.
- `Thing.DoTick` (tickerType Normal) of an `IThingHolder` ticks `GetDirectlyHeldThings()` **and every child holder from `GetChildHolders`**, every tick. Held items throttle their own rare and long ticks. This is why the elevator's buffer rots without code of ours.
- Pawns only take items out of buildings that are `IHaulSource` (bookcases, outfit stands). Child holders are used for ticking, wealth (`WealthWatcher`), quest item checks and the map search, not for hauling.

**Exit logic on pocket maps (main risk)**
- `JobGiver_ExitMap.TryGiveJob`: on a pocket map that `!CanEverExit`, it gives `EnterPortal` on the **first reachable thing in `ThingRequestGroup.MapPortal`**, without checking `IsEnterable`. Users include leaving guests and traders, and fleeing raiders. A loader can be picked: if enterable, a non-colony pawn rides to the partner level; if not, the job fails on start and repeats.
- `RCellFinder.TryFindExitPortal` (right-click "Carry to exit" for downed pawns on pocket maps) takes the **nearest** `MapPortal`, so it may pick a loader.
- MultiFloors levels and vanilla pocket maps are pocket maps. DeepRim layers aren't checked (§8).
- **There is no non-patch fix**: `IsEnterable` has no pawn argument. If §8 test 1 shows the problem, stop and report; the human decides (possibly one patch).

## 4. Decisions

| Topic | Decision |
| --- | --- |
| Base class | `Building_TransportLoader : MapPortal, IThingHolder` (interface re-implemented for `GetChildHolders` only) |
| Dialog | Vanilla `Dialog_EnterPortal` as it is. No patches, no copy |
| Items | Into the loader container through our proxy, then pulled as in E1 |
| Pawns and animals | Vanilla: carried and walking ones arrive at the partner elevator directly (§1) |
| Right-click "Enter" | Kept |
| Deposit time | Vanilla 90 ticks per haul trip, accepted |
| Container ticking | Automatic, through the child holder (§5.2). No `DoTick` call of ours |
| Opening the dialog | Requires a link, regardless of power (same as E1). Pawns in an order then wait until `IsEnterable` is true |
| `IsEnterable` | Linked, the partner elevator is operational (both ends powered), plus the base comp checks. **Planner decision, to confirm:** the "Transfer items to elevator" toggle gates items only, as its label says; it doesn't gate pawns |
| Carried pawn while not enterable | Hauling doesn't check `IsEnterable`, so a carried pawn can be deposited while the pair is down. Then: put it down next to the loader, subtract it from the order, log, and show a message. No retry loop (planner decision) |
| Cancel | Vanilla cancel gizmo (`CancelLoad`): ends the lord and clears the order. Items already in the loader stay; our Unload drops them |
| Load end | Automatic: the order empties. No finish check |
| Drops | Unload, the contents-tab drop and the despawn drop all avoid elevator cells (`ElevatorUtil.UnloadValidator`). Loaders stand next to elevators, so a plain "near" drop could land on the intake cell |
| Wording | Vanilla portal strings ("Enter transport loader", "Cancel entering", "Travel" tab). Wording changes are for later |
| Old saves | E1 is untested, so no migration. Loaders saved with the E1 transporter comp may log errors on load; deconstruct them first |

## 5. Loader

### 5.1 Def (`Defs/ThingDefs/CargoElevator_TransportLoader.xml`)

- Remove `CompProperties_Transporter` and the whole `<comps>` block.
- `inspectorTabs`: `ITab_ContentsMapPortal`, then `CargoElevator.ITab_LoaderContents`.
- Everything else stays: `thingClass`, size, `tickerType` Normal (needed for ticking), graphics, cost, place worker. No `<portal>` props.
- Description: the loader sends a chosen list of items, and people and animals, through the adjacent elevator. Items ride the elevator; people and animals step out at the other end. Needs no power.

### 5.2 Container and child holder

- New small class `LoaderContainerHolder : IThingHolder` (in `Building_TransportLoader.cs` or its own file), about 15 lines. It holds `ThingOwner<Thing> container` (owner = the holder), `ParentHolder` = the loader, `GetDirectlyHeldThings()` = the container, `GetChildHolders` = `ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, container)`.
- The loader creates the holder (and through it the container) in its constructor. `Container` (public, as now) returns it.
- `void IThingHolder.GetChildHolders(List<IThingHolder> outChildren)` on the loader adds the holder. **Do not** re-implement `GetDirectlyHeldThings`: deposits must keep going to the proxy, or the order never shrinks.
- Saving: `Scribe_Deep.Look(ref <container>, "innerContainer", holder)`, with a `PostLoadInit` guard that re-creates an empty container when the node is missing (as the elevator does).
- Comment the re-implementation, like the elevator's `Accepts`: why only `GetChildHolders`.

### 5.3 Class changes (`Source/Building_TransportLoader.cs`)

`public class Building_TransportLoader : MapPortal, IThingHolder`

**Removed:** the `CompTransporter` field, its caching and warning; `FinishLoadIfComplete`; the vanilla-cancel skip; the `Command_LoadToTransporter` handling; `transporter.Notify_ThingRemoved`; the "loading complete" status part. Update the class summary.

**Proxy** (new class `LoaderContainerProxy : PortalContainerProxy`, `Building_TransportLoader loader`):
```csharp
public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
{
    if (item is Pawn pawn)
    {
        if (loader.IsEnterable(out _))
        {
            return base.TryAdd(pawn, canMergeWithExistingStacks); // vanilla: dropped next to the partner elevator
        }
        // Not enterable (pair down): put down next to the loader, take off the order, message (§4).
        ...
        return true;
    }
    portal.Notify_ThingAdded(item); // before adding: a merge can consume the item
    if (!loader.Container.TryAdd(item, canMergeWithExistingStacks)) { warn; drop near the loader with the validator; }
    return true;
}
```
Log each item deposit ("Deposited (loader): ...") and each pawn sent. In `SpawnSetup`, after `base.SpawnSetup`: `containerProxy = new LoaderContainerProxy { portal = this, loader = this };`.

**Overrides**
- `GetOtherMap()`: the partner elevator's partner's `Map`, or `null` when not linked or the partner elevator isn't connected. Never base.
- `GetDestinationLocation()`: the partner elevator's partner's `Position`, or `IntVec3.Invalid`. Vanilla finds a standable cell within 5 cells.
- `IsEnterable(out reason)`: base first (comps), then not linked → `CargoElevator_LoaderNeedsLink`; `!partner.IsOperational` → new key `CargoElevator_LoaderPairDown` ("the elevator is not connected or has no power"). Reasons appear on the right-click option and the disabled gizmo.
- `OnEntered(Pawn pawn)`: `Notify_ThingAdded(pawn)` (no-op for a right-click entry, since the order is null or doesn't contain the pawn) and a log line with the destination. Never base.
- `EnterTex`: the vanilla "UI/Commands/LoadTransporter" texture, cached in `ElevatorTextures`.

**Gizmos** (`GetGizmos`): pass base gizmos through, with one change. The Enter command (`Command_Action` whose `icon == EnterTex`; it's our cached reference, so the match is reliable) gets `Disabled = partner == null` with reason `CargoElevator_LoaderNeedsLink`, instead of vanilla's `!IsEnterable` (decision: link only). Vanilla's Cancel stays as is. Then:
1. **Unload all** (replaces cancel/unload): shown while the container has items. Vanilla `CommandUnload`/`CommandUnloadDesc` texts, `CompTransporter.CancelLoadCommandTex` icon. It drops every entry with `Container.TryDrop(entry, Position, Map, ThingPlaceMode.Near, out _, null, cell => ElevatorUtil.UnloadValidator(cell, Map, entry))`; whatever finds no room stays. It doesn't touch the order.
2. Transfer toggle, priority, Connect to..., dev gizmo: unchanged.

**Lifecycle**
- `DeSpawn(mode)`: `ReleasePartner`, then `if (LoadInProgress) CancelLoad();`, then drop the container with the validator. Anything left after that goes down with a plain `TryDropAll` near the loader, so nothing is deleted. Then base.
- `Notify_MyMapRemoved`: as now.
- `Tick`: `base.Tick()` (vanilla's can't-load message), then link validation as now.
- `Notify_Pulled(Thing)`: only sets `lastPullTick`.

**Inspect and status**
- Loading part: `loading` while `LoadInProgress`, otherwise omitted.
- Transfer part: as now, with `isLoading = LoadInProgress`.
- Keep the pawn skips in `ElevatorCannotAcceptNow` and the elevator's pull as safety, even though pawns no longer reach the container.

**Dev state log**: replace the transporter fields with `LoadInProgress`, the order entry count and `notifiedCantLoadMore`.

### 5.4 Contents tab (new file `Source/ITab_LoaderContents.cs`)

`ITab_LoaderContents : ITab_ContentsBase`, built like `ITab_ElevatorContents`:
- `container` → `((Building_TransportLoader)SelThing).Container`.
- `IsVisible`: the loader is the player's and the container isn't empty.
- `labelKey` `TabTransporterContents` ("Contents"), `containedItemsKey` `ContainedItems`, `UseDiscardMessage` false.
- `OnDropThing(thing, count)`: take `count` out of the container and put it down with `ElevatorUtil.TryDropNear` (skips elevator cells). If there's no room, put it back and log. Log the drop.

### 5.5 Placement

`PlaceWorker_TransportLoader`: unchanged.

## 6. Elevator

No changes. `PullFromLoaders`, `RoomFor`, `CanPullFrom`, priorities, links and the transfer toggle work through `loader.Container`, `Notify_Pulled` and the link API, which keep their signatures.

## 7. Language keys

New: `CargoElevator_LoaderPairDown`. Unused afterwards: `CargoElevator_LoaderStatusComplete`. Remove it, or log in the results why it stays. Update the def description (§5.1).

## 8. Tests for the human

Tests marked (E1) repeat `plan_stepE1.md` §8 on the new base.

**Exit logic first (stop and report if it fails)**
1. On a MultiFloors level (pocket map) with a linked, powered loader: guests or traders leave, and raiders flee. Watch whether any pick the loader. Repeat with the pair unpowered: no pawn stuck retrying at the loader. "Carry to exit" for a downed pawn on that level: which portal does it choose? Repeat on a vanilla pocket map if available.
2. DeepRim layer: does the dialog list loose items outside the home area (such as fresh ore)? Is the layer a player home?

**Portal behaviour**
3. Loader beside an elevator: Enter gizmo opens the vanilla dialog. Unlinked: disabled with the link reason. Linked but unpowered: opens. The game pauses; no caravan bar; mass column.
4. Items: hauled in (about 1.5 s wait per trip), shown in "Contents", pulled by the elevator respecting slots and priority, sent at the normal rate, placed in storage at the destination. Order shrinks; load ends by itself. (E1 10–14)
5. Rot: food waiting in the loader rots at the same rate as nearby on the floor (not double, not zero). Wealth includes items in the loader.
6. Change the order mid-load (reduce, add): already loaded items aren't counted again. Cancel: lord ends, order cleared, items in the loader stay; Unload all drops them off the elevator cells.
7. Transfer toggle off: items stay in the loader; walking pawns still enter (confirm the decision in §4).
8. Pawns: select a colonist in the dialog → they help haul, walk in, appear next to the partner elevator on the other level, drafted state kept. An animal and a downed colonist → carried in, appear at the partner.
9. Pawns with the pair down: the right-click option is greyed out with the reason; walking pawns in an order wait; a carried pawn is put down next to the loader with a message, with no retry loop. Watch for the vanilla "can't finish entering" message.
10. Right-click "Enter" on the loader: drafted, undrafted, several pawns, a mech. Blocked destination (wall the partner in): vanilla "destination is blocked".
11. Contents tab: drop one item; it lands next to the loader, not on the elevator.
12. Destroy the loader mid-load: lord ends, items drop (not on the elevator), haulers stop. Save and reload mid-load: order, container and links survive. (E1 8, 19)
13. Linking, partner lost, priority, status strings, placement ghost. (E1 4–8, 20)

## 9. Known and accepted

- Pawns and animals don't ride the elevator: no travel time, no rate, no buffer (`plan.md` §7 has how to change it).
- No rot or market value column. Vanilla portal wording.
- Items waiting in the loader are invisible to `GetDirectlyHeldThings` callers (by design: that's the proxy). Ticking and searches see them through the child holder.
- Exit logic on pocket maps is untested (§8 test 1).
