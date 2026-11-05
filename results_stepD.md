# Step D results – 2x2 elevator, power, status, state lights

Build: `dotnet build CargoElevator.csproj` (Debug) → 0 errors, 0 warnings. `CargoElevator\1.6\Assemblies\` contains only `CargoElevator.dll` and `.pdb`. Nothing committed. **Not run in the game**: the human checklist in `plan_stepD.md` §9 is still to do.

## 1. What was implemented

### Defs (`Defs\ThingDefs\CargoElevator_Elevator.xml`)
- Abstract `CargoElevatorBase` holds everything shared, including `drawerType MapMeshAndRealTime`. The children hold the per-size lists only (`statBases`, `costList`, power comp, `modExtensions`).
- `CargoElevator_Elevator` (1x1): power 200 W active / 10 W idle, 4 slots, 60 ticks per stack.
- `CargoElevator_ElevatorLarge` ("large cargo elevator"): `CargoElevator_large` sprite, Steel 250 + 6 components (placeholder), power 300 / 30 W, 8 slots, 15 ticks per stack.
- Light overlay paths are set in each extension (`CargoElevator_green/red/orange`, `…_large`).

### Code
| File | Change |
| --- | --- |
| `ElevatorSettingsExtension.cs` | `activeHoldTicks` (+`ActiveHoldTicks`), `blockedAfterSendIntervals`, light texture paths |
| `CargoElevatorDefOf.cs` | `CargoElevator_ElevatorLarge` |
| `ElevatorUtil.cs` | `ElevatorAnchoredAt(cell, map, def)`; `UnconnectedCandidates` uses it with `self.def`, so no pairing across sizes |
| `PlaceWorker_Elevator.cs` | cache keyed by `(map, def)`; `AddTargets` takes the def and outlines every footprint cell, cursor text stays keyed by the anchor |
| `ElevatorDiagnostics.cs` | loops over all defs whose `thingClass` is a `Building_Elevator`; also logs size, drawerType, power and the new fields |
| `Building_Elevator.Power.cs` (new) | power draw, `IsPowered` / `IsOperational`, blocked state, `ElevatorStatus` enum and `Status`, status description and logging, state light drawing |
| `Building_Elevator.cs` | `CachePower()` in `SpawnSetup`; periodic power update and status logging in `Tick`; new status line in the inspect string; `LogLinkState` uses `ElevatorAnchoredAt` |
| `Building_Elevator.Buffer.cs` | `Accepts` / `ShouldAbsorb` refuse for "no power" and "partner unpowered"; `HoldReason.NotConnected` removed from the paused-line list |
| `Building_Elevator.Send.cs` | `TrySend` bails out when not operational (clears holds, logs `Send paused` on change); `MarkTransferActive()` on each successful send; `HoldReason.NotConnected` removed; `blockedSinceTick` set in `SetHold` and reset in `ClearHold` |
| `Languages\English\Keyed\CargoElevator.xml` | removed the three old keys, added the status keys |

### Design notes
- **Power:** a transfer sets `lastActiveTick` on both ends; each end is "active" while the last transfer is within `ActiveHoldTicks` (= `ticksPerStack + 30` by default). The comp's `PowerOutput` is written only on change. A def with no `idlePowerDraw` uses its active draw as idle.
- **Status:** `Disconnected → NoPower → PartnerUnpowered → Blocked → Active → Idle`, first match wins. The status is logged on change from the periodic tick only.
- **Light:** the base sprite stays in the map mesh. `DrawAt` draws the light graphic at half an altitude step above it. No light when disconnected or unpowered; green when operational; red when blocked; orange blinks in real time while `IsManifestLoading` (never true in this step). Light graphics are cached per def and loaded on the first draw. A missing texture is logged once and that light is skipped.
- Nothing beyond the spec was added (no VFE, manifest, Harmony, settings, on/off switch).

## 2. Deviations / things to check
- `UnloadValidator` still uses `GetFirstThing<Building_Elevator>` on purpose: it must reject any cell of any elevator footprint, which is the wanted behaviour for 2x2.
- `PlaceWorker_Elevator` de-duplicates footprint cells with `List.Contains`. The lists are tiny (a few elevators), and it is cached once per 60 frames.
- The two old off-state textures (`CargoElevator_off*.png`) are deleted in the working tree; defs reference only `CargoElevator.png` / `CargoElevator_large.png` plus the light PNGs.
- `ContentFinder<Texture2D>.Get(path, reportFailure: false)` is used to check light textures before creating the graphic.

## 3. Test results
Only the compile check was done (see top). The in-game checklist (§9 of `plan_stepD.md`) has not been run; results to be filled in by the human:

| # | Item | Result |
| --- | --- | --- |
| 1–10 | see `plan_stepD.md` §9 | not run |

## 4. Post-test changes (human)
- Power values changed in the defs: 1x1 100 W active / 10 W idle, large 300 W / 30 W. Code reads them from the def, so no code change.
- Defs and textures can't end in a digit: the 2x2 def is now `CargoElevator_ElevatorLarge` (`CargoElevatorDefOf.CargoElevator_ElevatorLarge`), and its textures are `CargoElevator_large` and `CargoElevator_{green,red,orange}_large`. Defs and the DefOf were updated; no other code referenced these names.

## 5. Second round of fixes
1. **Status text:** when the elevator is both unpowered and disconnected the status shows "no power, disconnected" (new key `CargoElevator_StatusUnpoweredDisconnected`). Powered and disconnected still shows "disconnected". The `ElevatorStatus` enum, light and behaviour are unchanged (disconnected still wins there); only `DescribeStatus` combines the texts.
2. **Menu icons:** both defs now set `uiIconPath` to `CargoElevator_MenuIcon` / `CargoElevator_MenuIcon_large`. The base sprite is still used for blueprint, ghost and the map.
3. **"started 10 jobs in one tick" (HaulToCell):**
   - **Cause:** the vanilla haul search (`StoreUtility.TryFindBestBetterStoreCellForWorker`) picks a destination cell using only the storage filter (`Settings.AllowedToAccept`) and cell checks. It never calls the building's `Accepts`. The hauling job's goto toil then fails at once, because it calls `IsValidStorageFor`, which does call `Accepts`. The pawn ends the job, finds the same haul, and repeats until the game aborts after 10 jobs in one tick. So any time hay (or anything the filter allows) could be hauled to an elevator whose `Accepts` is false, it looped. It was not caused by a new Step D refusal as such: the "not connected" refusal from Step C behaves the same way when the filter allows the item.
   - **SUPERSEDED / REVERTED (see §10):** the `HaulDestinationEnabled` override described next was incomplete and has been removed again; the Harmony patch of §10 replaces it. Original description follows for the record. `HaulDestinationEnabled` (checked by the haul search before it looks at a storage group) was hidden with `new` in `Building_Elevator.Power.cs`. It is true only when the elevator is operational (connected, both ends powered) and the buffer has a free slot or a partly filled entry that merging can top up. The interface re-declarations on the class make the game's calls bind to it.
   - **Known residual case:** buffer completely full of slots, with a partly filled entry present, and an allowed item that does not match that entry: `HaulDestinationEnabled` is true but `Accepts` is false, so the same loop can still happen. Not fixed (it needs a per-item hook, e.g. Harmony, which is out of scope); to be tested/observed.
   - **Not run in the game.** Test: elevator unpowered/disconnected with hay allowed and hay on the map → no errors, pawns ignore the elevator. Then connect and power both → hauling resumes.

## 6. Follow-up on item 5.3 (haul loop): evidence and open question
- **Verified from the decompiled game code (1.6):** `StoreUtility.TryFindBestBetterStoreCellFor` skips a group only if `parent.HaulDestinationEnabled` is false; per group `TryFindBestBetterStoreCellForWorker` uses `Settings.AllowedToAccept` (filter) + `IsGoodStoreCell`, and `IsGoodStoreCell` does **not** call `Accepts`. `HaulAIUtility.HaulToCellStorageJob` doesn't either. `JobDriver_HaulToCell`'s goto toil fails the job when `B.IsValidStorageFor(map, thing)` is false, and that calls `parent.Accepts`. So a building whose `Accepts` is false while its filter allows the item gets chosen and the job dies on its first toil: exactly the trace (every toil `ReadyForNextToil` in one tick, then `EndJobWith`, then `TryFindAndStartJob`).
- **Why vanilla shelves don't hit it (corrected wording):** my earlier shorthand "their `Accepts` is just the filter" was imprecise and, on its own, does not explain anything. The accurate statement is about *where the truth lives*. In vanilla, every limit on what a shelf can take (cell already holds a different item, stack full) is a property of the **cells**, and the haul search checks cells (`IsGoodStoreCell` → `NoStorageBlockersIn`) before it creates a job. A shelf whose filter is broader than its contents (partial stacks of A, filter also allows B) is fine because the search finds no free cell for B on that shelf and never produces a job; `Accepts` (filter-based) is only a later re-check that cannot disagree with a cell the search already approved. The filter never needs to be "dynamic": the cells are. Our elevator has no items on its cells (the buffer is virtual), so the cell check always says "free", and every limit we have (slots full, entry mismatch, no power, not connected) exists only in `Accepts`, which the search does not call. That is the difference, not the filter width. This part is from the decompile done earlier in this task; I have not re-verified it in this round (see §9).
- **Not explained / retracted:** I earlier noted that Step C (disconnected + hay allowed) showed no errors and could not explain it. The human has since confirmed the Step B version also produces the error (disconnected, and also all slots full with item A while the filter allows item B). So Step C/B did have the bug; the earlier non-observation was a coincidence of what was exercised, not a different code path.

## 7. Plan correction needed: haul search description (`plan.md` is outdated; code already changed)
Status: the code change this refers to (`HaulDestinationEnabled`, §5.3) **is implemented** in the source. What is **not yet done** is updating `plan.md`, which still contains the wrong description below. The edit to `plan.md` is only proposed here and must be reviewed and applied elsewhere. Note: §9 shows the implemented `HaulDestinationEnabled` fix is incomplete and will probably be replaced, so the plan wording should be finalized after that.
Two statements in `plan.md` are contradicted by the decompiled game code (see §6):
1. §2, "Hauling reaches our `Accepts`" (around line 195). Current text: *"Hauling reaches our `Accepts`: `StoreUtility.IsGoodStoreCell` → `IsValidStorageFor` → `slotGroup.parent.Accepts(t)` (interface call). `TryFindBestBetterStoreCellForWorker` pre-filters with `slotGroup.Settings.AllowedToAccept(t)` (the filter only) and then checks cells through `Accepts`."* Proposed replacement:
   > The haul *search* never calls our `Accepts`. `TryFindBestBetterStoreCellForWorker` pre-filters with `slotGroup.Settings.AllowedToAccept(t)` (the filter only) and `IsGoodStoreCell` checks cell contents, reservation and reach (`NoStorageBlockersIn`), not `Accepts`; a group is skipped only if `parent.HaulDestinationEnabled` is false. `IsValidStorageFor` (which calls `slotGroup.parent.Accepts(t)`) is used by the `HaulToCell` job's goto toil as a fail condition, so a refusal by `Accepts` alone makes the job fail instantly and the pawn restarts it ("started 10 jobs in one tick"). Anything that makes `Accepts` false for the whole building must therefore also make `HaulDestinationEnabled` false.
2. §2 "Hauling system", last sentence of the "filter is used by the game too" bullet (around line 212). Current text: *"An elevator that refuses an item its filter allows is safe: that cell is just skipped and the item goes elsewhere or waits."* Proposed: delete it, or replace with: *"An elevator that refuses an item its filter allows is not skipped by the search; only `HaulDestinationEnabled` (building-wide) keeps it out of the search. Per-item refusals make the haul job fail at once (see above)."*

## 8. Deviations from the original plans (already implemented and agreed; the plans need updating)
Everything listed here **is implemented in the defs/code** (the human requested or approved each change). What is **not done** is the update of `plan.md` / `plan_stepD.md`: they still show the original values and names. My earlier search-and-replace edits to those two files were reverted, so the plan updates below are only proposals for review and application elsewhere.

**Power values** (now 1x1 100 W active / 10 W idle; large 300 W / 30 W):
- `plan.md` line ~126 (Step D "Power" bullet): "1x1 200 W / 10 W, 2x2 500 W / 25 W" → "1x1 100 W / 10 W, 2x2 300 W / 30 W".
- `plan_stepD.md`: §2.1 "power comp: active 200, idle 10" → "active 100, idle 10"; "active 500, idle 25" → "active 300, idle 30"; the XML snippet `<basePowerConsumption>200` → `100`; §9 item 5: "Power needed: 10 W (active: 200 W)" (2x2: 25/500) → "(active: 100 W)" (2x2: 30/300); "active (200 / 500 W)" → "(100 / 300 W)".

**Names** (defs/textures cannot end in a digit):
- Def `CargoElevator_Elevator2x2` → `CargoElevator_ElevatorLarge` (also `CargoElevatorDefOf`): `plan_stepD.md` §2.1 heading and §2.3.
- Textures `*_2x2` → `*_large`: `CargoElevator_large.png`, `CargoElevator_{green,red,orange}_large.png`. `plan.md` lines ~144 and ~271; `plan_stepD.md` §2.1 (texPath, overlays) and §9 item 1.
- New menu icons (not mentioned in either plan): `CargoElevator_MenuIcon.png` and `CargoElevator_MenuIcon_large.png`, used as `uiIconPath` of the two defs; the base sprite stays for blueprint, ghost and map. `plan.md` line ~271 says the base sprite is also the build-menu icon, which is no longer true.
- Status text: unpowered + disconnected shows "no power, disconnected" (implemented, §5.1); the plans describe a single status value.

## 9. Haul error: new facts, status and next steps (nothing below is implemented yet)
**New facts from the human:**
- The Step B version produces the same "started 10 jobs in one tick" error, in two situations: elevator disconnected, and all slots full with item A (unmerged) while the filter also allows item B and pawns try to haul B. So this is an old defect in the `Accepts`-only refusal design, not something Step D introduced.
- Requirement: the error must be **eliminated completely**, using the mechanism vanilla and other mods rely on, not a partial guard.

**Why the current fix is not enough:** `HaulDestinationEnabled` is a building-wide switch. It cannot express per-item refusals. The cases "slots full, but a partly filled entry exists, item B does not match" and "slots full, item B allowed by the filter" stay unsolved whenever the building is enabled for some other item. It remains in the code only as a partial mitigation.

**Sources read this round (inside the project only):** the game via `include\Assembly-CSharp.dll` (decompiled `StoreUtility`, `JobDriver_HaulToCell`, `StorageSettings`, `Building_Storage`) and `Examples\` (VFE Factory, DeepRim). MultiFloors was not searched for this.

**Verified in the game code (1.6):**
- `IsValidStorageFor(c)` = `NoStorageBlockersIn(c)` **and** `slotGroup.parent.Accepts(t)`. `JobDriver_HaulToCell`'s goto toil fails when it is false.
- The search side: `TryFindBestBetterStoreCellFor` skips a group if `!parent.HaulDestinationEnabled`; the worker then requires `slotGroup.Settings.AllowedToAccept(t)` and per cell `IsGoodStoreCell(c, map, t, carrier, faction)`. `IsGoodStoreCell` checks forbidden, `NoStorageBlockersIn`, reservation, fire, blueprints, reachability. It never calls `Accepts`.
- `Building_Storage.Accepts(t)` = `GetStoreSettings().AllowedToAccept(t)`, i.e. the filter only. So in vanilla the only gap between search and job is the cell part, and the search already applies it. For a vanilla shelf the two checks never disagree, which is why partly filled shelves with a broader filter do not loop.
- `StorageSettings.AllowedToAccept(Thing)` = `filter.Allows(t)` **and** `owner.GetParentStoreSettings().AllowedToAccept(t)` (`owner` is the `IStoreSettingsParent`). `Building_Storage.GetParentStoreSettings()` is public and non-virtual.

**What others do (VFE Factory, `Examples\VanillaFurnitureExpanded-Factory\1.6\Source\Harmony\StoreUtility_IsGoodStoreCell_Patch.cs`):** their storage-like buildings (`Building_FactoryHopper`) have a rule the cell check cannot see (`allowInserting`). They add a **Harmony postfix on `StoreUtility.IsGoodStoreCell`** that sets the result to false when the carried thing may not go there (`SlotGroupParentAt(c) is Building_FactoryHopper hopper && !hopper.allowInserting`, only when `carrier != null`). That is the same approach in principle as vanilla: put the limit where the search looks. Their conveyors implement `GetParentStoreSettings` as the fixed "everything" settings and use `Accepts`/`AllowedToAccept` only for their own item moving, not for pawn hauling. DeepRim only calls `Accepts` and `IsValidStorageFor` itself before moving items; it has no hauling-search workaround.

**Candidate fixes, none implemented; decision needed:**
1. **Harmony postfix on `IsGoodStoreCell`** (the VFE way). Per item and complete: veto the cell when `building.Accepts(t)` is false, using the carried/hauled thing `t`. Costs a Harmony dependency; `plan.md` said "avoid Harmony where possible", but later steps already list Harmony patches (e.g. the Dialog_LoadTransporters cleanup in Step F/polish, VFE hopper compatibility). Single small patch, easy to switch off.
2. **No Harmony: dynamic parent settings.** Re-list `IStoreSettingsParent` on the class and hide `GetParentStoreSettings()` with `new`. Return a settings object that the elevator keeps in sync with its buffer: everything allowed (the fixed settings) while operational and a slot is free; only the defs of partly filled entries (when merging is on) while slots are full; nothing while not operational. `AllowedToAccept(t)` in the search pre-filter then rejects exactly what `Accepts` rejects. This must be kept up to date whenever the buffer or power state changes. Known residual: items of the same def that differ in stuff and do not stack (practically no vanilla storables); and every new `Accepts` rule must be mirrored there, which is easy to forget. Needs a check that the storage-group case (`IStorageGroupMember`, already overridden on our class) takes the same path.
3. Make `Accepts` equal the filter and handle overflow some other way. Not recommended: a dropped item the buffer then refuses is hauled again and again.

Recommendation was option 1. **Decision (human): option 1, the VFE way; implemented, see §10.** The `HaulDestinationEnabled` override was reverted at the same time.

## 10. Haul error fix (Harmony) and "Allow insert" switch — IMPLEMENTED (code, defs, About.xml); in-game test pending
Status: everything in this section **is implemented in the source** and builds (0 errors, 0 warnings). It is not tested in the game. The plans (`plan.md`, `plan_stepD.md`) do not describe any of it yet; they need updating (deviations from the original plan, agreed with the human).

### 10.1 What was done
- **Harmony dependency:** `CargoElevator.csproj` references `include\0Harmony.dll` (symlink to the Harmony mod, `Private=False`, so it is not copied to our output). `About.xml` now has `modDependencies` (`brrainz.harmony`, Steam/GitHub links) and `loadAfter brrainz.harmony`. This is a **deviation from `plan.md`** ("avoid Harmony where possible"), decided by the human because no non-Harmony way covers every case.
- **`HarmonyPatches.cs` (new):** `[StaticConstructorOnStartup]` creates `Harmony("FT.cargoelevator")` and calls `PatchAll`; logs the patched method count. `Patch_StoreUtility_IsGoodStoreCell` is a **postfix** on `StoreUtility.IsGoodStoreCell(c, map, t, carrier, faction)`: if the result is true and the cell belongs to a `Building_Elevator` (`haulDestinationManager.SlotGroupParentAt(c)`), the result becomes false unless `elevator.AcceptsPawnDelivery(t)`. Same idea as VFE's `StoreUtility_IsGoodStoreCell_Patch` for hoppers. It only ever turns true into false. Unlike VFE it does not restrict itself to `carrier != null`, because `Accepts` is the arbiter for every caller.
- **`Building_Elevator.Buffer.cs`:** the acceptance rule now lives in one side-effect-free method `GetRefusalReason(thing)` (null = accept). `Accepts` (game job check, conveyors, our own deliveries) = that rule plus the deduplicated "Rejected" log. `AcceptsPawnDelivery` (haul search) = `allowInsert && GetRefusalReason == null`, no logging (it runs per cell per search).
- **Reverted:** the `HaulDestinationEnabled` override and `HasPartlyFilledEntry` in `Building_Elevator.Power.cs` (the patch makes them unnecessary; vanilla `HaulDestinationEnabled` is back).
- **"Allow insert" gizmo:** toggle in the buffer gizmos, label "Allow insert". Saved (`allowInsert`, default from new extension field `defaultAllowInsert`, set in both defs), logged in the buffer dump and in the startup diagnostics. Keys `CargoElevator_ToggleInsert(Desc)`. The icon is a placeholder (vanilla `TexCommand.Install`).
- **Decision "allow taking" is not needed:** the elevator never acts as a pawn-accessible source (its buffer is internal; Unload puts items on the ground and the normal storage rules apply), so there is nothing to forbid.
- **Decision "allow insert" scope:** it only affects the *haul search* (pawns carrying items in). It is deliberately **not** part of `Accepts`, otherwise it would also block conveyor belts and other mods that call `Accepts`, which is the case it is meant for. A pawn already carrying an item to the elevator still delivers it (the job's check uses `Accepts`; the elevator absorbs what lands on its cell). A manual "haul to this cell" order is not blocked either (it does not go through `IsGoodStoreCell`'s search; not verified, to test).

### 10.2 Consistency analysis: search check vs. the job's `Accepts` check
Question: the search (via the patch) and the later job check are two separate evaluations; can they disagree and recreate the loop?
- **By construction the search is a subset of the job check.** Search = filter ∧ cell checks ∧ `AcceptsPawnDelivery` = filter ∧ cell checks ∧ `GetRefusalReason == null` ∧ `allowInsert`. Job = cell checks ∧ `Accepts` = cell checks ∧ `GetRefusalReason == null`. Same function, so a cell the search accepts is accepted by the job as long as the state is unchanged. The only extra condition in the search is `allowInsert`, which can only make the search stricter (harmless: no job is created).
- **State changes between search and job check** (another pawn or a conveyor fills the last slot, power is cut, the link breaks, the filter is edited): the job fails once. The pawn then searches again with the new state, and the patched search now rejects the elevator, so no new job points at it. Self-correcting, at most one failed job per change, not a loop. A loop needs a *persistent* disagreement within the same tick, which the shared rule rules out.
- **Several pawns, one free slot:** both searches pass; the first delivers; the second's job check fails once and is not restarted for this destination. Pawns that already carry the item still drop it and the elevator absorbs it (a slight overfill is allowed, as before).
- **Same `t`:** the search passes the thing to be hauled (the ground stack), the job checks the same thing as target A. `GetRefusalReason` depends only on the thing's def/stuff/filter match and the buffer, not on the carried count.
- **Logging side effects:** the search no longer calls the logging path, so alternating item types cannot spam the log. `Accepts` (job check) still logs once per change.
- **Not covered / residual:** (a) callers that do not use `IsGoodStoreCell` and call `IsValidStorageFor` directly (manual orders) use `Accepts` only, i.e. they ignore `allowInsert` and cannot loop either; (b) other mods that replace the whole haul search (e.g. "Pick Up And Haul" still calls vanilla `IsGoodStoreCell`; unverified) are untested; (c) if the patch fails to apply, the old loop returns: the startup log line `Harmony: patched N method(s)` and Harmony's own error log show that.

### 10.3 Test plan (to run in the game)
1. Startup log shows `Harmony: patched 1 method(s)`; no Harmony errors.
2. Repeat the original repro: elevator unpowered / disconnected, hay allowed, hay on the map → no "started 10 jobs in one tick" errors; pawns ignore it.
3. All slots full with item A (merge off), filter also allows B, B lying around → no errors, B stays put.
4. Slots full, but a partly filled A entry with merge on: A is still hauled in, B is not.
5. Connect and power both ends → hauling works again.
6. "Allow insert" off: pawns no longer haul into the elevator; a conveyor feeding it still works; the toggle persists through save/load; on again → pawns haul.

## 11. Future option (NOT implemented, deferred as too complex for now): power the pair from one end only
Idea: if one end is on a powered grid, the other end works too, and the pair uses the same energy whether one or both ends are grid-powered. To be added to the plan as a future option. This section is a summary of the idea, the verified facts and the proposed solution.

### 11.1 Verified facts (decompiled `PowerNet`, `CompPowerTrader`, 1.6)
- A power net exists per map and a building has one registered `CompPowerTrader`. There is no vanilla link between maps.
- `PowerNet.CurrentEnergyGainRate()` sums the output of comps that are `PowerOn` only; an off comp costs the net nothing, whatever its `PowerOutput`.
- `PowerNetTick` switches a wanting-but-off comp on (randomly, paced) if `gain + stored >= -(its output)`, and switches random consumers off (about 5% per 20 ticks) while the net is in deficit.
- Consequence 1: `PowerOn` is only truthful while the comp requests power. With `PowerOutput = 0` the turn-on test is `0 >= -1e-7`, so the comp turns on even on a dead net. An end that draws nothing cannot report whether its grid is alive.
- Consequence 2: a request larger than the net can give makes vanilla switch the comp off and back on repeatedly (flicker, sounds). A request change must be checked against what the net can afford first.
- DeepRim moves power between levels with a power-plant comp whose output is set via reflection. That creates energy on one side and consumes it on the other; not recommended.

### 11.2 Proposed solution (summary)
- Treat the pair as one load `D = d_this + d_partner` and place it on whichever grid can pay. Modes: Split (each end draws its own share), Host (one end requests `D`, the other 0), Off (each requests its own share, as today).
- One end (the pair leader, lower `thingIDNumber`) decides every 30 ticks and after a transfer, writing `PowerOutput` of both ends. Per end: `surplus = netGain - (own contribution if PowerOn) + netStored`, independent of the end's current request, so the decision cannot oscillate. Both surpluses cover their own share -> Split; exactly one covers `D` -> that end hosts (prefer the current host); else Off.
- Total consumption is `D` in Split and Host, so one grid or two makes no difference.
- Operational: every end with a non-zero request is `PowerOn`; a zero-request end only needs `WantsToBeOn && !IsBrokenDown()`.
- Needed changes (about 150 lines, mostly `Building_Elevator.Power.cs`): the pair plan, a "plan applied" flag instead of the `PowerOutput != 0f` sentinel, pair-level `IsPowered`, `PartnerUnpowered` merged into `NoPower`, `Accepts`/`ShouldAbsorb`/`TrySend` testing `IsOperational` only, re-plan on `Link`/`Unlink`, an inspect line for the power source (vanilla would show "Power needed: 0 W" on the passive end), logging of mode changes.
- Open questions: should flicking an end off or a breakdown stop the pair (proposed: yes)? In mode Off an end that can pay its share keeps drawing it (as today). The handover after a grid failure takes seconds (vanilla switches the failing end off with a delay). Batteries are counted like vanilla's turn-on test; debug `unlimitedPower` passes every test.
- Simplest alternative, not recommended: pair works if either end is `PowerOn`, each end requests its own share. One grid costs half the energy of two, and the unpowered end keeps showing "needs power".
- Tests needed: one grid only (each side), both grids, failure and recovery on either side, batteries, flick at each end, unlink/relink, save/load in every mode.

## 12. Consideration for a future step (NOT implemented): the elevator as a weak light source in its status colour, blinking with the status
Findings (decompiled `CompGlower`, `CompProperties_Glower`):
- Add a `CompProperties_Glower` to the defs with a small `glowRadius` (about 2-3 cells) and a dim `glowColor`; no custom mesh. `CompGlower.GlowColor` is virtual and has a per-instance override (the `GlowColor` setter), so one def can show different colours per elevator.
- On/off: `CompGlower.ShouldBeLitNow` asks the parent if it implements `IThingGlower` (and every comp that does). `Building_Elevator` would implement `IThingGlower.ShouldBeLitNow()` with the same rule as the status light (not Disconnected/NoPower). Colour change: set `GlowColor`, then `CompGlower.ForceRegister(map)`; `UpdateLit(map)` handles on/off.
- Colours must match the sprite lights: take both from one place (e.g. `ElevatorSettingsExtension`) so they cannot drift apart. Brightness needs in-game tuning and a check that it does not change room lighting too much.
- **Blinking (requested): wanted if it can be done in a performant way.** Each glow change recalculates the map's glow grid and dirties a map mesh section, which is the cost to measure. Points for the decision:
  - The blink exists only while a manifest load is set up (orange/steady-colour alternation), so the cost is bounded to the elevators in that state, not all elevators.
  - Drive the glow blink from the elevator's periodic tick (not per frame), phase-locked to the sprite blink as closely as the tick allows, and change the glow only when the phase flips (twice per 2 s per blinking elevator).
  - Measure first: use `ElevatorProfiler` (already in the project) around `ForceRegister`/`UpdateLit`, with e.g. 1, 10 and 50 blinking elevators on a large map and with a lot of lights. Acceptance criterion to agree before testing.
  - Fallbacks if too expensive: a slower glow blink than the sprite blink, a steady glow with only the sprite blinking, or blinking only the sprite when more than N elevators blink at once.
- Effort: small (one comp in the defs, `IThingGlower`, colour update in the periodic status path, one colour table), plus the profiling. Depends on Step D's `Status`; independent of section 11.

## 13. Note for the manifest step (NOT implemented)
When a manifest load is complete, issue a global message to the player that loading is done, the same way the shuttle announces that it is ready and caravans announce that they are leaving (a standard `Messages.Message` with the elevator as the look target, and a message type consistent with those vanilla ones; the exact vanilla keys and message type are to be looked up in that step). Add the language key then.

## 14. In-game test round 1 (human) and follow-ups
Human results: checklist items 1, 2, 6, 8 and 10 OK. 3/4 OK as far as visible. 5 OK except for one unsynced power change. 7 OK, but the paused line should come back for power. 9: buffered meat spoils; whether it spoils at the same rate as outside was not measured. The Harmony patch is active ("patched 1 method"); with "allow insert" off nothing is hauled in and there are no errors. Many `Harmony: rejected …` lines appear even during steady, unblocked delivery.

### 14.1 Fixed (by the planner, builds with 0 errors)
- **Dev gizmos** now use `DebugSettings.ShowDevGizmos` instead of `Prefs.DevMode` (`Building_Elevator.GetGizmos`). Vanilla: `ShowDevGizmos = Prefs.DevMode && godMode`, so they show only with dev mode and god mode on, like every vanilla dev gizmo. Recorded in `plan.md` §1.

### 14.2 Power changed on one end only (open, fix proposed)
- **Cause:** each end runs `UpdatePowerDraw` on its own `IsHashIntervalTick(30)`, and the phase comes from `thingIDNumber`, so the two ends are evaluated up to 29 ticks apart. Both share `lastActiveTick`, but one end can time out and go idle while the other has not been evaluated yet. If the next transfer comes within that gap, the first end logs "idle → active" and the other was never idle, so it logs nothing. That is the S1L/B1L log. Going active is already synced (`MarkTransferActive` updates both ends); going idle is not.
- **Proposed fix:** in the periodic power block, also call `partner?.UpdatePowerDraw()`. The method is idempotent and both ends then switch in the same tick. Cost: one extra comparison per elevator every 30 ticks.

### 14.3 Harmony rejections during steady delivery (open, investigation proposed)
- The postfix logs every rejected cell on every search, without the reason. The haul search runs per haulable item, per pawn scan and per cell (4 cells on the 2x2), so even short refusals produce many lines.
- Candidate reasons, not verified: (a) **buffer full** for moments when several pawns arrive together. (b) **No power** for moments: switching to active (30 → 300 W) on a weak grid makes vanilla switch the comp off and on (`PowerNetTick`). (c) The **receiving end**: items dropped next to B1L are searched for storage, and B1L is a candidate when its filter allows them. The log names the elevator, so the human can check which end the lines name. If B1L *accepts*, pawns would carry output back in (the "pawn bounce" in §1 of `plan.md`, the player's responsibility).
- **Proposed:**
  - The postfix gets the reason from a new side-effect-free method, e.g. `GetPawnDeliveryRefusalReason(t)`: "insert off", or `GetRefusalReason`.
  - It logs deduplicated per elevator (only when the def or reason changes, like `LogRefusal`).
  - Remove the leftover `var harmony = new Harmony("FT.cargoelevator");` in the postfix. It is unused and allocates a Harmony instance on every rejection.
- **Optional test aids** for items 3/4:
  - the intake cell in the `Absorbed` log line;
  - the game tick in send and absorb lines, so rates can be read from the log.
  - Rot rate (item 9): compare against an identical stack lying unroofed next to the elevator.

### 14.4 "Transfer paused" line: current rules (for the decision)
The line exists only on the **sending** end, after a send attempt that sent nothing. It lists the entries still in the buffer, grouped by reason, in this fixed order:

| Order | Shown | Reason (key) | When |
| --- | --- | --- | --- |
| 1 | yes | storage full | some storage on the partner's output ring accepts the item but has no room. Drop is not tried in this case, whatever the drop setting. |
| 2 | yes | no storage | no adjacent storage accepts it, and drop is off or no floor cell has room |
| 3 | yes | FIFO on | FIFO on, entry is behind the head (only the head is evaluated) |
| – | no | undeliverable | pawn entry; cannot occur yet |
| – | no (removed in Step D) | not connected | no valid partner |
| – | no (never shown) | no power / partner unpowered | either end unpowered |

- **Not operational (disconnected / no power / partner unpowered) wins over everything:** `TrySend` clears the hold and returns before any entry is evaluated, so no line is shown, only the status. Example: no power with drop off shows just "Status: no power"; "no storage" is never evaluated.
- With FIFO off, the line appears only when *no* entry could be sent; a single send clears it.
- After power returns, the per-entry reasons reappear at the next failed attempt (up to `HeldRetryTicks` later).
- **Option to reinstate (display only, no change to sending):**
  - While not operational and the buffer is non-empty, show one group with all entries and the not-operational reason: "Transfer paused: Hay x75, Rice x20 (no power)". The texts would be "not connected", "no power" and "partner unpowered".
  - It replaces the per-entry groups, because those are not evaluated then.
  - Decision needed: whether to reinstate "not connected" too (it was removed in Step D) or only the power reasons.

### 14.5 Clarification for checklist item 1
"Extension values" means the `ElevatorSettingsExtension` fields of each def (slots, `ticksPerStack`, `activeHoldTicks`, `blockedAfterSendIntervals`, light texture paths, defaults of the toggles). The startup diagnostics line prints them per def.

### 14.6 Noted for later (in `plan.md` §7)
- A global configuration UI (mod settings), with a toggle for elevators acting as light sources.

## 15. Round 2 (human decisions after test round 1)
Rule from the human: the plan files are not edited any more; findings, decisions and work beyond the plans are logged here only. (In round 1 two lines were added to `plan.md`, the dev-gizmo rule in §1 and the global settings UI in §7. The human decides whether to keep them.)

### 15.1 Dev gizmos: god mode (done)
The human meant god mode. Vanilla dev gizmos, e.g. `CompPowerTrader`'s "DEV: Toggle power on", are gated on `DebugSettings.ShowDevGizmos`, which is `Prefs.DevMode && godMode`. The change in §14.1 already uses exactly that check, so it behaves like vanilla. No further code change.

### 15.2 "Transfer paused" lists held items for every reason (done, builds with 0 errors / 0 warnings, not tested in game)
- **Decision (human):** whenever items in the buffer are held back, the paused line lists them with a reason, including "not connected" and "no power" (either end). Overlap with the status line is accepted.
- **Implementation (`Building_Elevator.Buffer.cs` `DescribePausedTransfer`):**
  - While the pair is not operational, all buffer entries are listed in one group: "Transfer paused: Hay x75, Rice x20 (no power)".
  - Not connected (`partner == null`) wins over no power. "No power" covers this end and the partner.
  - The reason is read live at display time, not stored in `heldEntries`. `TrySend` stops before it evaluates any entry in this state, and a live check cannot go stale when the state changes. Sending, holds and `blockedSinceTick` are unchanged, so the red "blocked" light cannot be triggered by an unpowered period.
  - When the pair is operational, the line is as before (§14.4).
- New keys `CargoElevator_PausedNotConnected` ("not connected") and `CargoElevator_PausedNoPower` ("no power").
- **Remaining case without a reason:** a pawn entry (`HoldReason.Undeliverable`); none can be in the buffer yet.
- **Finding (unchanged behaviour, for a later decision):** while not operational, `TrySend` retries only every `PeriodicInterval` (250 ticks). After power or the link returns, sending can therefore wait up to about 4 s. During that time the paused line shows nothing (the held list was cleared) until the first attempt. Retrying at the power update interval (30 ticks) while not operational would shorten this to 0.5 s at negligible cost.

### 15.3 Unused Harmony object in the postfix (removed)
The postfix had a line `var harmony = new Harmony("FT.cargoelevator");` (added together with the rejection log line). It created a new Harmony instance on every rejection and was never used. It has been removed. The patch now creates nothing. The rejection log line itself stays until §15.5 is decided.

### 15.4 Power sync (discussion, not implemented)
- **What a transfer does:** `MarkTransferActive` sets the same `lastActiveTick` on both ends and calls `UpdatePowerDraw` on both, in the same tick. So it forces a *check* on both, not a state, but both get identical inputs and therefore the same result (active).
- **Inputs of the decision:** `TicksGame - lastActiveTick <= Settings.ActiveHoldTicks`. `lastActiveTick` is always set on both ends together. `ActiveHoldTicks` comes from the def, and both ends have the same def (no cross-size pairing). So two checks in the same tick always agree. Today's asymmetry comes only from *when* each end checks (its own hash phase), never from different inputs.
- **Human's proposal (push on change):** an end that detects its own change during its periodic check makes the partner update too. In the steady state there is no extra work, unlike re-checking the partner on every periodic check.
- **Recommended variant:** push the *state*, not a check. The end that detects the change sets the partner's draw state to the same value (a small `SetPowerActive(bool)` used by both). The draw state is a pair property by definition, so the pair cannot diverge even if a future change gives the ends different inputs (e.g. per-elevator settings). The partner's own later check then finds nothing to change.
- **Side effect:** the partner's `Status` log may still lag by up to 29 ticks, because status is logged from each end's own periodic check. This affects the log only, not behaviour. The push could also call the partner's status log.
- **Decision needed:** push the state (recommended) or push a check.

### 15.5 Rejection logging inside the elevator (discussion, not implemented)
- **Agreed direction:** the postfix calls `Building_Elevator.AcceptsPawnDelivery(t)`, which decides from `allowInsert` plus `GetRefusalReason` (the same rule `Accepts` uses). That method should do the logging; the postfix only applies the result.
- **Deduplication problem:** the method runs per cell, per haulable item and per pawn scan, so it needs a change filter. `Accepts` logs when the def or the reason changes. With 4 allowed item types the searches alternate between defs, so that filter would still log on almost every call.
- **Proposed:**
  - Log when the *reason* changes, naming the item that triggered it.
  - Use separate change-detection fields from `Accepts`, so the two logs do not reset each other.
  - Optionally add a count of suppressed rejections since the last line.
  - Filter refusals are not logged, like in `Accepts`.
- **Decision needed:** whether reason-only change detection is enough for the investigation.

### 15.6 Transfer-rate counter (on hold)
- **Human's proposal:** a temporary counter that counts transfers and logs the rate per second at about TickRare frequency, removed later.
- **On hold:** it will be done after all other changes are tested. The human snapshots with git, runs the measurement, then restores.
- **Points to settle then:**
  - Count per elevator, sent stacks and items (absorbed too?).
  - Rate per game second (60 ticks), so it does not depend on the game speed.
  - Log only windows with activity.

## 16. Round 3 (human decisions)
- The two `plan.md` lines from round 1 are fine. The planner may edit the plan files again (the §15 note is superseded). `plan.md` is updated to rev 9 with the Step D results (§1–§16 of this file).
- **Power sync (§15.4): documented as an approved option for later, not implemented.** Pushing the state is approved, but strict sync is not a priority now. Recorded in `plan.md` §7.
- **Rejection logging (§15.5): done, unfiltered (human decision).** `AcceptsPawnDelivery` logs every refusal with its reason: `Pawn delivery refused: <elevator> refuses <item>: <reason>`. The reasons are those of `GetRefusalReason`, or "allow insert is off"; filter refusals are logged too. There is no change detection and no extra fields, so the probe is easy to remove later. The postfix no longer logs; it only applies the result. Expect log flooding while hauling; this is a probe to be removed with the others.
- **Retry while not operational: done.** `TrySend` retries every `PowerUpdateInterval` (30 ticks) instead of `PeriodicInterval` (250 ticks) while the pair is not operational. Sending resumes within about 0.5 s after power or the link returns. The `Send paused` log stays change-detected (existing, unchanged).
- **Noted for later (in `plan.md` §7):** configuration settings for transfer rate (ticks between pieces), retry delay (ticks), and active and idle power use, next to the light-source toggle.
- Build: 0 errors, 0 warnings. Not tested in the game.

## 17. Round 4 (tests of rounds 2/3, then changes)
### 17.1 Test results (human)
- Dev gizmos disappear with god mode off: OK.
- Disconnected elevator with an empty filter: no rejection lines: OK.
- Disconnected, normal priority, wide filter: haul jobs are refused, e.g. `Pawn delivery refused: CargoElevator_ElevatorLarge4515157 refuses Limestone blocks x5: not connected`.
- Rejection reasons seen: not connected, allow insert is off, buffer full, partner unpowered, no power. This answers §14.3: the rejections during "steady" delivery are real states (moments of a full buffer, a partner or own end briefly unpowered). The `IsGoodStoreCell` patch is working as designed.
- The paused line showed the reasons correctly (no power, FIFO, no storage).
- The log message limit filled up quickly from the unfiltered rejection log.

### 17.2 Changes (done, builds with 0 errors / 0 warnings, not tested in game)
1. **Shorter "Transfer paused" line (human decision).** The vanilla "Contents:" line already lists the buffer, and long labels (quality, HP %) took a lot of room. The reasons are now in **square brackets**, so they don't look like quality or HP indicators. `DescribePausedTransfer` picks the first matching form:

   | Case | Output |
   | --- | --- |
   | not operational (not connected wins over no power) | `all items [not connected]` / `all items [no power]` |
   | FIFO: head held, every later entry waiting | `Berries x25 [no storage]; all others [FIFO on]` |
   | several held entries, all with the same reason (FIFO off) | `all items [no storage]` |
   | anything else (mixed reasons, or one entry) | grouped as before, e.g. `Corn x50, Rice x20 [storage full]; Steel x75 [no storage]` |

   - **Planner's interpretation (to confirm):** the "all items" form is also used when FIFO is off and every held entry has the same per-item reason. The human named only the not-operational reasons as examples.
   - A single held entry shows its own label (`Berries x25 [storage full]`), not "all items".
   - "All items" and "all others" refer to the held entries still in the buffer. Items absorbed after the last failed attempt are not part of the hold until the next attempt.
   - New keys `CargoElevator_PausedAllItems` ("all items") and `CargoElevator_PausedAllOthers` ("all others").
   - Two small helpers were added, `PausedReasonText` and `PausedGroup`.
2. **Rejection-reason log disabled (human decision).** The logging part of `AcceptsPawnDelivery` is commented out. The method is back to `allowInsert && GetRefusalReason(thing) == null`. The commented block says what it was for (§15.5/§16) and how to re-enable it. The "allow insert is off" reason text existed only in that block. No other code was used only by it (`ElevatorUtil.Describe` is shared). The postfix comment points to the disabled probe.

## 18. Round 5: potential issue and test result
### 18.1 Potential issue: unexplained haul-search rejections (open, not reproduced)
- **Observation (round 1):** the first version of the patch log (`Harmony: rejected <thing> for <elevator> …`, no reason) showed occasional rejections while the elevator appeared fine: connected, both ends powered, not blocked, buffer not full, cells empty, and the hauled item had room.
- **What we tried:** the unfiltered reason log in `AcceptsPawnDelivery` (§16), then a long run with thousands of items delivered in hundreds of hauls.
- **What we found:**
  - Every logged rejection had an explained reason (not connected, allow insert is off, buffer full, partner unpowered, no power), in states that matched.
  - While the elevator was fine, as far as we can detect (unblocked, power OK etc.), no rejection was logged at all.
  - No haul errors or loops were seen.
- **Not known:** whether the round-1 cases were real refusals in states too short to see in the UI, or whether they occurred at all with the current code.
  - Short states could include a full buffer for a few ticks while several pawns arrive, or a brief switch-off by the power net when the draw steps from 30 W to 300 W.
  - The round-1 build differed: it had the leftover Harmony object and no reasons.
- **If it matters again:**
  1. Re-enable the probe (§17.2).
  2. Add the elevator state to each line: used/max slots, `PowerOn` of both ends, `powerActive`, hold state.
  3. Watch for a rejection while that state says the elevator should accept.
- Recorded in `plan.md` (Step D, potential issue).

### 18.2 Test result
- The status line changes are confirmed working and correct. This is taken to include the round-4 paused-line format (the human called the paused line part of the status line before).

### 18.3 Plan update
- `plan.md` rev 9 is updated with rounds 4–5: the paused-line format, the disabled probe, this potential issue, the test results so far, and the transfer-rate counter as the next activity after the human's git commit.

## 19. Rate test (moved)
The rate test (goal, measured values, probe implementation, results) is documented in `test_rates.md`. Raw logs: `rate_test_small.log`, `rate_test_large.log`. In short: sending is exactly 1 piece/s (1x1) and 4 pieces/s (2x2). Intake is limited by pawns to about 2 stacks/s per cell (min gap 31 ticks). The 2x2 reached 5.6 stacks/s with its 4 cells used in parallel.
