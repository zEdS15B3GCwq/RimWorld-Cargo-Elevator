# DeepElevator – Step H preparation: build menu, performance, preview, mod settings

Findings for the human's decisions before `plan_stepH.md` is written. Basis: the `stepF` branch as of `69d01a2`, decompiled RimWorld 1.6 (`include/decompiled`), the example mods in `Examples/` (DeepRim and FasterBiosculpterPod sources, MultiFloors 1.6 defs and assembly). Better Architect Menu (BAM) was not looked at, as asked. Nothing was run in game.

## 1. Build menu category

### 1.1 How it works in vanilla

- A buildable def names one `designationCategory` (a `DesignationCategoryDef` reference). That is the architect tab it appears in. Our elevators (`DeepElevatorBase`) and the loader use `Misc`.
- A `DesignationCategoryDef` is a top-level architect tab: `label`, `description`, `order` (position in the list; vanilla Structure is 700), `specialDesignatorClasses` (Cancel, Deconstruct, …), `showPowerGrid`, `researchPrerequisites`, `preferredColumn`. Vanilla has no sub-categories; a tab is a flat list.
- `designatorDropdown` (a `DesignatorDropdownGroupDef`) groups several buildables into one button with a drop-down inside a tab, as vanilla does for walls of different materials.
- Because the category is a def reference, naming a category from another mod directly in our def fails to resolve when that mod isn't loaded. The safe way is an XML patch that runs only when the mod is active: `PatchOperationFindMod` (matches the mod's **name** from `About.xml`, here `MultiFloors`). `MayRequire` works on defs and list items, not on a single field's value.

### 1.2 What the two mods do

| Mod | Category | Defined where |
| --- | --- | --- |
| MultiFloors | Its **own** tab `MultiFloors` (label "MultiFloors", "Build upwards and downwards.", `order` 650, just before Structure; Cancel, Deconstruct, Remove foundation). Stairs, elevators and pipe connectors use it. | `1.6/Defs/Designations/DesignationCategoryDefs.xml`; no English DefInjected, so the label shows as "MultiFloors" |
| DeepRim | Vanilla **`Production`** for the mining shaft | `1.6/Defs/ThingDefs_Buildings/Building_Buildable.xml` |

So neither mod defines "Elevation" or "Infrastructure". Those names come from BAM, which maps categories into its own main tabs (MultiFloors' tab → Build/Elevation; vanilla Production → Make/…, and the shaft ends up under Infrastructure there). That's a BAM question for the later discussion.

### 1.3 Options for us

| Option | Without MultiFloors | With MultiFloors | Work |
| --- | --- | --- | --- |
| **A. Join MultiFloors' tab when it's active** | stays where we choose (`Misc`, or B's own tab) | our buildings sit next to MF's stairs and elevators; under BAM they should land where MF's do (Build/Elevation), **to verify** | one patch file: `PatchOperationFindMod` `MultiFloors` → `PatchOperationReplace` on the `designationCategory` of `DeepElevatorBase` and the loader |
| B. Own tab (for example "Levels") | a top-level tab of our own, order next to Structure | the same own tab, a second one beside MF's | one `DesignationCategoryDef` plus a language entry. Under BAM it would be an unmapped tab until BAM is configured for it |
| C. Vanilla tab that fits better | e.g. `Production` like DeepRim, or `Furniture` (vanilla shelves) | same | one line per def |

Recommendation: **A plus C** (or A plus B). A puts us where MF players look, at no cost. For players without MF, keep `Misc` or move to a vanilla tab; I'd keep `Misc` unless you prefer one. A drop-down group for the 1x1 and the large elevator (`designatorDropdown`) is optional and would make one button for both sizes; the loader stays separate.

## 2. Performance review

No part of our code runs long per tick or per frame in normal play. The largest steady cost is the logging. Ordered by expected cost:

| # | Where | When | Cost | Notes |
| --- | --- | --- | --- | --- |
| 1 | `ElevatorUtil.Log` → `Verse.Log.Message`, unconditional | every delivery, absorb, pull and state change (`Delivered`, `Absorbed`, `Received`, `Targets`, …) | **highest**: each call captures a managed stack trace (`StackTraceUtility.ExtractStackTrace`) and writes to Unity's `Player.log`. A large elevator sends 4 pieces a second, plus absorb lines on the other end, so a busy pair writes several lines a second. It also fills the in-game log | the only fix is fewer lines: a "verbose logging" mod setting (default off), as DeepRim has. That changes the current rule "logging stays unconditional", so it's your decision (§4.3) |
| 2 | Loader glow blink | twice a second per **waiting** loader (items in it, no order) | a light registration change: vanilla re-floods that light's area (Burst job) and rebuilds the glow and roof mesh sections in its radius (3) | already the Step F open item. Fine for a few loaders; untested with many. Fallback: keep the glow on while waiting and blink only the overlay (the overlay costs nothing extra) |
| 3 | `TrySend` → `BuildDeliveryTargets`, `LogTargetsIfChanged` | per send attempt: 1/s (1x1) or 4/s (large) while items are buffered; with FIFO off and everything held, once per buffered entry per retry | small: a scan of the ring of cells around the partner (8 or 12), a few `Accepts` calls, LINQ lists, and **a target-description string built on every attempt** just to compare with the last one | cheap fix: build the comparison string only when logging is on (with §4.3), or compare the targets without a string |
| 4 | `PullFromLoaders` | every tick, while the elevator is operational and has linked loaders | small: per loader with transfer on, per container entry `RoomFor` (counts the used slots and scans the buffer, at most a few entries). Exits at once with no loaders | when the buffer is full, the same "no room" result is recomputed every tick for each waiting entry. Could be skipped while nothing changed, but the numbers are tiny (buffer ≤ 8 slots) |
| 5 | Inspect string (`GetInspectString`) | every frame **while selected** | small: level labels through the DeepRim/MultiFloors providers (reflection calls, not cached), translations, and for an unlinked elevator `UnconnectedCandidates`, a scan of the other maps' elevators | only one building is inspected at a time; acceptable |
| 6 | `DrawAt` (elevator, loader) | every frame per building on screen | negligible: reads the status and draws one cached overlay | |
| 7 | Harmony `IsGoodStoreCell` postfix | per cell checked by any haul search on the map | negligible: one array lookup (`SlotGroupParentAt`), and only for elevator cells the refusal rule (fields, the filter, a buffer scan) | |
| 8 | Placement helper (`PlaceWorker_*`) | per frame while placing an elevator or a loader | small: cache rebuilt every 60 frames (scans other levels' elevators); `DrawFieldEdges` per frame | measured fine in Step B |
| 9 | Temporary probes: `MapComponent_ElevatorProbe` (per frame), `ElevatorProfiler.Measure` in the place workers | per frame | negligible, but they're debug code | removal is planned for release prep (`plan.md` §7) |
| 10 | Every-30-tick blocks (power draw, status log, glow), every-250-tick backstops | periodic, hash-spread | negligible | |

**Recommendation:** fix 1 with a logging setting (default off, so releases are quiet), fix 3 with it, and keep 2 on the test list. The rest doesn't need changes.

## 3. Mod preview image (`About/Preview.png`)

- **Loading** (`ModMetaData.PreviewImage`): the file `About/Preview.png` is read with Unity's `ImageConversion.LoadImage`, so any size works and no power of two is needed. A JPEG renamed to `.png` would load too, but keep it a real PNG.
- **Mod list** (`Page_ModsConfig`): shown centred, scaled to the panel width but **never above its own width**, and its height limited to 35% of the panel's height, keeping the aspect ratio. A small image therefore shows small; a wide one is limited by the width.
- **Steam Workshop**: the same file is uploaded as the item preview (`SteamUGC.SetItemPreview`). Steam's limit for the preview file is **1 MB** (a Steam rule, from memory, not in the game code); an upload with a larger file fails.
- **Recommended:** 16:9, **1280x720** or **1920x1080**, under 1 MB (PNG compressed; reduce colours if needed). `plan.md` §5's 640x360 is the old common size; it works but looks soft on large screens.
- Optional, separate: `About/ModIcon.png`, a small square icon shown next to the mod's name in some lists (`ModMetaData.Icon`); without it the game shows a generic icon.

## 4. Mod settings

### 4.1 How the examples do it

All three use the vanilla pattern:

- A class derived from `Mod` (constructed by the game at startup), which calls `GetSettings<T>()` and overrides `SettingsCategory()` (the name in Options → Mod settings), `DoSettingsWindowContents(Rect)` (draws the page every frame while open) and optionally `WriteSettings()` (called when the window closes).
- A class derived from `ModSettings` with public fields and `ExposeData` (`Scribe_Values.Look` per field). The game saves it to `Config/Mod_<folder>_<class>.xml`.
- Drawing with `Listing_Standard`: `Begin(rect)`, then `CheckboxLabeled`, `SliderLabeled` / `IntSliderLabeled`, `Label`, `GapLine`, `End()`.

| Example | Notes |
| --- | --- |
| **FasterBiosculpterPod** (rewritten to vanilla) | The clearest one. A scroll view (`Widgets.BeginScrollView` around the listing, with the height remembered from the last frame), headings, numeric text fields with per-field buffers (`Widgets.TextFieldNumeric`), checkboxes, a **"Reset to defaults"** button, `ClampValues()` in `WriteSettings`. Applies the values **by writing them into the defs** (`ApplySettings`) at startup (`[StaticConstructorOnStartup]`, after defs load) and again in `WriteSettings`, so the rest of the code reads the defs as before. Saves the original def values at startup for the reset |
| DeepRim | One page with `CheckboxLabeled` and `SliderLabeled` with tooltips; has a **verbose logging** checkbox |
| MultiFloors | A left column of categories (General, Map generation, Needs, Stairs, Work) selecting the page on the right, inside a scroll view; checkboxes with tooltips and int sliders. More than we need |

### 4.2 What we would expose

Our tunables already live in the defs (`ElevatorSettingsExtension`, `CompProperties_Power`, `CompProperties_Glower`) and differ per size. So the FasterBiosculpterPod approach fits: settings are written into the defs at startup and on close, the code keeps reading the defs, and "reset" restores the values the defs had at startup.

| Setting | Per size? | Applies to | Notes |
| --- | --- | --- | --- |
| Transfer rate (ticks per piece) | yes (60 / 15) | `ticksPerStack` | shown as pieces per second is friendlier |
| Retry delay for held items | yes | `heldRetryTicks` (-1 = rate) | maybe not worth showing |
| Active and idle power (W) | yes (100/10, 300/30) | `CompProperties_Power` | `basePowerConsumption` is **private**: write it through `AccessTools.FieldRefAccess` (Harmony is already a dependency). Existing elevators pick it up at their next draw change; a forced refresh on close is cleaner |
| Buffer slots (max) | yes (4 / 8) | `maxSlots` | existing elevators above a lowered max are clamped |
| Glow on/off | elevator and loader | `IThingGlower.ShouldBeLitNow` also checks the setting | on close, refresh every spawned building's glow once |
| Defaults for new elevators (FIFO, merge, drop, allow insert) | no | `default*` | optional |
| Verbose logging | no | `ElevatorUtil.Log` | see 4.3 |

Changes take effect when the settings window closes, without a restart.

### 4.3 Logging

Today logging is unconditional (your rule). For release, a **"verbose logging"** checkbox, default off, would keep warnings and errors always and drop the routine lines. That fixes §2 item 1. `plan.md` §7 also lists "gate logs on `Prefs.DevMode`"; a setting is clearer because the player controls it. Your call: keep unconditional until release prep, or add the setting in Step H.

## 5. Proposed Step H

1. Mod settings (§4.2), applied to the defs, with reset to defaults and the glow toggle.
2. The verbose logging setting (§4.3), if you agree, including the cheap send-path fix (§2 item 3).
3. Optional: the build menu patch (§1.3 A) and the drop-down group, since they're one small XML file.
4. Optional: the approved pair idle-switch sync (`plan.md` §7).

Release prep stays separate: probe removal, `About/Preview.png` (supplied by you), the mod description.

## 6. Decisions needed

1. Build menu: join MultiFloors' tab when it's active (A)? Without MF: keep `Misc`, a vanilla tab, or our own tab?
2. One drop-down button for both elevator sizes?
3. Which settings from §4.2 to expose, and power and rate per size or one multiplier for both?
4. Logging: add the "verbose logging" setting (default off) in Step H?
5. Include the idle-switch sync in Step H?
