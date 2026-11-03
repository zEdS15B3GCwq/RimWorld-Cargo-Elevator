# DeepElevator – Step E2 results (portal loader: vanilla portal dialog; pawns and animals)

Implemented from `plan_stepE2.md` on branch `stepE`. Builds with MSBuild (`dotnet msbuild`, Release): 0 errors, 0 warnings. **Not tested in game yet** (the tests in `plan_stepE2.md` §8 are for the human). Other documentation files are untouched.

## 1. Commits on `stepE`

1. `Docs before step E2`: the uncommitted plans and results that were in the working tree when this step started (`plan_stepE2.md`, `results_stepE1.md`, `comparison_multifloors.md`, plan updates). Committed first, so the E2 commit holds only code.
2. `E2: transport loader becomes a vanilla map portal`: everything below (includes the rebuilt, tracked `DeepElevator.dll`).

## 2. What was built

**Loader** (`Building_TransportLoader : MapPortal, IThingHolder`)
- `CompTransporter` removed from the def (the whole `<comps>` block). `inspectorTabs`: `ITab_ContentsMapPortal`, then `DeepElevator.ITab_LoaderContents`. New description. Nothing else in the def changed; no `<portal>` props.
- Removed from the class: transporter field and caching, `FinishLoadIfComplete`, the vanilla-cancel skip, the `Command_LoadToTransporter` handling, `transporter.Notify_ThingRemoved`, the "loading complete" status part and its key.
- Portal overrides, none of which call a base that would need portal def props: `GetOtherMap` (`partner?.Partner?.Map`), `GetDestinationLocation` (the partner's partner's `Position`, else `IntVec3.Invalid`), `IsEnterable` (base comps, then link, then `partner.IsOperational`; new key `DeepElevator_LoaderPairDown`), `OnEntered` (`Notify_ThingAdded` plus a log line), `EnterTex` (`ElevatorTextures.LoaderEnter`).
- Gizmos: the base gizmos are passed through. The Enter command (matched by `icon == EnterTex`) gets `Disabled = partner == null` with the link reason, so the dialog opens while the pair is unpowered. Vanilla's Cancel stays. "Unload all" (vanilla texts and icon) shows while the container has items and drops with `ElevatorUtil.UnloadValidator`. Transfer toggle, priority, Connect to..., dev gizmo are unchanged.
- `DeSpawn`: release the partner, `CancelLoad()` if `LoadInProgress`, drop the container with the validator, then a plain `TryDropAll` for anything left, then base. `Notify_MyMapRemoved` as before. `Tick`: `base.Tick()` then link validation. `Notify_Pulled` only sets `lastPullTick`.
- Status: `loading` while `LoadInProgress`; the transfer part is unchanged and uses `isLoading = LoadInProgress`. Dev log shows `LoadInProgress`, the order entry count and `notifiedCantLoadMore`.

**New classes**
- `LoaderContainerHolder : IThingHolder` (own file): holds the `ThingOwner<Thing>`, `ParentHolder` is the loader, child holders come from `AppendThingHoldersFromThings`. It also saves the container as `innerContainer` (guard re-creates it when the node is missing). The loader creates it in its constructor and registers it through the explicit `IThingHolder.GetChildHolders` only (commented why; `GetDirectlyHeldThings` stays the proxy).
- `LoaderContainerProxy : PortalContainerProxy` (own file), set in `SpawnSetup` after the base call:
  - Pawn, pair working: logs, then `base.TryAdd` (vanilla: `Notify_ThingAdded`, drop next to the partner elevator).
  - Pawn, pair down: `Notify_ThingAdded` (off the order), put down near the loader with the validator, warning in the log and a message (`DeepElevator_MsgLoaderPawnReturned`). No retry.
  - Item: `Notify_ThingAdded` first, then `Container.TryAdd`; on failure a warning and a validator drop near the loader. Each deposit is logged ("Deposited (loader): ...").
- `ITab_LoaderContents : ITab_ContentsBase` as specified; its drop calls the loader's `DropFromContainer`.

**Elevator**: no changes (`PullFromLoaders`, `CanPullFrom` and the pawn skips work through `Container`, which keeps its name; its type is now `ThingOwner<Thing>`).

## 3. Deviations and additions (please review)

1. **Two extra language keys** beyond the spec: `DeepElevator_MsgLoaderPawnReturned` (the message for the "put down next to the loader" decision, §4) and `DeepElevator_LoaderUnloadIncomplete` (shown by "Unload all" when something finds no room; it is the loader's twin of the elevator's `DeepElevator_UnloadIncomplete`, whose text says "elevator"). `DeepElevator_LoaderStatusComplete` is removed, as the spec allowed.
2. **`DeSpawn` with `DestroyMode.WillReplace` does not drop the container** (same as the elevator's buffer: the thing lives on). The load is still cancelled. The spec did not cover this mode.
3. **Drop logic lives on the loader** (`DropContainerContents`, `DropFromContainer`), shared by the Unload command, the despawn drop and the contents tab. `DropFromContainer` puts the portion back when there is no room (merges into the original entry if only part was taken, else re-adds without merging) and logs a warning.
4. **Holder and proxy are in their own files** (the spec allowed the holder in `Building_TransportLoader.cs`).
5. **Enter gizmo reason**: `disabledReason` is the resolved `DeepElevator_LoaderNeedsLink` text; it is set to null when enabled.

## 4. Notes and things to watch in the tests

- **Container ticking relies on the explicit `IThingHolder.GetChildHolders`** being reached by `Thing.DoTick` through the interface (as the elevator's buffer does). Test 5 (rot at the same rate as on the floor) confirms it; if food does not rot in the loader, this is where to look.
- **Deposit and the order**: `Notify_ThingAdded` runs before the add, as specified. A merge into an existing container entry is therefore counted once. Test 6 (changing the order mid-load) checks that.
- **Transfer off keeps accepting hauled items.** The order and the work giver ignore the toggle, so items keep being hauled into the loader and wait there, while pawns still walk in (§4 planner decision to confirm, test 7).
- **Partner lost mid-order**: items are still deposited; walking pawns wait (`IsEnterable` false); a carried pawn is put down with the message. The existing "lost its elevator" message text still fits.
- **Exit logic on pocket maps is untested** (§8 test 1). Nothing in the code addresses it. If a pawn picks the loader as its exit portal, stop and decide on a patch; none exists in this step.
- Old E1 saves: a loader saved with the transporter comp may log errors on load (no migration, as agreed).
- `Notify_Pulled(Thing)` keeps its signature and parameter although it now only records the tick.

## 5. Suggested plan updates for the reviewing agent

- `plan.md` §3/§4: new files `LoaderContainerHolder.cs`, `LoaderContainerProxy.cs`, `ITab_LoaderContents.cs`; the loader is a `MapPortal`, no transporter comp; `ElevatorTextures.LoaderEnter`.
- `plan.md` §7: pawns and animals arrive directly through the vanilla portal path (no buffer, slots or send rate); routing them through the elevator stays a later option.
- Language keys: `DeepElevator_LoaderPairDown`, `DeepElevator_MsgLoaderPawnReturned`, `DeepElevator_LoaderUnloadIncomplete` added; `DeepElevator_LoaderStatusComplete` removed.
