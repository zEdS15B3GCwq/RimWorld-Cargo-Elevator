# CargoElevator – Step B results

Spec: `plan_stepB.md`. Overall: **functionally successful in the first test round.** Linking across the surface, DeepRim (DR) layers and MultiFloors (MF) levels, renaming, labels, the placement helper, and all destruction paths worked as specified. No lag, no `Validation:` warnings. The change requests from round 1 (section 4) and the wording change after round 2 (4.6) are implemented and confirmed in game. Only pocket maps and the provider-fallback check remain untested (section 7).

## 1. What was implemented

Everything in `plan_stepB.md` §2–§7. Build: `dotnet build` (Debug) gives 0 errors / 0 warnings; `CargoElevator/1.6/Assemblies/` contains only `CargoElevator.dll` + `.pdb`; no source file references DeepRim or MultiFloors types at compile time (only our own provider class names contain the words).

- Removed: DeepRim reference and `modDependencies`, `FindAnchor` and helpers, `LayerLabel`, `TryGetKey`, `AllowsPlacing` and its caller-report probe in the place worker.
- New: `LevelLabels` (+ `ILevelLabelProvider`), `DeepRimLabelProvider`, `MultiFloorsLabelProvider`, `Dialog_RenameElevator`.
- Changed: `ElevatorUtil` (group root, same-group test, center offset, matching cell, candidate search), `Building_Elevator` (`IRenameable`, Rename gizmo, labels, richer state log), `PlaceWorker_Elevator` (placement helper), About / def description / language keys.
- Probes (`ElevatorProfiler`, `ElevatorDiagnostics`, hitch detector in `MapComponent_ElevatorProbe`) kept, per decision. The place worker now measures `PlaceWorker.DrawGhost` and `PlaceWorker.DrawPlaceMouseAttachments`.

### Deviations from the spec

| Change | Reason |
| --- | --- |
| `UnityEngine.IMGUIModule` reference added to the csproj | The cursor text uses `GUI.color`; `GUI` lives in that module, not in `CoreModule`. The spec did not list it. |
| `ILevelLabelProvider.TryGetLabel` has an extra `out int sortValue` and the interface has `ProviderName` | The spec's `SortKey` needs each provider's order value (DeepRim depth, MultiFloors level) and the warning log needs a provider name. |
| `LevelLabels.SortKey` returns `long`, not `int` | It packs one value per provider plus the map id into one number; `int` would overflow. Order: DeepRim depth, then MultiFloors level, then map id. A map a provider doesn't know sorts at that provider's neutral value, so the surface comes first. |
| Candidate order comes from `OrderBy(SortKey)` | As specified; `SortKey` is computed once per candidate. |

## 2. Test results (human, in game)

### Round 1

| Area | Scenario | Result |
| --- | --- | --- |
| Linking | Built elevators on the surface, MF levels and DR layers | No errors. Initially "Not connected". Icon works. |
| Linking | Connections ground–MF, MF–DR, ground–DR | Established correctly. "Connected to" is correct on both ends. |
| Linking | Offsets of linked elevators | Same offset on both ends, tested at (0,0,0) and other positions. |
| Linking | "Connect to…" with ambiguous candidates | Menu correct across DR, surface and MF levels. |
| Linking | Save/load of links and of unconnected elevators | Both survive. |
| Linking | `Validation:` warnings | None seen. |
| Names | Rename | Works. The partner shows "Connected to <name> – <level> <mod tag>". Renaming after linking is reflected on the partner. |
| Names | Save/load | Names survive. |
| Labels | `map#27` / `map#25` | "90m (DeepRim)" and "B1 (MultiFloors)", correct. |
| Placement | Map boundaries while placing | Correct. Several boundaries are shown when maps of different sizes exist. |
| Placement | "No counterpart on: …" text | Shown correctly for every map that does not cover the hovered cell. (Wording changed, 4.1.) |
| Placement | Cell outside a smaller map | Gets no placement indicator, as intended. |
| Placement | Highlight of available unconnected elevators | Correct, also in complex cases across MF and DR levels. |
| Placement | Hitches while dragging | No hitch lines. |
| Destruction | Deconstructing a connected elevator | Partner unlinked. Log: `Destroy`, `Unlinked…`, `DeSpawn`, `Discard`. |
| Destruction | Abandoning a DR layer | Unlinked elevators connected to the layer and to MF levels that originated from it. |
| Destruction | Destroying maps with Dev tools | Same, with the unlinks triggered. |
| General | Lag | None observed. |
| General | Old Step A saves | Not tested; not needed (the mod is new and unpublished, no backwards compatibility required). |

### Not tested

- Vanilla pocket maps (checklist item 6): the tester does not know how to create one. Group-root logic for pocket maps is therefore untested in game. (Candidate: an Anomaly undercave.)
- Label fallback with a provider disabled (checklist item 8), and a vanilla-only run (item 1) were not reported.

## 3. Issues and observations

### 3.1 Confusing level names when DR and MF are combined (observation; labels come from the mods)

- MF labels every map that an MF stack starts from as level 0, "G": the surface, but also a DR layer where stairs were built.
  - Result: a DR layer with stairs is labelled "90m (DeepRim), G (MultiFloors)". Both providers succeed on it, as designed in the spec (§4.2, last bullet).
  - Log example: `State: CargoElevator_Elevator4562452 on map#0 (G (MultiFloors)) offset=(41, 0, -3) root=map#0 partner=CargoElevator_Elevator4541125 partnerMap=map#27 (90m (DeepRim), G (MultiFloors)) valid=yes candidates=0`. The surface itself is also shown as "G (MultiFloors)".
- A DR shaft built on an MF level reuses DR's depth numbering (e.g. "10m"), although that level is below a 90m DR layer. This is DR's own naming and not our problem.
- DR does not label origin maps, so nothing similar happens on the DR side.
- **Decision:** the "G" label is a ground-level marker, not information we need. See 4.2 and 4.3 for the label changes.

## 4. Changes made after round 1 (implemented, build 0 errors / 0 warnings; confirmed in game in round 2, section 6)

1. **Cursor text rewritten.** `CargoElevator_HelperNoCounterpart` ("No counterpart on: …") replaced by `CargoElevator_HelperOutsideMap`: "Outside the boundaries of <level label>", where the label is the smaller map's level label (so it contains the map id and mod tag, e.g. "Outside the boundaries of map27 - 90m (DeepRim)").
2. **Level labels are prefixed with the map id**, in the form `map<id> - <label>` (`CargoElevator_MapPrefix`), e.g. "map27 - 90m (DeepRim)", "map25 - B1 (MultiFloors)", "map0 - surface". The prefix is part of `LevelLabels.LevelLabel`, so it applies everywhere: Connect menu, inspect text, link/unlink messages, placement helper, and logs. Elevator labels with a custom name were first `<name> – map27 - 90m (DeepRim)`; changed after round 2 (4.6, confirmed). `ElevatorUtil.MapDesc` (logs) now returns the level label directly, since it already contains the id (it used to print `map#27 (label)`).
3. **No "G" from MultiFloors.** `MultiFloorsLabelProvider` returns no label for level 0 (the map a stack starts from). A DR layer with stairs now shows only its DR label; the surface falls through to the new "surface" label (`CargoElevator_Surface`).
   - **Surface rule:** a map with no mod label, whose parent is not a `PocketMapParent`, is called "surface". This also covers any other non-pocket map on a world tile (e.g. a quest site map) and a DR layer whose provider failed; none of these were distinguished.
   - **Pocket maps are unchanged (user decision, untested):** they keep the generic fallback "<parent label> (map N)", without the new prefix, which would repeat the id. A pocket map that is the ground of an MF stack now also loses its "G" (the provider rule above has no pocket-map exception), so it falls back to that generic label. MF levels above or below a pocket map get the prefix like any other MF level.
4. **"Go to partner" gizmo** (normal, shown only while connected): `CameraJumper.TryJumpAndSelect(partner)`, which also switches maps. Icon is the vanilla magnifier `UI/Widgets/Search` as a placeholder (`ElevatorTextures.GoToPartner`). New keys `CargoElevator_GoToPartner`, `CargoElevator_GoToPartnerDesc`.
5. **Log messages include the custom name.** New `Building_Elevator.LogId` = thing id plus the name in quotes if the elevator has one (`Elevator4562452 "Ore lift"`); all log lines in `Building_Elevator` use it. Exception: the rename log line prints the plain thing id, because the name is part of the line.

Files touched: `LevelLabels.cs`, `MultiFloorsLabelProvider.cs`, `ElevatorUtil.cs`, `Building_Elevator.cs`, `PlaceWorker_Elevator.cs`, `ElevatorTextures.cs`, `Dialog_RenameElevator.cs`, `Languages/English/Keyed/CargoElevator.xml`.

## 5. Decisions

- Backwards compatibility with Step A saves is not a concern (no need for checklist item 11).
- Level labels remain UI text only; pairing never depends on them.
- Pocket maps are left as they were until the tester can create one.

## 6. Round 2 test results (human, in game)

| Scenario | Result |
| --- | --- |
| Labels with DR + MF stacks | Correct. No "G" on the surface or on DR layers. |
| Cursor text outside a smaller map | OK. |
| "Go to partner" | Works. Absent when not connected, and removed when the partner is removed. |
| Log lines | Show custom names. |
| Connect menu | OK. |
| **Vanilla-only run** (checklist item 1): vanilla + main DLCs with only CargoElevator loaded | Loads without errors; elevator buildings can be placed without errors. This counts as the vanilla-only run. |

### Change after round 2 (4.6) (implemented, build 0 errors / 0 warnings; confirmed in game)

The text of a named elevator's label used "–" between name and level, which produced too many dashes next to the map prefix ("Connected to Ore lift – map27 - 90m (DeepRim)"). `CargoElevator_ElevatorOnLevel` is now `{0} on {1}`, giving **"Connected to Ore lift on map27 - 90m (DeepRim)"**. Unnamed elevators are unchanged ("Connected to map27 - 90m (DeepRim)"). The key is shared, so the same wording appears in the Connect menu, link messages and the placement helper ("Connects to: Ore lift on map…"). Only the language file changed.

## 7. Open items

- **Pocket maps (checklist item 6): postponed.** The tester can't create one yet; group-root logic and the pocket-map labels stay untested until then (candidate: an Anomaly undercave).
- **Label fallback with a provider disabled (checklist item 8): not tested.** What it means: each label provider (DeepRim, MultiFloors) switches itself off if reading the other mod fails (renamed class or field after a mod update); labels then fall back to the generic form with one warning in the log. It can only be seen by breaking a provider on purpose (e.g. temporarily misspelling a reflected member name in code), so it was skipped as optional. Low priority; the DeepRim/MultiFloors absent case is already covered by the vanilla-only run.
- Remove the probes at some point (decision pending).
