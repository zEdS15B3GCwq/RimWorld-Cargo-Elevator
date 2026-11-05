# CargoElevator – Step H results

Branch `stepH`, from `stepF` at `5c49417` (clean, so no commit was needed before branching). Step G (VFE checks) had no code; nothing was skipped in the code.

Task order (human): 6 (name change) first, then the human tests, then 1–5, 7, 8.

| # | Task | State | Section |
| --- | --- | --- | --- |
| 6 | Name change DeepElevator → CargoElevator | done, **tested** | §1 |
| 1 | Extant tasks of `pre_stepH.md` (settings, logging setting, build menu) | done, builds; not tested | §2, §4, §5 |
| 2 | Remove repetitive logging | **dropped** (human): replaced by the verbose-logging setting | §2, §4 |
| 3 | Remove the profiler | done, builds | §3 |
| 4 | Performance review | done: `performance_review.md` | §8 |
| 5 | Own build menu category + BAM patch | done, builds; not tested | §5 |
| 7 | Useful building XML nodes from the examples | done: review, three nodes added, the rest proposed | §7 |
| 8 | Own research project | done, builds; not tested | §6 |

Every change after the rename is built (`dotnet build`, 0 warnings, 0 errors) but **not run in game**. The test list is in §9.

## 1. Name change (task 6)

**Name:** "Cargo Elevator" (`CargoElevator` in defs, code and file names). I kept it: it says what the building does without tying it to one level mod, and "cargo" fits since items ride it (pawns use the loader's portal). Alternatives I considered and rejected: "Level Lift" (vaguer), "Cargo Lift" (same meaning, "elevator" matches MultiFloors' and the vanilla wording). Before publishing, check the Workshop for an existing mod with the same name.

### 1.1 What changed

| Before | After |
| --- | --- |
| mod folder `DeepElevator/` | `CargoElevator/` |
| `DeepElevator.csproj`, `DeepElevator.slnx` | `CargoElevator.csproj`, `CargoElevator.slnx` (RootNamespace, AssemblyName, Product, OutputPath, Compile/None globs) |
| `DeepElevator.dll` | `CargoElevator.dll` (old DLL removed from git, new one built and committed) |
| About: name `DeepElevator`, packageId `FT.deepelevator` | name **`Cargo Elevator`** (with a space; it's the display name), packageId **`FT.cargoelevator`** |
| namespace `DeepElevator`, `DeepElevatorDefOf` | `CargoElevator`, `CargoElevatorDefOf` (file renamed too) |
| defs `DeepElevatorBase`, `DeepElevator_Elevator`, `DeepElevator_ElevatorLarge`, `DeepElevator_TransportLoader` | `CargoElevatorBase`, `CargoElevator_Elevator`, `CargoElevator_ElevatorLarge`, `CargoElevator_TransportLoader` |
| def files `DeepElevator_Elevator.xml`, `DeepElevator_TransportLoader.xml` | `CargoElevator_Elevator.xml`, `CargoElevator_TransportLoader.xml` |
| labels "deep elevator", "large deep elevator" (and the large one's description) | "cargo elevator", "large cargo elevator" |
| language file `Keyed/DeepElevator.xml`, keys `DeepElevator_*` | `Keyed/CargoElevator.xml`, keys `CargoElevator_*` (all uses in code renamed with them) |
| texture folder `Textures/Things/Buildings/DeepElevator/`, files `DeepElevator*.png` | `…/CargoElevator/`, files `CargoElevator*.png` (texPaths and `ElevatorTextures` icon names follow) |
| Harmony id `FT.deepelevator` | `FT.cargoelevator` |
| log prefix `[DeepElevator] ` | `[CargoElevator] ` |
| `.gitattributes` `DeepElevator/** text eol=crlf` | `CargoElevator/** text eol=crlf` |
| all `*.md` docs (plan, step plans, results, pre-step notes, `test_rates.md`) | renamed to match (human's exception for this task) |

**Extra fix found on the way:** the large elevator's sprite file was `DeepElevator_large.png` (lower-case l) while its def's `texPath` says `…_Large`. That only worked because Windows paths ignore case; on Linux or macOS the large elevator would likely have shown no texture. The file is now `CargoElevator_Large.png`, matching the def.

All texture paths named in defs and code were checked against the files after the rename (all present). `dotnet build CargoElevator.csproj`: 0 warnings, 0 errors.

### 1.2 Not renamed, on purpose

- **The repository folder** `C:\Code\Rimworld\DeepElevator`. It's this session's working folder (and the Claude memory folder is keyed by it), so renaming it mid-session would break things. Docs that name the repo root keep `C:\Code\Rimworld\DeepElevator` (`plan_stepA.md`, `plan_stepB.md`, `plan_stepD.md`). Rename it yourself between sessions if you want; nothing in the build depends on it.
- **`rate_test_large.log`, `rate_test_small.log`**: raw game output captured in Step D (`[DeepElevator]` prefixes). Left as recorded.
- `Examples/`, `include/` (not ours).
- An **empty** leftover folder `DeepElevator\Textures\Things\Buildings\DeepElevator\` in the repo could not be deleted ("device or resource busy": some program, probably Explorer or Visual Studio, has it open). It's empty and git ignores it; delete it by hand. The same lock first blocked `git mv` of the whole folder, so files were moved one by one (history is kept as renames).
- `.vs/` (Visual Studio's cache for the old `.slnx`): open `CargoElevator.slnx` instead; the old cache can be deleted.

### 1.3 For the in-game test

- The game loads the mod from a copy in `D:\G\RimWorld\Mods\_DeepElevator` (a plain folder, not a link). Copy `CargoElevator\` (About, Defs, Languages, Textures, 1.6, loadFolders.xml) to e.g. `D:\G\RimWorld\Mods\_CargoElevator` and **remove `_DeepElevator`**, otherwise both show in the mod list.
- The packageId changed, so the mod list sees a new mod: enable **Cargo Elevator** (after Harmony, and after DeepRim/MultiFloors if used).
- Def names changed, so elevators and loaders in old saves won't load. Test on a new colony or rebuild them.
- What to look for: the buildings in the architect menu (still `Misc`) with the new labels; textures for all three buildings, both elevator light overlays, the loader light, and the gizmo icons; no raw `CargoElevator_…` keys in the UI (a missing string shows the key); log lines start with `[CargoElevator]`; Harmony line at startup.

### 1.4 Commits

- `5c49417` (before; `stepF`)
- `1d01e96` H: rename DeepElevator to CargoElevator (`stepH`)

**Human test (after 1.3):** the rename works without problems. The empty leftover folder was removed by hand. The repo folder is renamed by the human between sessions, along with refreshing `obj/` and `.vs/`.

## 2. Decisions on `pre_stepH.md` (human, after the rename test)

| `pre_stepH.md` §6 | Decision |
| --- | --- |
| 1. Build menu | Own category; under BAM, move to where MultiFloors' buildings go (task 5). **Add BAM to `loadAfter`.** |
| 2. One drop-down button for both sizes | **No.** Drop-downs are for one building in different materials; the small and large elevators stay separate buttons. |
| 3. Settings | Most of §4.2, with verbose logging. **No retry-delay setting.** **New:** a setting to turn off the loader's blink: the light then stays on steadily while items wait in it. |
| 4. Logging | **Verbose logging setting** (pre_stepH §4.3), keeping the existing log lines. This replaces task 2 (removing repetitive lines), which is dropped. |
| 5. Idle-switch sync | **Not added.** |

## 3. Profiler removed (task 3)

Removed: `ElevatorProfiler.cs`, `MapComponent_ElevatorProbe.cs`, and the four `using (ElevatorProfiler.Measure(...))` wrappers in `PlaceWorker_Elevator` (`DrawGhost`, `DrawPlaceMouseAttachments`) and `PlaceWorker_TransportLoader` (the same two). The bodies are unchanged, only un-indented.

**What it did** (a Step A probe, from the "20000 `AllowsPlacing` calls per frame" investigation, results_stepA §3.2):
- `ElevatorProfiler`: a stopwatch per named code section (call count, total and max milliseconds, an optional note). Only the four place-worker draw methods were still measured.
- The hitch detector in `MapComponent_ElevatorProbe.MapComponentUpdate` (every frame, for the visible map): if a frame took longer than 150 ms while a build designator was selected or our measured code had run, it logged a warning "Hitch: previous frame took … ms … Our code during that frame: …" with the profiler's numbers; otherwise it cleared them each frame.
- The same map component also logged `MapComponent.MapRemoved: map#… tile=…` when a map was removed (to check the order of map-removal callbacks in Step A).

**What will be missing:**
- No automatic "Hitch" warnings any more. A freeze while placing an elevator or a loader is no longer tied to (or ruled out for) our place workers by the log. If one is suspected later, a profiler such as Dubs Performance Analyzer does the same job better.
- No `MapComponent.MapRemoved` line. Each elevator still logs its own `Notify_MyMapRemoved` (verbose) and unlinks, so link handling on map removal stays visible.
- A per-frame cost goes away: one `MapComponentUpdate` call and the Stopwatch reads in the place workers (tiny, but it was debug code).
- **Saves made since the rename** still list the removed map component, so loading one may log one error about a missing type `CargoElevator.MapComponent_ElevatorProbe`; the game drops it and goes on. New saves don't have it.

Kept on purpose: `ElevatorDiagnostics` (two lines per elevator def at startup; now verbose-only, §4), the disabled "Pawn delivery refused" probe comment in `AcceptsPawnDelivery`, and the `Discard` lifecycle log.

## 4. Mod settings and verbose logging (tasks 1 and 2)

Options → Mod settings → **Cargo Elevator**. Changes take effect when the window closes (`WriteSettings`), also for buildings already built. The page shows a hint saying so.

| Section | Setting | Default | Applies to |
| --- | --- | --- | --- |
| Lights | Elevators light up their surroundings | on | `IThingGlower.ShouldBeLitNow` of the elevator; the lamp on the sprite stays |
| | Transport loaders light up their surroundings | on | the same for the loader |
| | Transport loaders blink while items wait | on | off = steady orange (lamp and light) for as long as items wait and no order runs (the human's new setting) |
| one heading per elevator def (both sizes) | Ticks between sent pieces (with "N per second") | the def's value (60 / 15) | `ElevatorSettingsExtension.ticksPerStack`, range 1–600 |
| | Buffer slots (maximum) | the def's value (4 / 8) | `maxSlots` (range minSlots–20) and `defaultSlots` |
| | Power while transferring (W) | the def's value (100 / 300) | `CompProperties_Power.basePowerConsumption` (private; written through `AccessTools.FieldRefAccess`) |
| | Power while idle (W) | the def's value (10 / 30) | `idlePowerDraw` |
| Logging | Verbose logging | **off** | `ElevatorUtil.Log` |
| footer | Reset to defaults | | everything back to the def values, lights on, verbose off |

How it works (the FasterBiosculpterPod pattern, pre_stepH §4.1):
- `CargoElevatorMod : Mod` loads the settings in its constructor (before defs exist) and draws the page (`Listing_Standard` in a scroll view; numeric fields with edit buffers; tooltips with the allowed range). The settings file is the game's own `Config/Mod_<folder>_CargoElevatorMod.xml`.
- `CargoElevatorSettings : ModSettings`. Per-def values are stored by defName, and **-1 means "not changed"**: the def keeps the value it had at startup, including other mods' patches. So the file only holds what the player changed, and "default" in the UI is the real def value, not a copy in code. The defaults are captured on first use, at startup, before anything is written.
- `CargoElevatorStartup` (`[StaticConstructorOnStartup]`) writes the values into the defs, then logs the def facts (`ElevatorDiagnostics.LogDefs`, verbose only), so the diagnostics show the values in use. `ElevatorDiagnostics` lost its own static constructor for that.
- After writing, every spawned elevator gets `Notify_ModSettingsChanged`: it lowers its slots to a reduced maximum (buffered items stay), sets the new wattage at once (`UpdatePowerDraw(force: true)`), and re-registers the glow. Every loader re-registers its glow. Elevators also lower their slots on spawn, for minified or saved ones. (The name avoids `Building_Storage.Notify_SettingsChanged`, which is vanilla's storage-settings callback.)
- New elevators start with all their slots when the def does (both shipped defs do), so raising the maximum also raises what new elevators get.
- The "(N per second)" readout and the active hold window (`ticksPerStack + 30`) follow the rate automatically, as they're computed from the def.

Not included, on purpose:
- Retry delay (human: not needed).
- Defaults for new elevators (FIFO, merge, drop, allow insert; marked optional in pre_stepH §4.2). Each elevator has its own toggles, and the shipped defaults are what everyone has tested. Easy to add later in the same pattern.
- Glow radius and colours (decided in Step F to stay as they are).

**Logging:** `ElevatorUtil.Log` writes only with verbose logging on; `LogWarn` always writes. Every existing line is kept (human decision; task 2's removal list is dropped). `LogTargetsIfChanged` returns before building its string when verbose is off (pre_stepH §2 item 3). With verbose off, a normal game logs nothing from us except warnings.

Implementation notes:
- `Text.Font`, `Text.Anchor` and `Widgets.ButtonText` need `UnityEngine.TextRenderingModule`, which the project doesn't reference. Rather than add a reference, the headings are coloured (`AsTipTitle`) instead of in a larger font, the labels are offset by hand, and the reset button is a `Listing_Standard.ButtonText` in a small footer listing. Larger headings are not needed (human, §11).
- Files: `CargoElevatorMod.cs`, `CargoElevatorSettings.cs`, `CargoElevatorStartup.cs` (new); `ElevatorUtil.cs`, `ElevatorDiagnostics.cs`, `ElevatorSettingsExtension.cs` (comment), `Building_Elevator.cs` / `.Buffer.cs` / `.Power.cs` / `.Send.cs`, `Building_TransportLoader.cs`, the language file.

## 5. Build menu category (task 5)

- New `DesignationCategoryDef` **`CargoElevator`** ("Cargo elevator"; order 640, so right after MultiFloors' tab at 650 and before Production at 600; Cancel and Deconstruct). It needs our research (§6), like vanilla's Power tab needs Electricity, so it doesn't sit empty early in the game. Both elevators and the loader use it (was `Misc`).
- **Better Architect Menu** (`ferny.BetterArchitect`): one patch, `Patches/CargoElevator_BetterArchitectMenu.xml` (reworked in §11; first built with load folders under `Mods/`):
  - **BAM + MultiFloors**: moves our three buildings to **`MultiFloors`**. That is MF's tab, which BAM's own MF patch relabels "Elevation" and nests under Structure. So we appear next to MF's stairs and elevators, as asked.
  - **BAM without MultiFloors**: moves them to **`Ferny_Elevation`**. That is BAM's own "Elevation" tab under Structure ("Build stairs, ladders, and elevators between levels."; As Above So Below uses it). Same place for the player, and no reference to MF's def when MF is missing.
  - Both patches also **remove our own tab**, so BAM doesn't show an empty one.
  - The patch replaces `designationCategory` on the abstract `CargoElevatorBase` and the loader (XML inheritance is resolved after patching).
- `About.xml`: BAM is in **`loadBefore`** (human, §11; first it was in `loadAfter`).
- Not done (human): one drop-down button for both sizes.
- When shipping: `Patches/` is copied with the rest (About, Defs, Languages, Patches, Textures, 1.6, loadFolders.xml).

## 6. Research project (task 8)

`Defs/ResearchProjectDefs/CargoElevator_Research.xml`: **`CargoElevator_Research`**, "cargo elevators", **600** (as Solar panel), Industrial, prerequisite **Electricity** only. Position **x 6.00, y 5.50**.
- The column the human named: Advanced lights (`ColoredLights`, 2.70), Gunsmithing (3.40), Flak armor (4.10), Smokepop packs (4.80), steps of 0.70, so the next slot is 5.50. Nothing else sits at (6.00, 5.50) on the main tab in Core or any DLC (checked all `ResearchProjectDef`s in `Data/*/Defs`). The main tab already goes down to 5.60 (Plate armor, Long blades, Heavy bridges), so the tree doesn't get taller.
- Those four depend on Machining (5.00, 3.50); ours draws its line from Electricity (4.00, 3.00), so the line passes below Machining's box. Looks fine on paper, not yet seen in game.
- It unlocks both elevators, the loader (`researchPrerequisites` on `CargoElevatorBase` and the loader) and our architect tab. The elevator's "build loader" gizmo already hides itself until the loader can be built (`FindAllowedDesignator`).
- `generalRules` added like vanilla (subject, story and gerund lines for generated texts).

## 7. Building XML nodes from the examples (task 7)

Compared: every building `ThingDef` in DeepRim, MultiFloors, VFE Factory, Adaptive Storage Framework and FasterBiosculpterPod, plus vanilla `BuildingBase` and `ShelfBase`, against our three defs (the tags we don't set, with how many defs use each).

### 7.1 `staticSunShadowHeight` and `castEdgeShadows` (the human's example)

- **`staticSunShadowHeight`** (float 0–1, default 0). `SectionLayer_SunShadows` draws a sun shadow for every cell of an edifice with a value above 0. It's baked into the section mesh, with the darkness taken from the height, and it moves with the sun over the day. It's the "this thing has volume" shadow: walls 1.0, shelves and dressers 0.5, tool cabinet 0.35, sarcophagus 0.3, benches, tables, batteries and sandbags 0.2, beds 0.17. It's an error to set it together with `graphicData/shadowData` (we don't).
- **`castEdgeShadows`** (bool). `SectionLayer_EdgeShadows` draws the soft dark rim on the floor around the edifice. Shelves, benches and most furniture have it.
- **Cost:** none per frame; both are part of the static section mesh, rebuilt only when that section changes.
- **Do we need them? Will it look cooler?** Not needed for anything functional, but without them our buildings look flat next to vanilla furniture (a shelf has both). **Added** (cosmetic only, easy to change):
  - elevators (both sizes): `castEdgeShadows` true, `staticSunShadowHeight` **0.6** (changed from 0.35 in §11: the elevators are taller than the loader's crates). A tall frame: above shelves (0.5), below walls (1.0).
  - loader: `castEdgeShadows` true, `staticSunShadowHeight` **0.35** (changed from 0.5 in §11), stacked crates at tool-cabinet height.
  - **To judge in game**, outdoors in daylight. Underground (DeepRim, roofed) there's little sun to cast them, but the edge shadow still shows. The height sets how dark and how long the shadow is.

### 7.2 Also added

- **`uiOrder`** 3140 / 3141 / 3142 (small, large, loader). It keeps our three together and in that order in our tab, and after MultiFloors' elevators (3110, 3130) when they share MF's tab under BAM.

### 7.3 Proposed, not added (they change gameplay; your call)

| Node | Seen in | What it would do for us | Proposal |
| --- | --- | --- | --- |
| `terrainAffordanceNeeded` | MF elevators and stairs: `Heavy`; ours inherit `Light` from `BuildingBase` | `Heavy` = not on soil, sand or bridges, only stone, rough rock and heavy floors | `Medium` for the large elevator would fit its cost and size; `Heavy` like MF is plausible for a lift. Check that MF upper floors and DeepRim layers give it (MF builds `Heavy` elevators there, so they do) |
| `holdsRoof` | MF elevators and stairs, DeepRim shaft | the building supports a roof like a column | fits a lift shaft; harmless. Mostly matters on the surface |
| `constructionSkillPrerequisite` | MF, VFE (typically 4–8) | minimum Construction skill to build | e.g. 5 small, 8 large, none for the loader |
| `minifiedDef` (+ `thingCategories`) | MF, VFE | uninstall and reinstall | possible: our `DeSpawn` already drops the buffer and unlinks, and spawning auto-pairs. Needs a test that a reinstalled elevator re-pairs and that loaders relink by hand |
| `building/relatedBuildCommands` | MF, VFE | build buttons for related buildings on the selected building | XML replacement for our coded "build loader" gizmo (`BuildCopyCommandUtility`), and the loader could offer both elevators. Same result; not needed |
| `building/paintable` | MF stairs, vanilla shelves | Ideology/1.6 painting | our sprites are full-colour `Graphic_Single` and the lights are overlays, so a paint colour would tint the whole sprite but not the lamp. Not recommended |
| `neverMultiSelect` | MF stairs | no box-select | no: multi-select is useful for our toggles |
| `building/ai_neverTrashThis`, `destroyable`, `building/deconstructible`, `building/isTargetable`, `claimable`, `isInert` | MF, DeepRim | protect stairs and shafts from raiders or the player | no: our buildings are ordinary machines; losing one already unlinks cleanly |
| `preventSkyfallersLandingOn` | MF | drop pods avoid the cell | no |
| `designationHotKey`, `uiIconScale`, `uiIconOffset` | VFE, MF | menu hotkey and icon framing | only if the icons look off in the menu |
| `blockPlants`, `useHitPoints`, `leaveResourcesWhenKilled`, `building/expandHomeArea`, `building/buildingTags`, `drawGUIOverlay`, `storageGroupTag`, `blueprintClass` | various | defaults are already right for us, or not applicable (`storageGroupTag` is left out on purpose) | no |

## 8. Performance review (task 4) and review of the pre-H thread

`performance_review.md` (new). The other thread's `pre_stepH.md` §2 was checked item by item against the code and the decompiled game, and every item holds (table in performance_review §3). Additions it didn't list: gizmos while selected, the exit and enter job patches, buffered items ticking, the settings pass, and the new shadows. All are negligible. The two items that grow with building count are verbose logging (now off by default) and the loader blink (now optional).

Other points of `pre_stepH.md` reviewed:
- §1 (build menu facts): correct. The BAM part it left open is answered in §5 here (BAM maps MF's tab to Structure > Elevation by relabelling and nesting it, and has its own `Ferny_Elevation`; DeepRim's shaft goes to `Ferny_ProductionInfrastructure`).
- §3 (preview image): not touched in this step.
- §4 (settings): followed, with the human's changes (§2, §4).

## 9. Test list

1. Copy the whole `CargoElevator\` folder, **including the new `Patches\`**, to the game's Mods folder (delete a `Mods\` subfolder from the game's copy if one was copied earlier). A save made since the rename may log one error about the removed `MapComponent_ElevatorProbe`; that's expected once.
2. **Research:** "cargo elevators" shows below Smokepop packs, cost 600, needs Electricity. Before it's done: no Cargo elevator tab, no buildings. After: the tab appears (after MultiFloors' tab if present, before Production) with small, large, loader in that order.
3. **Settings page:** opens, scrolls, tooltips, the "(default: …)" values match the defs (60/4/100/10 and 15/8/300/30), and reset works. Then change and close:
   - rate (e.g. small 30 → 2 per second; check with verbose logging or by watching);
   - max slots lowered below an elevator's current setting → it drops to the new maximum; raised → new elevators get all of it;
   - power: the inspect pane's "Power needed" changes at once on built elevators, both idle and active;
   - glow off/on for elevators and loaders: the light on the floor goes and comes back, the lamp on the sprite stays;
   - loader blink off: a loader with items and no order shows a steady light; on again: it blinks.
   - The values survive a game restart (settings file).
4. **Verbose logging:** off (default): no `[CargoElevator]` message lines during normal use, only warnings if something goes wrong. On: the familiar lines come back (startup diagnostics show after a restart).
5. **BAM** (`ferny.BetterArchitect`): with MultiFloors, our buildings sit in Structure > Elevation next to MF's; without MultiFloors, in BAM's Elevation; in both cases no separate Cargo elevator tab. Without BAM: our own tab.
6. **Looks:** edge and sun shadows of the elevators (0.6) and the loader (0.35) in daylight (§7.1).
7. Regression: transfer, holds, loader orders and pawn trips work as before (nothing in those paths changed except log gating).
8. **Heavy terrain** (§11): the elevators can't be placed on marsh, mud, soft sand, shallow water or bridges. They still can on soil, sand, gravel, rough stone, built floors, DeepRim layers and MultiFloors upper floors.
9. **Loader uninstall** (§11): uninstall a loader with items and an order in progress. The order is cancelled, the items are put down nearby, and the minified loader appears. Reinstall it next to an elevator: it links by itself (with the message), keeping its transfer switch and priority.

## 10. Commits (stepH)

| Commit | Content |
| --- | --- |
| `1d01e96` | rename DeepElevator → CargoElevator |
| `bc07170` | results: rename commit recorded |
| `507cc23` | profiler and hitch-detector map component removed |
| `80c8b04` | mod settings, verbose logging, glow and blink switches |
| `0cbc103` | own architect tab, BAM patches, BAM in `loadAfter` |
| `eb51cd1` | research project |
| `8bdae90` | edge and sun shadows, menu order |
| `99200e9` | `performance_review.md` |
| `6e6089a` | results §4–§10 |
| `721fd1e` | BAM patch moved to `Patches/` as one conditional patch; BAM in `loadBefore` |
| `a6dee9f` | loader minifiable, links by itself after a reinstall; loader shadow 0.35 |
| `a3faa60` | elevators need Heavy terrain; elevator shadow 0.6 |
| (next) | results §11 |

## 11. Human decisions after the first round, and changes

| # | Decision | Change |
| --- | --- | --- |
| 1 | No `loadAfter` BAM; load **before** BAM | `About.xml`: BAM moved from `loadAfter` to `loadBefore`. See the note below on why the order doesn't matter |
| 2 | Patches go in the customary `Patches/` folder | `Mods/` and the two conditional `loadFolders` entries removed. One file `Patches/CargoElevator_BetterArchitectMenu.xml` does the same with `PatchOperationConditional` |
| 3 | Uninstall/reinstall: **yes for the loader**, no for the elevators | loader: `minifiedDef MinifiedThing`, `thingCategories BuildingsMisc` |
| 4 | Heavy floor requirement: **yes** | `terrainAffordanceNeeded Heavy` on `CargoElevatorBase` (both elevators); the loader stays `Light` |
| 5 | Holding the roof: undecided | reasons below; nothing changed |
| 6 | Minimum construction skill: **no** | – |
| 7 | The elevators are taller than the crates | sun shadow: elevators **0.6**, loader **0.35** (they were 0.35 and 0.5, the wrong way round) |
| 8 | No larger headings in the settings | – |

**The BAM patch (1, 2).** It now checks what it refers to instead of mod names or packageIds:
- `Defs/DesignationCategoryDef[defName="Ferny_Elevation"]` exists → BAM is active. Then:
  - if `Defs/DesignationCategoryDef[defName="MultiFloors"]` exists → our buildings get `MultiFloors`, otherwise `Ferny_Elevation`;
  - and our own tab is removed.
- Without BAM, nothing happens and nothing is logged (a conditional with no match and no `nomatch` succeeds).
- `PatchOperationFindMod` would have been the other customary way, but it matches mod names only (`ModLister.HasActiveModWithName`), and for "BAM and MultiFloors" or "BAM without MultiFloors" it would need two nested levels anyway.
- The XPaths were checked with .NET `XmlDocument` (the same engine the game uses) on our defs: two `designationCategory` nodes, one tab.

**On load order (1).** The game first joins the XML of **all** active mods into one document, then runs every mod's patches on it, in load order, and only then turns the XML into defs and resolves def references (`LoadedModManager`: `CombineIntoUnifiedXML` → `ApplyPatches` → `ParseAndProcessXML`). So:
- Our patch sees BAM's and MultiFloors' defs whether we load before or after them.
- BAM's MultiFloors patch changes MultiFloors' tab def (label "Elevation", order, nesting under Structure). It doesn't rewrite our buildings' `designationCategory`, which only names that def, and the name is resolved after all patches. So BAM's change applies to our buildings at any load order.
- No patch of ours touches a node that BAM's patches touch, or the other way round.
- Loading before BAM is fine and is now set. Loading after would have worked too.

**Loader uninstall (3).** Uninstalling calls `DeSpawn` with mode `Vanish`, so the existing clean-up runs: the order is cancelled, the container is put down nearby, and the link is released. The minified loader is always empty. One code change: `DeSpawn` sets `autoLinkAllowed` back to true. A despawned loader only comes back by reinstalling, and a reinstalled loader then links by itself like a newly built one. The "never relink unasked" rule is still kept for a loader that stays where it is while its elevator is lost or rebuilt. The transfer switch and the priority are kept through the reinstall. The elevators are not minifiable (human).

**Heavy (4).** Checked against the vanilla terrain defs. Heavy is offered by soil, rich soil, sand, gravel, packed dirt, ice, mossy terrain, riverbank, broken asphalt, every built floor (`FloorBase`) and rough or smooth stone. It is not offered by marsh, mud, soft sand, shallow water or bridges. MultiFloors' own elevators and stairs need Heavy and are built on its upper floors and DeepRim layers, so ours can go there too.

**Holding the roof (5): reasons.** `holdsRoof` makes a building count as a roof support, like a wall or a column. Vanilla keeps a roof cell up only within about 6 cells of a support, and collapses unsupported roof when the last support in range goes away.

| For | Against |
| --- | --- |
| It looks right: a lift shaft or frame is structural. MultiFloors' elevators and stairs and DeepRim's shaft all set it | No function of ours needs it |
| An elevator in the middle of a big room would hold the roof there, so the player needs no extra column next to it | Removing or losing that elevator (deconstruction, a raid, fire) can **collapse the roof** it held. Collapse damages or kills pawns under it and destroys items, and the player may not expect it from a machine |
| | Underground (DeepRim, under mountain) it makes no difference: overhead mountain never collapses. So it only matters on the surface and on MultiFloors levels |
| | Not checked: whether MultiFloors uses roof supports for its upper levels (its stair placement checks the roof). Its own elevators hold the roof, so it is at least compatible |

Recommendation: **leave it off**. The gain is a column you don't have to build; the risk is a surprise roof collapse when an elevator is removed. If you want it anyway, it's one line on `CargoElevatorBase`, best tested together with MultiFloors.

## 12. Load order questions (human) and the roof decision

**Holding the roof: off** (human). Nothing changes.

**"The patches run in some order, so the result depends on it?"** Patches do run one after another, in load order. But the result only depends on the order when two patches touch the same XML nodes, or when one patch's condition looks at something another patch creates or changes. Neither is the case here:

| Patch | Touches | Changes |
| --- | --- | --- |
| BAM's MultiFloors patch (`Mods and Shit/Multifloors/Patches/multifloors patch.xml`) | MultiFloors' tab def `DesignationCategoryDef[defName="MultiFloors"]` | its `order` (850) and `label` ("Elevation"); adds a `modExtensions` entry (nested under Structure). **The defName stays `MultiFloors`.** |
| Ours (`Patches/CargoElevator_BetterArchitectMenu.xml`) | our own `CargoElevatorBase` and loader defs, and our own tab def | `designationCategory` becomes the text `MultiFloors` (or `Ferny_Elevation`); our tab is removed |

- BAM's "MF → Elevation" is a relabel and a move of MultiFloors' tab. It isn't a new tab: the tab's defName stays `MultiFloors`. Our buildings name that defName, and the name is only looked up after all patches, when the XML becomes defs.
- If BAM's patch runs first: the tab is relabelled, then our buildings are pointed at it. If ours runs first: our buildings are pointed at it, then the tab is relabelled. Either way the final XML is the same: our buildings in the tab with defName `MultiFloors`, which shows as Structure > Elevation.
- The two patches never touch the same node, so they give the same result in either order.

**"The node check depends on another mod having run?"** No. The two nodes we check are ordinary defs from the mods' `Defs` folders, not something a patch creates:
- `Ferny_Elevation`: `Better-Architect-Menu/Defs/DesignationCategories.xml`;
- `MultiFloors`: `MultiFloors/1.6/Defs/Designations/DesignationCategoryDefs.xml`.

The game loads every active mod's `Defs` XML and joins it into one document (`LoadModXML` → `CombineIntoUnifiedXML`) **before the first patch of any mod runs** (`ApplyPatches`). So both nodes are there for our patch whether BAM and MultiFloors load before or after us. The check would only depend on order if it looked for something that only a patch adds. It doesn't. BAM's patch keeps MultiFloors' tab def and its defName.

The one way this could break is a future BAM or MultiFloors version that renames or removes those defs. Then our patch quietly does nothing, and the buildings stay in our own tab. That's a safe failure.

`loadBefore` BAM stays as set; it does no harm.
