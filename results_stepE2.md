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
- **Exit logic on pocket maps** is handled by two Harmony patches, added after the first hand-over (§6). They are not tested in game; test 1 now checks that nothing uses the loader as an exit.
- `Notify_Pulled(Thing)` keeps its signature and parameter although it now only records the tick.

## 5. Suggested plan updates for the reviewing agent

- `plan.md` §3/§4: new files `LoaderContainerHolder.cs`, `LoaderContainerProxy.cs`, `ITab_LoaderContents.cs`; the loader is a `MapPortal`, no transporter comp; `ElevatorTextures.LoaderEnter`.
- `plan.md` §7: pawns and animals arrive directly through the vanilla portal path (no buffer, slots or send rate); routing them through the elevator stays a later option.
- Language keys: `DeepElevator_LoaderPairDown`, `DeepElevator_MsgLoaderPawnReturned`, `DeepElevator_LoaderUnloadIncomplete` added; `DeepElevator_LoaderStatusComplete` removed.

## 6. Loader excluded from exit searches (added after hand-over)

**Decision (human):** the loader is never an exit. It is not a means of transportation for pawns that nobody sent: pawns could end up on a level they could not otherwise reach and, as elevators only link pairs, might never get away. Pawns use it only on request (portal dialog order, right-click "Enter"). This also covers guests and traders, even with the pair working.

**What searches for an exit** (everything in the decompiled game that touches `ThingRequestGroup.MapPortal`, `JobDefOf.EnterPortal` or `CarryDownedPawnToPortal`; none of the example mods use these either):
1. `JobGiver_ExitMap.TryGiveJob`: on a pocket map (`!CanEverExit`), `EnterPortal` on the first reachable MapPortal, without checking `IsEnterable`. Subclasses `JobGiver_ExitMapBest`, `JobGiver_ExitMapNearDutyTarget` and `JobGiver_ExitMapRandom` only override `TryFindGoodExitDest`; `JobGiver_ExitMapFollowOverseer` (player mechs escorting their overseer) calls the base method. All of them are covered by one patch.
2. `RCellFinder.TryFindExitPortal`: the nearest MapPortal on a pocket map. Its only caller is the right-click "Carry to exit" (`FloatMenuOptionProvider_CarryPawnToExit`), whose job `CarryDownedPawnToPortal` then enters that portal.
3. Not exit searches, left alone: `WorkGiver_HaulToPortal` (the order), `JobGiver_EnterPortal` and `LordToil_LoadAndEnterPortal` (duty from the order's lord, set only by the dialog), `FloatMenuOptionProvider_EnterMapPortal` (explicit right-click on the loader), `PocketMapExit` and the generation steps (their own portals).

**Patches** (new file `Source/LoaderExitPatches.cs`; the project already applies its Harmony patches with `PatchAll`):
- `PortalExitRules.IsExcludedFromExitSearch(Thing)`: the one rule, currently `portal is Building_TransportLoader`. Extend it for future buildings.
- `Patch_JobGiver_ExitMap_TryGiveJob` (postfix): if the result is `EnterPortal` on an excluded portal, it is replaced by the same job for the first reachable portal that is not excluded, or by no job.
- `Patch_RCellFinder_TryFindExitPortal` (postfix): if the result is excluded, the nearest portal that is not excluded is used, or `false` (vanilla then shows "no path").
- Both only change the result when vanilla picked a loader, so nothing else changes. No log lines: the AI giver runs often and would flood the log.

**Not covered:** other mods that choose exit portals themselves. A mod that sends a pawn straight into the loader would still work, because the loader remains an ordinary enterable portal.

**Test 1 now reads:** with a linked loader on a level (pair working and pair down), no pawn that wants to leave (guest, trader, released prisoner or slave, escorting mech) uses the loader; "Carry to exit" picks a stairs portal, or shows "no path" when there is none.

## 7. Changes after the first in-game tests

From the human's test report (44 numbered observations). Not re-tested yet.

**Fixed or changed**
- **Test 11, 24: Enter command rules.** A new order needs a linked elevator that is connected to its partner (`partner.IsConnected`); otherwise the command is disabled with the reason `DeepElevator_LoaderNeedsLink` or the new `DeepElevator_LoaderElevatorNotConnected`. While an order is in progress the command stays enabled whatever the link says, so the order can be changed or cancelled. An unpowered pair does not disable it (the order waits for power). Implemented in `ApplyEnterCommandRules`.
- **Test 24: "started 10 jobs in one tick".** Cause: `JobGiver_EnterPortal` gives `EnterPortal` without checking `IsEnterable`; the job fails at start and is given again in the same tick. Reproducible whenever a pawn is in the order while the pair is disconnected or unpowered (including a power loss during an order, or changing an order while disconnected), so the gizmo rule alone does not cover it. Fixed with a postfix on `JobGiver_EnterPortal.TryGiveJob` (`LoaderEnterPatches.cs`): for a loader that is not enterable it gives no job, so the pawn idles and starts walking when the pair works again. This is the fourth Harmony patch (with the two exit patches and the existing store-cell patch). Untested: what exactly the pawns do while they wait.
- **Test 17: emptied order stays.** Cause: vanilla's remove ("x") button in the order tab only sets the entry's count to zero; `LoadInProgress` counts entries, so the tab and the cancel button stay. `Building_TransportLoader.PruneFinishedOrder` removes finished entries and cancels the order (and its lord) when none are left. It runs right after the tab is drawn (`ITab_LoaderOrder`) and every 250 ticks as a safety net. Not fixed: vanilla's remove button for a pawn looks for the wrong lord job class (`LordJob_LoadAndEnterTransporters`), so removing one pawn of several does not take that pawn out of the lord and it may still walk in; removing the last entry ends the lord.
- **Test 13, 21: wording, no patch.** The tab is `ITab_LoaderOrder : ITab_ContentsMapPortal` with its own `labelKey` ("Loading"). The cancel button text is the portal's virtual `CancelEnterString` ("Cancel loading"); the list header is `EnteringString` ("Still to be sent").
- **Test 29: no automatic relink.** The loader has a saved flag `autoLinkAllowed`, true until the loader is linked once (any link, also a manual one, and set on load for a loader that has a partner). After a loss it is only linked by "Connect to...". `Notify_PartnerLost` no longer tries to relink. Test 37 (transfer toggle off after a loss) is consistent with this and unchanged.
- **Test 5, 40: elevator shows its loaders.** The status line closes with ", loaders: N" (linked or not). Selecting the elevator draws a line to each linked loader and selecting a loader draws one to its elevator (vanilla facility style). The elevator's gizmos include the loader's build command (`BuildCopyCommandUtility.FindAllowedDesignator`), like the "build linkable facility" buttons of beds. The loader is not a game facility, so this is a plain gizmo; no facility comp is involved.
- **Test 8: loader status line.** Idle, linked, transfer on now says "transfer on" (key `DeepElevator_LoaderStatusTransferOn`); once linked, "priority: <level>" ends the line (`DeepElevator_LoaderStatusPriority`). The transfer part is now never empty.
- **Test 7: priority icons.** Intended placeholder. `ElevatorTextures.LoaderPriorityIcon(level)` loads `Textures/Things/Buildings/DeepElevator/DeepElevatorLoader_Priority<Level>.png` with Level one of Low, Normal, Preferred, Important, Critical; a missing file keeps the placeholder icon.

**Answered, no change**
- Test 16, 22: the drop lands on or next to the loader (the loader is not storage and not an elevator cell), and nothing absorbs it again. Expected.
- Test 41: the loader priority only orders the elevator's pulls. Hauling into the loader is vanilla (order, work giver priority). Expected.
- Test 38, 39: the exit patches (§6) are still not proven; test 39 is weak evidence only.
- Not wired yet: `DeepElevatorLoader_ConnectToIcon.png` and `DeepElevatorLoader_TransferToElevator.png` exist in the textures folder but the code still uses placeholders for them.

## 8. Changes after the second round of tests

From the human's second report (notes 1-3, tests 1-11). Builds with 0 errors. Not re-tested in game.

**Changed**
- **Loading look (note 3).** The loader now has two looks: `DeepElevatorLoader.png` normally and `DeepElevatorLoader_orange.png` while an order is in progress (`LoadInProgress`), switched live. Done like the elevator's state light: the def uses `drawerType MapMeshAndRealTime`, the base sprite stays in the map mesh, and `Building_TransportLoader.DrawAt` draws the orange sprite over it (full sprite, same canvas) while loading. The path comes from a new def extension, `LoaderSettingsExtension.loadingTexPath` (`Source/LoaderSettingsExtension.cs`). The loading code was shared: `ElevatorUtil.LoadOverlayGraphic` replaces the elevator's private `LoadLightGraphic`, which now calls it (same behaviour; the warning text says "overlay texture" instead of "light texture"). No redraw call is needed because `DrawAt` runs every frame.
- **Elevator status line (test 3).** "loaders: 2" became the directions of the linked loaders from the elevator, clockwise from north, e.g. "loaders: N, ESE". The direction is the 16-point compass point nearest to the offset between the two centres (`Building_Elevator.DescribeLoaderDirections`). For the 1x1 elevator that gives N, E, S, W; for the 2x2 the in-between points NNW, NNE, ENE, ESE, SSE, SSW, WSW, WNW (a loader on a 2x2 side sits about 18 degrees off the side's middle, which rounds to those). Each letter is a language key (`DeepElevator_CompassN/E/S/W`).
- **Enter command wording (test 4).** The command's label and description are replaced in `ApplyEnterCommandRules` ("Open transport menu", `DeepElevator_LoaderOpenMenu` / `...Desc`); no patch. The icon is still the placeholder, and the enabling rules are unchanged.
- **Selection lines removed (test 10).** I had added the lines between loader and elevator (results §7, test 5/40) and nothing depends on them, so both `DrawExtraSelectionOverlays` overrides are gone. The elevator's status-line part, and the build-loader gizmo, stay.

**Postponed (note 2).** All gizmo and icon work, including `DeepElevatorLoader_ConnectToIcon.png`, `DeepElevatorLoader_TransferToElevator.png` and the five priority icons, waits until the human supplies the full set. Stock icons stay.

**Note 1: the loader's enter patch and the elevator's patch.** The elevator's is `Patch_StoreUtility_IsGoodStoreCell` (`HarmonyPatches.cs`): the haul search offered the elevator as a store cell although `Accepts` refused, so the hauling job failed at once and was given again ("started 10 jobs in one tick"). The loader's `Patch_JobGiver_EnterPortal_TryGiveJob` has the same cause and the same fix shape: a job giver proposes a job whose target refuses, the job fails at start, the think tree repeats it; a postfix that only turns a proposal into "no proposal" when the target says no (`AcceptsPawnDelivery`, `IsEnterable`). They are not mergeable into one patch: they patch different vanilla methods (a bool cell test used by the haul search, and a job giver returning a `Job`), with different signatures and unrelated rules. What they share is the idea, and the rule is already in one place per target (`AcceptsPawnDelivery`, `IsEnterable`), so there is nothing to deduplicate. If a third such case appears, they could move into one `JobRefusalPatches.cs` file with a shared header comment; I left them where they are.

**Confirmed by the tests**
- Tests 1, 2, 5, 6, 8, 11: as expected. Test 4: the Enter command's disabled state follows the elevator's connection, not power. Test 7: no automatic relink after a loss, "Connect to..." works (the intended rule from §7). Test 8: pawn orders pause when the elevator is lost, resume when a connected elevator is linked, and the walking job is cancelled when the distant partner is destroyed; the order stays in the Loading tab.

**Not testable by the human: loader as an exit (test 9).** Whether pawns on a pocket map would pick the loader as an exit cannot be reproduced in game. It stays untested (the two exit patches from §6 are unproven in practice). For the plan: add it to the untested features section; `plan.md` line 328 ("Exit logic on pocket maps ... Decide after test 1 of E2") should become "decided: the loader is excluded from exit searches (two Harmony patches, E2 §6); not verified in game".

## 9. Changes after the third round of tests

Builds with 0 errors. Not re-tested in game.

- **Orange look rules (tests 1).** `Building_TransportLoader.DrawAt` / `ShowsOrangeLook`: an order in progress (`LoadInProgress`) shows constant orange; no order but something in the container shows orange and normal alternating (one full blink per `WaitingBlinkPeriodTicks` = 60 game ticks, half orange, half normal; it stops while the game is paused); nothing to send and nothing waiting shows the normal look. The orange texture is now `DeepElevatorLoader_Orange` (the def path was updated by the human to match the renamed file).
- **Elevator status line (test 3).** Now "<count> loaders: <directions>", e.g. "2 loaders: N, ESE" (`DeepElevator_StatusLoaders` takes the count and the directions). One loader uses its own key, `DeepElevator_StatusLoaderSingle`: "1 loader: N".
- **Patches (test 2).** The elevator's and the loader's Harmony patches stay separate.
