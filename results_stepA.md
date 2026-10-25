# DeepElevator – Step A results

Spec: `plan_stepA.md`. Overall: **functionally successful, with one open performance issue** (placement lag, section 3.2) that has been solved. Building, placement rules, auto-pairing, the "Connect to…" menu, lifecycle callbacks (including all map-removal paths) and save/load all behaved as specified.

## 1. What was implemented

Everything in `plan_stepA.md` §2–§4, with the deviations below, plus the diagnostics and the load-time link check added after the second round of testing (section 6).

- `DeepElevator/Source/`: `Building_Elevator`, `ElevatorUtil`, `PlaceWorker_Elevator`, `MapComponent_ElevatorProbe`, `ElevatorTextures`, `DeepElevatorDefOf`, and (round 2) `ElevatorProfiler`, `ElevatorDiagnostics`.
- Defs / language / About: `Defs/ThingDefs/DeepElevator_Elevator.xml`, `Languages/English/Keyed/DeepElevator.xml`, `About.xml` (DeepRim dependency, loadAfter, one-shaft note, empty `modIconPath` removed).
- Build output: only `DeepElevator.dll` + `.pdb` in `DeepElevator/1.6/Assemblies/` (no game/DeepRim DLLs copied).

### Deviations from the spec

| Change | Reason |
| --- | --- |
| `ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch=true` in the csproj | DeepRim.dll is built for .NET 4.8.1, the project targets 4.7.2. MSBuild refused the reference (MSB3274, then CS0246 for `using DeepRim`). Only affects compile-time reference resolution; the game's Mono does not enforce the framework label, and DeepRim already loads compiled that way. |
| MSBuild path is `...\Visual Studio\18\Community\...` (VS 2026), not 2022 | The "2022" in the plan was a typo. |
| **csproj converted to SDK-style** (`net472`, requested after Step A) | No per-file compile list, `dotnet build` works, no VS-only targeting packs. `Properties/AssemblyInfo.cs` removed (assembly info generated from csproj properties). Default item globs are off; only `DeepElevator/Source/**/*.cs` is compiled, so `Examples/` (DeepRim source) is never picked up. Verified: `dotnet build` (Debug) and VS MSBuild (Release, with `-restore`) both give 0 errors / 0 warnings and identical output files. |
| `DrawGhost` override in `PlaceWorker_Elevator` (the optional extra in the spec) | Draws the red boundary around the anchor so the player sees the forbidden cells. |
| `System.Data*`, `System.Net.Http` references removed | Unused. |
| `DeepElevatorDefOf` exists but is unused | Spec asked for it; later steps will use it. |

Also: the "Connect to…" gizmo uses the vanilla icon `UI/Commands/LoadTransporter` (not verified to exist; falls back to the bad-texture icon), and `plan.md` was modified on disk by the user during the session (not by the agent).


## 2. Test results (human, in game)

### Round 1

| Scenario | Result |
| --- | --- |
| Elevator placed at a different key (offset from the anchor) than any elevator on another layer | No connection, as expected. |
| Elevators at the same key on different maps | Connected automatically. |
| Second pair at another shared key | Also connected correctly. |
| Pairs A–B and C–D on the same cell; B and D removed | A and C stayed unpaired, correctly. |
| Deconstructing / removing a building | Log order: `Destroy`, `DeSpawn`, `Discard` (matches the expected order in the spec §4.4). |
| "Connect to…" menu with two unconnected candidates | Worked; established A–C. |
| Save/load with one connected pair + one single elevator | Loaded correctly. |
| Placement adjacent to the shaft/lift (inside the red boundary) | Refused correctly. |

### Round 2

| Scenario | Result |
| --- | --- |
| **Abandon a DeepRim layer** (checklist #7) | The link was dropped. Log order: `Notify_MyMapRemoved`, `Unlinked`, `MapComponent.MapRemoved`. This confirms the design assumption (map removal calls neither `Destroy` nor `DeSpawn`). |
| **Dev-mode map destruction** (#8) | Same steps and same order. |
| Exclusion zone around an underground **lift** | Works the same as around the shaft. |
| Two shafts on one map | Placing elevators is refused. Existing elevators are unaffected. Accepted as a fringe case. |
| Save after a partner was lost (#11) | Cannot be produced naturally: removals and deconstructions clear the reference before any save. Simulated by editing the save instead (next two rows). |
| Simulated damaged save (a): `partnerMap` of one end changed | The edited end cleared itself on load (`inconsistent link after load`), but the other end kept its link: `State` showed `valid=partner links elsewhere (none)`. **Found a gap, see 3.3.** |
| Simulated damaged save (b): partner reference changed | Same result as (a). |
| Lag when a placement is rejected | Reproduced; see 3.2. |

### Round 3 (diagnostics build)

| Scenario | Result |
| --- | --- |
| Damaged save (a) and (b) after the load-time check | Link removed on both ends with the warning `partner ... does not link back (it links to none); unlinking.`. "Connect to…" becomes enabled and the link can be re-established. 3.3 is fixed. |
| Unrelated build icon right after restart | Also produced the Dubs Mint Menus error (3.1). |
| Startup diagnostic | `placeWorkers=[DeepElevator.PlaceWorker_Elevator] hasInteractionCell=False passability=PassThroughOnly ... tickerType=Rare`: no other mod adds place workers or interaction cells to our def. |
| Hitch lines after rejected placements | Showed about 20000 `AllowsPlacing` calls per frame; see 3.2. |

### Round 4 (after the `FindAnchor` fix)

| Scenario | Result |
| --- | --- |
| Destroying the mining shaft (#9) | The surface elevator was unlinked from its lost deep partner. |
| Repeated placement attempts on and next to a shaft/lift | No `Hitch:` line was logged, i.e. no frame over 150 ms with the designator selected. The lag is treated as fixed. The caller report was logged once (stack trace in 3.2). |

### Round 5 (adjacency rules removed)

| Scenario | Result |
| --- | --- |
| Placing elevators without the adjacency restrictions | Works. |

No `Validation:` warning has been reported in any round (the tick backstop is expected not to fire in normal play).

## 3. Issues found

### 3.1 Dubs Mint Menus error on the first build-menu click: unrelated
An error from Better Architect / Dubs Mint Menus (`toggleDesMethod is null`) appears once after each restart, on the first build icon clicked. It is a version mismatch between those two mods and unrelated to this mod; other icons evoke it too (confirmed in round 3).

### 3.2 Lag when a placement is rejected (FIXED; cause and mechanism below)

**Symptom.** A freeze of up to a few seconds when a placement attempt was refused, worst on the large surface map, barely noticeable on small deep layers. Dev mode on or off. Only our building seemed affected.

**Mechanism: what is proven**

| Fact | How it is known |
| --- | --- |
| In one frame the game called `PlaceWorker_Elevator.AllowsPlacing` about 20000 times (20501 and 20141 in two hitch reports), instead of the once per frame we expected. | Hitch log (`calls=20501`, `calls=20141`), measured by our own counters. |
| Nearly all of that frame time was spent in `ElevatorUtil.FindAnchor` (2657 ms of 2680 ms), at about 0.13 ms per call (max 0.23 ms). 20000 calls x 0.13 ms is the 2.7 s freeze. | Hitch log with `total=` and `max=`. |
| `DrawGhost` was called once per frame and took 0.17 ms: not involved. | Hitch log. |
| Who makes the calls: the click on a refused placement goes `DesignatorManager.ProcessInputEvents` → `Designator.Finalize` → `FinalizeDesignationFailed` → `DesignationDragger.get_FailureReason` → `UpdateDragCellsIfNeeded` (a Harmony **prefix from the mod "Designator Shapes" by Merthsoft**) → reflection `InvokeMethod` → `DesignationDragger.TryAddDragCell` → `Designator_Build.CanDesignateCell` → `GenConstruct.CanPlaceBlueprintAt` → our `AllowsPlacing`. | Stack trace logged by our probe when `AllowsPlacing` reached 500 calls in a frame (round 4). |
| After the fix, repeated rejected placements logged no hitch. | Round 4 test: no `Hitch:` line. |

**What is assumed (not proven)**

- That the 20000 calls of the round 3 hitch reports came from that same caller (the stack trace was captured in a later run, at call number 500 of a frame). Very likely, but not shown by the same log.
- That the old `FindAnchor` was slow because `listerBuildings.AllBuildingsColonistOfDef` and `AllBuildingsColonistOfClass<T>` scan every colonist building on the map (plus `ToList()` allocations), and that this is why the surface, with far more buildings, was worse. The timing fits (the cost is inside `FindAnchor`, and it varies with the map), but the scan itself was not measured separately.
- Why Designator Shapes asks for so many cells. The trace shows it re-running the drag cell computation through reflection when the failure reason is requested; the number of cells (about 20000) is not explained, so we do not know whether it covers a shape area, a whole region or something else.
- Why only our building seemed affected. Plausible: most vanilla buildings are refused or accepted by cheap checks without a costly place worker, while ours adds about 0.13 ms to each of those 20000 cells. Not tested with other buildings.
- That the call count is now still high but cheap. The probe fires at 500 calls; we do not know the current count per frame.

**Fix**

1. `ElevatorUtil.FindAnchor` now uses `map.listerThings.ThingsOfDef(def)` (indexed by def, so the cost does not depend on the number of buildings) for the shaft (`ShaftThingDefOf.miningshaft`) and the lift (`undergroundlift`, thing class `DeepRim.Building_SpawnedLift`), counts only player-owned buildings, and allocates nothing. It returns the same result as before: exactly one anchor, else a reason.
2. The adjacency rule was removed from `AllowsPlacing` (commented out, section 5), which also reduces the work per call. This does not address the number of calls.

**Consequence for later steps.** `PlaceWorker.AllowsPlacing` may be called many thousands of times per frame by other mods, so anything run in it (and in other hot paths like `Accepts` in Step B) must be O(1) and allocation-free. This is now a design rule.

### 3.3 One-sided links after a damaged save (FIXED, confirmed in round 3)

- **Observed:** with only one end's saved data damaged, the damaged end clears itself while the other end keeps pointing at it: `State: ... partner=<id> valid=partner links elsewhere (none)`. The tick backstop would unlink only after up to 250 ticks.
- **Cause:** `ExposeData` only clears a link when its own two fields disagree (partner vs partnerMap). It cannot know what the partner saved.
- **Fix:** `VerifyPartnerLinksBack()`, called from `SpawnSetup` when `respawningAfterLoad` is true. It drops the link on this end (with a warning) if `partner.partner != this`. This is safe at load-spawn time because all saved data and all references (including across maps) are resolved before any map spawns; only the partner's *spawned state* is unreliable then, and the check does not read it. The full `IsValidPartner` checks stay on the tick.
- **Result:** both ends show "Not connected" afterwards, and "Connect to…" can re-link them.

## 4. Not yet tested

- Placement refusal on a map **without** a shaft (the "Needs a DeepRim mining shaft…" text).
- Nothing else open from the Step A checklist. (Placement without the adjacency restrictions was tested in round 5: it works.)

## 5. Agreed changes to the plan

- **Placement restrictions (user decision):** elevators may be placed adjacent to the shaft/lift and adjacent to other elevators. Players must be aware that DeepRim pulls items from adjacent storage and plan accordingly; some may even want that. The only remaining rule: the map must have exactly one anchor (shaft or lift).
  - **Done in code:** the adjacency-to-anchor check in `PlaceWorker_Elevator.EvaluatePlacement` and the red-zone `DrawGhost` override are **commented out, not deleted**, in case adjacency rules return. The language key `DeepElevator_AdjacentToAnchor` is unused for now. `plan.md` §1 (placement rule) still describes the old rule and must be updated.
- **Placement helper** (ghost shading of unconnected elevators on other layers) stays in "Later", implemented in `PlaceWorker_Elevator.DrawGhost` by projecting unconnected elevators through the key. Open question: how to show the layer name (the world-draw pass cannot draw text; check for a PlaceWorker GUI hook first). Note the performance rule from 3.2 if `DrawGhost` ever does more than trivial work.
- **Transport loops (for Step B):**
  - Basically, not as much of a concern as thought before. Player-built conveyor loops remain possible; the rate limit keeps them from being instant.
  - Neighbouring elevators can also be targets, and configuring them to prevent loops is the responsibility of the player.
  - The "pawn bounce" (delivered item dropped on the floor, hauled back into the partner, sent back, …) is also the responsibility of the player - they should not configure the elevator's storage (which affects input), to accept what comes out of it (output). No extra mechanism required.
- **Hot paths (new rule for Step B):** code that vanilla or other mods may call very often (`Accepts`, place workers) must be O(1) and allocation-free (see 3.2). Explore options.
- **Load-time link check (new):** a link must exist on both ends; checked at load-spawn (3.3), in addition to the tick backstop.
- **`plan.md` and `plan_stepA.md` must be updated (NOT done yet, deliberately left for the user to trigger):**
  - `plan.md` §1 (Requirements), placement rule: remove "**not adjacent to the anchor**" and the reason about DeepRim emptying adjacent storage. New text: the only rule is exactly one anchor on the map; adjacency to the shaft/lift and to other elevators is allowed, and the player is responsible for knowing that DeepRim pulls from adjacent storage. Keep the note in the mod description about building one shaft per map.
  - `plan.md` §3 (Design) and §4 (Files): `PlaceWorker_Elevator` only checks for the single anchor; `DrawGhost` red zone is disabled; temporary probe classes (`ElevatorProfiler`, `ElevatorDiagnostics`, the hitch detector in `MapComponent_ElevatorProbe`) exist until removed.
  - `plan.md` §1 (Delivery / loops): add the Step B rules from this section (never deliver into another elevator; hot paths O(1)).
  - `plan.md` §2 / `plan_stepA.md` §4.1: `FindAnchor` was changed to `listerThings.ThingsOfDef` (the plan text names `AllBuildingsColonistOfDef` / `OfClass`); `plan_stepA.md` §4.2 describes the removed adjacency rule and `DrawGhost`; §4.4 should mention `VerifyPartnerLinksBack` at load-spawn (it contradicts "no validation during load-spawn" in §1/§4.4).
  - Tooling: see the next item.
- **Build tooling:** the project is SDK-style `net472`; `plan.md` §4/§6 and `plan_stepA.md` §2.1/§6 still describe the old csproj and a "VS 2022" MSBuild path. Both should be updated (VS 2026, `dotnet build` works).

## 6. Code added after the first test round

- `ElevatorProfiler`: `Measure("name")` scope (struct, no allocation), `Note(...)`, `DumpAndReset()` / `Reset()`, `GetCallCount(...)`. Statistics per named section: calls, total ms, max ms, last note.
- `PlaceWorker_Elevator`: `AllowsPlacing` is measured, its last result is attached as a note, and a stack trace of the caller is logged (max 3 times per session) when it is called 500 times within one frame. The rule evaluation moved into `EvaluatePlacement`. Adjacency rule and `DrawGhost` commented out (section 5).
- `ElevatorUtil.FindAnchor`: rewritten to use `listerThings.ThingsOfDef` (3.2), measured as `ElevatorUtil.FindAnchor`.
- `MapComponent_ElevatorProbe`: also detects **frame hitches** (a frame over 150 ms, via `MapComponentUpdate`) and logs the frame length, selected designator, mouse cell and our timings of that frame. Only reported if a designator was selected or our code ran in that frame (otherwise it is an unrelated stall such as alt-tab, saving or loading). Statistics are cleared every frame.
- `ElevatorDiagnostics`: one startup line with the def's place workers, interaction cell flag, passability, thing class and ticker type.
- `Building_Elevator.VerifyPartnerLinksBack()` (3.3).
- Builds with 0 errors / 0 warnings.

## 7. Next

1. Decide when to remove the temporary probes (section 8).
2. Then plan Step B, with the rules from section 5.

## 8. Other notes

- Logging is unconditional (probe build); consider gating on `Prefs.DevMode` in Step B.
- `MapComponent_ElevatorProbe`, `ElevatorProfiler`, `ElevatorDiagnostics`, the 500-call caller report and the `Discard`/`DeSpawn` probe logging are temporary. The map-removal behaviour (abandon, dev removal, shaft destruction) is confirmed, so `Notify_MyMapRemoved` is enough, and the lag is understood; they can be removed before Step B.
- Nothing is committed by the agent in this round. Human does git commits.
