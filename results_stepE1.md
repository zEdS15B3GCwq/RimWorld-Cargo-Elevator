# CargoElevator – Step E1 results (transport loader, items only)

Implemented from `plan_stepE1.md` on branch `stepE`. Builds with 0 errors, 0 warnings. **Not tested in game yet** (the tests in `plan_stepE1.md` §8 are for the human). Other documentation files are untouched.

## 1. Commits on `stepE`

1. `Snapshot of working tree before step E1`: the uncommitted work of steps C/D and the plans that was in the working tree when this step started. Committed as one baseline so the E1 commits contain only E1 changes (the tracked `CargoElevator.dll` is part of it).
2. `elevator uses a plain container buffer`: `ThingOwner<Thing>` buffer, `IThingHolder` and `ISearchableContents`, new contents tab, loading state and orange light removed, transporter comp removed.
3. `transport loader building, def, place worker; elevator pulls from linked loaders`: everything else.

## 2. What was built

**Elevator** (`Building_Elevator`)
- Plain container: `buffer` field created in the constructor, saved as `innerContainer` (the name the transporter comp used). A guard re-creates it when a save has no such node.
- `DeSpawn` drops the buffer after the base call unless the mode is `WillReplace`. Log text fixed.
- Removed: transporter field and caching, the `Command_LoadToTransporter` filter, `IsManifestLoading`, the "loading, ..." status wrapper and key `CargoElevator_StatusLoading`, the orange blink, `OrangeLightIndex`, `orangeTexPath` (extension, both XML defs, diagnostics), `hasTransporterComp` in diagnostics. The `CargoElevator_orange*.png` files are kept and unreferenced. The `<comps>` transporter entry is removed from the base def.
- `ITab_ElevatorContents : ITab_ContentsBase` as specified (own `container`, `UseDiscardMessage` false, visible when the thing is the player's and the buffer is non-empty, `OnDropThing` → `DropFromBuffer`).
- New `Building_Elevator.Loaders.cs`: `linkedLoaders` (priority descending, stable), `RegisterLoader` / `UnregisterLoader` / `Notify_LoaderPriorityChanged`, `RoomFor`, `PullFromLoaders`, `CanPullFrom`, `LinkWaitingLoaders` (new elevator links the waiting loaders), `ReleaseLoaders` (after the base `DeSpawn`).
- `Tick` order: `base.Tick()`, `PullFromLoaders()`, then the `absorbPending` block, periodic work and `TrySend`.
- Hint added to `CargoElevator_ToggleInsertDesc`.

**Loader** (`Building_TransportLoader`, `CargoElevator_TransportLoader`, `PlaceWorker_TransportLoader`)
- As specified in §5. Saved: `partner`, `transferAllowed` (default true), `priority`. Runtime: cached comp, `lastPullTick`.
- Gizmos: vanilla cancel skipped by icon, "Load" disabled with a reason while not loading and not linked, our own cancel/unload, transfer toggle, priority menu, "Connect to..." with directions, dev gizmo.
- Texture `TransportLoader.png` is used as supplied.

## 3. Deviations from the spec (please review)

1. **No `buffer.DoTick()` in the elevator's `Tick`** (spec §6.1 asked for it). In decompiled 1.6, `Thing.DoTick` itself ticks the contents of every `IThingHolder` (`cachedHolder.GetChildHolders` → `GetDirectlyHeldThings().DoTick()`) unless it is an `IThingHolderTickable` that says no. That is also what `plan.md` §2 says ("never call `buffer.DoTick()` ourselves"). A second call would make buffered food rot twice as fast. The comment in `Tick` says so. Test 2 ("food in the buffer rots") will show whether this was right; if the rot rate looks normal, it is.
2. **Status part 3 ("elevator cannot accept") is narrower than the literal spec.** The spec says it applies when `!partner.CanPullFrom(this)` and there was no pull in 120 ticks. Read literally, an idle empty loader would always say "elevator cannot accept", and a loader waiting for the next haul would say it too (nothing to pull is not a refusal). Implemented: the transfer part is skipped when not loading and the container is empty (idle: nothing shown); "cannot accept" shows only if the elevator is not operational, or the container holds a non-pawn item and `partner.CanPullFrom(this)` is false; and no pull within the last 120 ticks. "not linked" and "transfer off" still show when idle, since they are first in the order (idle unlinked loader: `not linked`).
3. **Keys reused instead of new ones**: the status line is prefixed with the elevator's existing `CargoElevator_StatusUnlinked` ("Status: {0}"); the connect menu entries use `CargoElevator_LabelWithTag` ("{0} ({1})") for "<elevator label> (<direction>)"; `CargoElevator_LoaderNeedsLink` is also the disabled reason of the transfer toggle. New keys are listed in the language file under "Step E1".
4. **Labels next to a loader use `LabelCap`, not the level label.** The elevator and the loader are on the same map, so `LevelLabels.ElevatorLabel` (map id and level) would say nothing. This affects the link message, "Linked to:", the connect menu and the placement text "Connects to: ...".
5. **Small additions**: `ElevatorUtil.ElevatorDefs` (all defs whose class is an elevator, found on first use; used by the loader place worker and now also by diagnostics), and `PlaceWorker_Elevator.DrawMouseTextLine` became `internal` and is shared. Placeholder icons for the loader's transfer toggle and priority button are in `ElevatorTextures` (`TexCommand.SelectCarriedThing`, `TexButton.ReorderUp`).

## 4. Notes and things to watch in the tests

- **Log volume**: every pull logs an "Absorbed (loader)" line from `AddToBuffer`, as every absorbed item does. A large manifest will produce many lines. Not gated (logging stays unconditional in this phase).
- **Tie order after loading a save**: loaders of equal priority register in spawn order, not in the original link order. Pulling still works the same; only which of two equal-priority loaders is emptied first can differ after a reload. The spec accepted this ("spawn order doesn't matter").
- **Vanilla cancel button skip** relies on `Command_Action.icon == CompTransporter.CancelLoadCommandTex` (reference equality). Spec test 17 checks that there is no duplicate button.
- **Pull skips pawns**; a pawn put into the loader through the dialog's Pawns tab stays until the load is cancelled (cancel drops it via the vanilla `CancelLoad`). Test 21.
- `Notify_PartnerLost` shows a message ("lost its elevator ... nothing is transferred until you turn ... back on"). Loader messages are shown on link and on loss only; `DeSpawn` and map removal are silent.
- Vanilla wording that stays (spec §9): "Transport pod loading complete. Launch when ready.", the started message, "because the shuttle has been destroyed".

## 5. Suggested plan updates for the reviewing agent

- `plan.md` §3/§4: new files `Building_Elevator.Loaders.cs`, `Building_TransportLoader.cs`, `PlaceWorker_TransportLoader.cs`, `CargoElevator_TransportLoader.xml`; `ITab_ElevatorContents` now derives from `ITab_ContentsBase`; `ElevatorUtil.ElevatorDefs`.
- `plan.md` §2: the "thing holders tick every tick" fact is what makes the elevator's buffer rot without code of ours; it holds for the new `IThingHolder` building too (deviation 1).
- Status table in `plan.md` §1 Step D: the "loading" rows and the orange light no longer exist.
