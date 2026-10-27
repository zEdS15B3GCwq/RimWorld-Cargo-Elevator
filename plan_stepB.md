# DeepElevator – Step B implementation spec (handoff)

> **Status: implemented and tested** (see `results_stepB.md`). This file is kept as the original spec. Where the build differs, the as-built state is:
> - The csproj also references `UnityEngine.IMGUIModule` (for `GUI.color`).
> - `ILevelLabelProvider` has `ProviderName` and `TryGetLabel(map, out label, out int sortValue)`; `SortKey` returns `long`.
> - Labels: `map<id> - <label>`. MultiFloors level 0 gets no label. A map without a mod label is "surface" unless it is a pocket map. Named elevators: "<name> on <level label>".
> - "No counterpart on" became "Outside the boundaries of <level label>" (`DeepElevator_HelperOutsideMap`).
> - Added a "Go to partner" gizmo and `LogId` (thing id + name) in logs.
> - Untested: pocket maps (item 6) and the broken-provider fallback (item 8). Item 11 (old saves) is dropped: there's no backwards compatibility before release.

Read `plan.md` (rev 5) first, especially §1 "Placement and pairing", the label bullets, and §2 (DeepRim, MultiFloors, world tile facts). Also skim `results_stepA.md` §3.2 (performance rule) and §5. This file is self-contained for Step B. **Don't implement anything beyond it:** no storage, no buffer, no transport (Step C), no Harmony, no mod settings, no probe removal.

**Goal of Step B:** the elevator no longer depends on DeepRim. It pairs with elevators on any map of the same **level group**, using the **center-relative** position. It shows mod-aware level labels (DeepRim / MultiFloors, with tags), can be renamed, and has a placement helper that shows where connections are possible. Everything from Step A (auto-pair, Connect menu, lifecycle unlinking, load check, save/load) keeps working.

**Done when:** `dotnet build` (Debug) gives 0 errors / 0 warnings; `DeepElevator\1.6\Assemblies\` contains only `DeepElevator.dll` (+ `.pdb`); no source file references `DeepRim` or `MultiFloors` types at compile time; the human test checklist (§8) can be run. The human does the in-game testing; the agent only builds. **No git commits** (the human commits).

## 0. Environment / conventions

- Repo root `C:\Code\Rimworld\DeepElevator`. SDK-style `DeepElevator.csproj` (`net472`, LangVersion latest) compiles `DeepElevator\Source\**\*.cs`. Build: `dotnet build DeepElevator.csproj` (Debug).
- Decompiler: `dotnet ilspycmd -t <Type.FullName> <dll>`, **run from the repo root** (local tool). Game: `C:\Games\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`. MultiFloors: `Examples\MultiFloors\1.6\Assemblies\MultiFloors.dll`. DeepRim source: `Examples\DeepRim\Source\DeepRim\`.
- Follow the existing code style: file-scoped namespace `DeepElevator`, XML doc comments on public members, section comments (`// ---- saving` etc.), all logging via `ElevatorUtil.Log` / `LogWarn` (prefix `[DeepElevator]`, unconditional for now).
- **Performance rule (results_stepA §3.2):** anything vanilla or other mods may call very often must be O(1)-ish and avoid per-call allocations, or be cached. That covers place-worker methods, and gizmo/inspect builders while selected. The candidate search is O(number of maps) and acceptable in gizmos/inspect (few maps). It must never run per cell.

## 1. Facts this step relies on (verified)

| Fact | Source |
| --- | --- |
| `Map.Tile => info.Tile => parent?.Tile ?? PlanetTile.Invalid`; `WorldObject.tile` defaults to `PlanetTile.Invalid`; `PlanetTile.Valid => tileId >= 0`; `PlanetTile ==` compares by value | decompiled `Verse.Map`, `Verse.MapInfo`, `RimWorld.Planet.WorldObject`, `PlanetTile` |
| Vanilla pocket maps: `PocketMapParent` with `sourceMap` set, **Tile not set** (Invalid) | `Verse.PocketMapUtility.GeneratePocketMap`, `RimWorld.Planet.PocketMapParent` (public field `Map sourceMap`) |
| `Map.Center = new IntVec3(Size.x / 2, 0, Size.z / 2)` | `Verse.Map` |
| DeepRim layer: `map.Parent` is `DeepRim.UndergroundMapParent` (public field `int depth`), `Tile` = shaft's tile. Names: `DeepRim.UndergroundManager` (a MapComponent, present on every map, but the shaft map's instance holds the data): public field `Dictionary<int, UndergroundMapParent> layersState`, public method `string GetLayerName(int key)` (returns "" when unnamed). Displayed depth = `depth * 10` m. | `Examples\DeepRim\Source\DeepRim\UndergroundMapParent.cs`, `UndergroundManager.cs` |
| MultiFloors level: `PocketMapParent` with `sourceMap` = map the stairs were on, **`Tile = sourceMap.Tile`**, size = that map's size. The stack lives on its ground map's `MultiFloors.MF_LevelMapComp` (MapComponent on every map): `public Dictionary<int, Map> MapByLevel` (property; contains level 0 = ground once a second level exists, `AddGroundMap` → `MapByLevel.Add(0, groundMap)`). Label: `public static string MultiFloors.LevelUtility.CurrentLevelString(int currentLevel, bool addParenthesis = false)` (English: ground "G", upper "L{n}", basement symbol from its keyed file). A DeepRim layer has MultiFloors `Level() == 0`, so it is the ground of its own stack. | decompiled `MultiFloors.dll` (`LevelMapGenerator`, `MF_LevelMapComp`, `LevelUtility`) |
| `Verse.IRenameable { string RenamableLabel { get; set; } string BaseLabel { get; } string InspectLabel { get; } }`; `Verse.Dialog_Rename<T> : Window where T : class, IRenameable` is abstract with `protected Dialog_Rename(T renaming)`, sets `renaming.RenamableLabel = curName` on accept, then calls `protected virtual void OnRenamed(string name)`; `protected virtual int MaxNameLength => 28` | decompiled `Verse.IRenameable`, `Verse.Dialog_Rename`1` |
| While placing, `Designator_Place` calls `placeWorker.DrawGhost(def, UI.MouseCell(), rot, color, thing)` (world pass, through `GhostDrawer.DrawGhostThing(drawPlaceWorkers: true)`) and `placeWorker.DrawPlaceMouseAttachments(float curX, ref float curY, BuildableDef def, IntVec3 center, Rot4 rot)` (GUI pass), once per frame each | decompiled `RimWorld.Designator_Place` |
| Drawing helpers: `GenDraw.DrawFieldEdges(List<IntVec3> cells, Color color, float? altOffset = null, HashSet<IntVec3> ignoreBorderCells = null, int renderQueue = 2900)`, `GenDraw.DrawLineBetween(Vector3 A, Vector3 B, SimpleColor color, float lineWidth = 0.2f)` | decompiled `Verse.GenDraw` |
| Reflection lookup without Harmony: `GenTypes.GetTypeInAnyAssembly(string fullName)`; mod active check: `ModsConfig.IsActive(string packageId)` (DeepRim `Mlie.DeepRim`, MultiFloors `telardo.MultiFloors`) | vanilla API |

## 2. Project / metadata changes

### 2.1 `DeepElevator.csproj`
- Remove the `DeepRim` `<Reference>`.
- Remove `<ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch>` and its comment (it only existed for DeepRim.dll).
- Nothing else changes.

### 2.2 `DeepElevator\About\About.xml`
- Remove the whole `<modDependencies>` block.
- `<loadAfter>`: `<li>Mlie.DeepRim</li>` and `<li>telardo.MultiFloors</li>` (harmless when absent; keeps their defs and types loaded before our startup code).
- Replace `<description>` with (keep it plain text; the human may edit it):
  > Elevator building that transports items between levels on the same world location: the surface, DeepRim layers, MultiFloors levels, and pocket maps together with the map they were opened from. Build two elevators at the same position measured from the center of their maps to link them. If maps have different sizes (DeepRim's layer size setting), the smaller map is placed over the center of the larger one, cell for cell. Elevators outside the smaller map's area can't connect to it, and an elevator's position relative to the DeepRim shaft may differ between levels. To avoid both, set DeepRim's layer size to the same size as your surface map. Items dropped onto an elevator are sent to its linked elevator; received items go into neighbouring storage if possible, or onto the ground. Works with VFE conveyor belts: conveyors can feed elevators, and output can go into hoppers.

### 2.3 `DeepElevator\Defs\ThingDefs\DeepElevator_Elevator.xml`
- Already `Graphic_Single`, `rotatable false`, no `uiIconPath`. Keep it.
- Update `<description>`: "Transports items to a linked elevator on another level at the same world location (surface, DeepRim layers, MultiFloors levels). Build a second elevator at the same position, measured from the map center, on another level to link them."
- Keep `<placeWorkers><li>DeepElevator.PlaceWorker_Elevator</li></placeWorkers>`; it is now only the placement helper.

### 2.4 `DeepElevator\Languages\English\Keyed\DeepElevator.xml`
Remove: `DeepElevator_NoAnchor`, `DeepElevator_MultipleAnchors`, `DeepElevator_AdjacentToAnchor`, `DeepElevator_Surface`, `DeepElevator_Layer`, `DeepElevator_LayerNamed`. Keep the others; replace "layer(s)" by "level(s)" in their texts. Add:
```xml
<DeepElevator_DeepRimDepth>{0}m</DeepElevator_DeepRimDepth>
<DeepElevator_DeepRimDepthNamed>{0}m {1}</DeepElevator_DeepRimDepthNamed>
<DeepElevator_TagDeepRim>DeepRim</DeepElevator_TagDeepRim>
<DeepElevator_TagMultiFloors>MultiFloors</DeepElevator_TagMultiFloors>
<DeepElevator_LabelWithTag>{0} ({1})</DeepElevator_LabelWithTag>
<DeepElevator_LabelSeparator>, </DeepElevator_LabelSeparator>
<DeepElevator_VanillaLabel>{0} (map {1})</DeepElevator_VanillaLabel>
<DeepElevator_ElevatorOnLevel>{0} – {1}</DeepElevator_ElevatorOnLevel>
<DeepElevator_Rename>Rename</DeepElevator_Rename>
<DeepElevator_RenameDesc>Give this elevator a name. It is shown in the connect menu and messages of the elevators on other levels.</DeepElevator_RenameDesc>
<DeepElevator_HelperConnectsTo>Connects to: {0}</DeepElevator_HelperConnectsTo>
<DeepElevator_HelperNoCounterpart>No counterpart on: {0}</DeepElevator_HelperNoCounterpart>
```
`DeepElevator_ElevatorOnLevel` = "<elevator name> – <level label>" (used only when the elevator has a custom name).

## 3. Level groups and center-relative key — `ElevatorUtil.cs`

Remove `using DeepRim`, `FindAnchor`, `FindAnchorUnmeasured`, `CountPlayerBuildings`, `MetresPerDepthLevel`, `OtherLayerMaps`, `LayerLabel` (moved to `LevelLabels`, §4). Keep `Log`, `LogWarn`, `MapDesc` (now uses `LevelLabels.LevelLabel`). New / changed members:

```csharp
private const int MaxSourceChainLength = 16; // cycle / runaway guard

/// The map whose tile defines the group: follows PocketMapParent.sourceMap while the tile is invalid.
public static Map GroupRoot(Map map)
{
    Map current = map;
    for (int step = 0; current != null && step < MaxSourceChainLength; step++)
    {
        if (current.Tile.Valid) return current;
        if (current.Parent is PocketMapParent pocketParent && pocketParent.sourceMap != null && pocketParent.sourceMap != current)
        {
            current = pocketParent.sourceMap;
            continue;
        }
        return current;          // no valid tile, no source: a group of its own
    }
    return current;              // chain too long: treat the last reached map as root (log once, see below)
}

/// Same level group: both roots have a valid tile and the tiles are equal, or it is the same root map.
public static bool SameGroup(Map a, Map b)
{
    if (a == null || b == null) return false;
    Map rootA = GroupRoot(a), rootB = GroupRoot(b);
    if (rootA == rootB) return true;
    return rootA.Tile.Valid && rootB.Tile.Valid && rootA.Tile == rootB.Tile;
}

/// Other maps of the same level group (not including map itself).
public static IEnumerable<Map> OtherLevelMaps(Map map)
// foreach otherMap in Find.Maps: otherMap != map && SameGroup(map, otherMap) → yield

/// Center-relative pairing key.
public static IntVec3 CenterOffset(IntVec3 cell, Map map) => cell - map.Center;

/// The cell on otherMap that corresponds to cell on map (1:1 overlay at the centers). False if out of bounds.
public static bool TryGetMatchingCell(IntVec3 cell, Map map, Map otherMap, out IntVec3 matchingCell)
{
    matchingCell = otherMap.Center + CenterOffset(cell, map);
    return matchingCell.InBounds(otherMap);
}
```
- If `GroupRoot` hits the chain limit, `LogWarn` once per session (static bool) with the map id. It should never happen.
- `UnconnectedCandidates(Building_Elevator self)`: same contract as in Step A. For each `otherMap` in `OtherLevelMaps(self.Map)`: if `TryGetMatchingCell(self.Position, self.Map, otherMap, out cell)`, get `cell.GetFirstThing<Building_Elevator>(otherMap)`; add it if `!= self`, `Spawned`, `Partner == null`. Keep the result order stable: sort by `LevelLabels.SortKey(map)` (§4) so the Connect menu is ordered.
- `TryGetKey` is removed. Update callers: `LogLinkState` uses `CenterOffset`.

## 4. Level labels — new `LevelLabels.cs` (+ providers)

```csharp
/// Supplies a label part for maps a mod knows about. Implementations resolve their reflection members once.
public interface ILevelLabelProvider
{
    /// Short label part without tag, e.g. "70m Mines" or "B1". False if this provider doesn't know the map.
    bool TryGetLabel(Map map, out string label);
    /// Tag text shown in parentheses, e.g. "DeepRim".
    string Tag { get; }
}

[StaticConstructorOnStartup]
public static class LevelLabels
{
    private static readonly List<ILevelLabelProvider> providers = new();
    static LevelLabels() { /* register DeepRimLabelProvider, then MultiFloorsLabelProvider, each only if TryCreate succeeds */ }

    /// "70m Mines (DeepRim)", "B1 (MultiFloors)", "70m (DeepRim), B1 (MultiFloors)", or vanilla fallback "pocket map (map 5)".
    public static string LevelLabel(Map map);

    /// Label of a specific elevator: "<custom name> – <level label>" if named, else the level label.
    public static string ElevatorLabel(Building_Elevator elevator);

    /// Stable ordering for menus: DeepRim depth, then MultiFloors level, then map uniqueID.
    public static int SortKey(Map map);   // or a comparer; keep it simple: (int)uniqueID fallback
}
```
- `LevelLabel(map)`:
  - `null` → "?".
  - Otherwise, for each provider whose `TryGetLabel` succeeds, collect `"DeepElevator_LabelWithTag".Translate(label, provider.Tag)` and join the parts with `DeepElevator_LabelSeparator`.
  - If none succeeded, use the vanilla fallback `"DeepElevator_VanillaLabel".Translate(map.Parent?.LabelCap ?? "?", map.uniqueID)`. For the colony this shows the settlement name.
  - **Computed on demand**, never cached or refreshed on a timer (decision). Only the reflection members are cached.
- Every provider call is wrapped in `try/catch`. On an exception, `LogWarn` once per provider and disable that provider for the session, then use the fallback.
- `MapDesc(map)` (logs) = `$"map#{map.uniqueID} ({LevelLabel(map)})"`, still exception-safe.

### 4.1 `DeepRimLabelProvider` (reflection; no `using DeepRim`)
- `TryCreate`:
  - Requires `ModsConfig.IsActive("Mlie.DeepRim")`.
  - Types via `GenTypes.GetTypeInAnyAssembly`: `"DeepRim.UndergroundMapParent"`, `"DeepRim.UndergroundManager"`.
  - Members: `FieldInfo depth` (public instance int) on the parent; on the manager, `FieldInfo layersState` (public instance, `IDictionary`) and `MethodInfo GetLayerName(int)`.
  - If any is missing, `LogWarn` once ("DeepRim detected but its layer data could not be read; using generic labels") and return null.
- `TryGetLabel(map)`:
  - `map.Parent` must be of the parent type (`parentType.IsInstanceOfType(map.Parent)`), else false.
  - `depth = (int)depthField.GetValue(map.Parent)`.
  - Name: find the map holding this layer. Loop over `Find.Maps` where `ElevatorUtil.SameGroup(map, m)` and `m.Parent` is not the parent type. Get the manager component (`m.components` entry with `managerType.IsInstanceOfType`; loop the list, no LINQ). Check that its `layersState` dictionary contains `map.Parent` as a value (iterate `IDictionary.Values`). Then `name = (string)getLayerName.Invoke(manager, new object[] { depth })`.
  - Label: `name.NullOrEmpty() ? "DeepElevator_DeepRimDepth".Translate(depth * 10) : "DeepElevator_DeepRimDepthNamed".Translate(depth * 10, name)`.
  - Tag: `"DeepElevator_TagDeepRim".Translate()`.
  - Sort key part: `depth`.
- The surface map itself gets no DeepRim label: it isn't a layer. It falls through to MultiFloors (if it has levels) or the vanilla fallback (settlement name).

### 4.2 `MultiFloorsLabelProvider` (reflection; no `using MultiFloors`)
- `TryCreate`:
  - Requires `ModsConfig.IsActive("telardo.MultiFloors")`.
  - Types: `"MultiFloors.MF_LevelMapComp"`, `"MultiFloors.LevelUtility"`.
  - Members: `PropertyInfo MapByLevel` (public instance, `Dictionary<int, Map>`; assign the getter result as `Dictionary<int, Map>`) and `MethodInfo CurrentLevelString(int, bool)` (public static; match by name + parameter types).
  - Missing → `LogWarn` once, return null.
- `TryGetLabel(map)`:
  - Loop over `Find.Maps` in the same group as `map`, **including `map` itself**. Get each map's `MF_LevelMapComp` component and its `MapByLevel`. If an entry's value is `map`, take its key as the level. Search the whole group, not just `map`: the stack lives on the ground map's component.
  - The dictionary only has entries once a stack has ≥ 2 levels, so a map without MultiFloors levels gets no MultiFloors label. That's intended.
  - `label = (string)currentLevelString.Invoke(null, new object[] { level, false })`; tag `"DeepElevator_TagMultiFloors".Translate()`; sort key part: level.
- Both mods on one map (MultiFloors level on a DeepRim layer, or the DeepRim layer that is that stack's ground): both providers succeed, giving e.g. "70m (DeepRim), G (MultiFloors)".

### 4.3 Where labels are used
Replace every `ElevatorUtil.LayerLabel(...)` call:
- Connect menu options: `LevelLabels.ElevatorLabel(candidate)`.
- `DeepElevator_MsgLinked`: `ElevatorLabel(other)`.
- `DeepElevator_MsgUnlinked`: `LevelLabel(lostMap)`. On map removal the lost elevator is gone, and `LevelLabel` must not throw for a removed map: providers may fail, so they're wrapped, and the fallback reads `map.Parent?.LabelCap`.
- Inspect "Connected to {0}": `ElevatorLabel(partner)`.
- Placement helper (§6): `ElevatorLabel` / `LevelLabel`.

## 5. Renameable elevator — `Building_Elevator.cs`

- `public class Building_Elevator : Building, IRenameable`.
- Field `private string customName;` saved with `Scribe_Values.Look(ref customName, "customName");`.
- `IRenameable`:
  - `RenamableLabel { get => customName ?? BaseLabel; set => customName = value.NullOrEmpty() || value == BaseLabel ? null : value; }`
  - `BaseLabel => def.label.CapitalizeFirst()`
  - `InspectLabel => LabelCap`
- `public override string Label => customName ?? base.Label;` This makes the name show in the selection/inspect title and in vanilla messages. Check that `LabelNoCount` etc. don't need overriding; if the title doesn't change, override `LabelNoCount` instead.
- `public bool HasCustomName => customName != null;`
- Gizmo "Rename" (always available): `icon = TexButton.Rename`, `action = () => Find.WindowStack.Add(new Dialog_RenameElevator(this))`.
- New `Dialog_RenameElevator.cs`: `public class Dialog_RenameElevator : Dialog_Rename<Building_Elevator> { public Dialog_RenameElevator(Building_Elevator elevator) : base(elevator) { } }`. Optionally override `OnRenamed` to `ElevatorUtil.Log` the rename.
- `LevelLabels.ElevatorLabel(elevator)` = `HasCustomName ? "DeepElevator_ElevatorOnLevel".Translate(customName, LevelLabel(elevator.Map)) : LevelLabel(elevator.Map)`.

Other changes in `Building_Elevator`:
- Class doc comment: replace "DeepRim layer" by "level".
- `LogLinkState`: `key=` → `offset=` (`ElevatorUtil.CenterOffset(Position, Map)`), plus `root=map#<GroupRoot id>`. Add one line per other level map: `map#id label matchingCell=<cell or "out of bounds"> elevatorThere=<id or none>`.
- `TryAutoPair` log: also list each candidate (`id on MapDesc`).
- Everything else (lifecycle, load check, validation, Link/Unlink logic) stays unchanged.

## 6. Placement helper — `PlaceWorker_Elevator.cs`

Remove the `AllowsPlacing` override, `EvaluatePlacement`, the caller-report constants/fields, and the commented-out adjacency code and `DrawGhost`. Keep the class.
New content, all static/cached; no work unless an elevator is being placed:

```csharp
private const int RebuildIntervalFrames = 60;

private static Map cachedMap;
private static int cachedFrame = -RebuildIntervalFrames;
private static readonly List<IntVec3> targetCells = new();                             // for DrawFieldEdges
private static readonly Dictionary<IntVec3, List<Building_Elevator>> targetsByCell = new(); // cursor text
private static readonly List<(Map otherMap, CellRect overlap)> partialOverlaps = new();   // other levels of a different size
```

`EnsureCache(Map map)`: if `map == cachedMap && Time.frameCount - cachedFrame < RebuildIntervalFrames`, return. Otherwise clear and rebuild (reuse the containers; the `targetsByCell` lists can be re-allocated on rebuild, since that is at most once per 60 frames):
- For each `otherMap` in `ElevatorUtil.OtherLevelMaps(map)`:
  - **Overlap rect:** the other map's cells translated into this map. `offset = map.Center - otherMap.Center`; `rect = new CellRect(offset.x, offset.z, otherMap.Size.x, otherMap.Size.z).ClipInsideMap(map)`. If `rect` doesn't cover the whole map (`rect.Area < map.Size.x * map.Size.z`), add `(otherMap, rect)` to `partialOverlaps`.
  - **Targets:** for each thing in `otherMap.listerThings.ThingsOfDef(DeepElevatorDefOf.DeepElevator_Elevator)` that is a spawned `Building_Elevator` with `Partner == null`, compute the cell here: `cell = map.Center + ElevatorUtil.CenterOffset(e.Position, otherMap)`. If `cell.InBounds(map)`, add it to `targetsByCell[cell]` (and to `targetCells` once).
- `cachedMap = map; cachedFrame = Time.frameCount;`

```csharp
public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
{
    using (ElevatorProfiler.Measure("PlaceWorker.DrawGhost"))
    {
        Map map = Find.CurrentMap;
        if (map == null) return;
        EnsureCache(map);
        if (targetCells.Count > 0) GenDraw.DrawFieldEdges(targetCells, Color.green);
        foreach (var (_, overlap) in partialOverlaps) DrawRectOutline(overlap, SimpleColor.Yellow);
    }
}

public override void DrawPlaceMouseAttachments(float curX, ref float curY, BuildableDef def, IntVec3 center, Rot4 rot)
{
    // center = UI.MouseCell()
    Map map = Find.CurrentMap;
    if (map == null) return;
    EnsureCache(map);
    if (targetsByCell.TryGetValue(center, out var elevators))
        foreach (var e in elevators) DrawLine(curX, ref curY, "DeepElevator_HelperConnectsTo".Translate(LevelLabels.ElevatorLabel(e)), Color.green);
    foreach (var (otherMap, overlap) in partialOverlaps)
        if (!overlap.Contains(center)) DrawLine(curX, ref curY, "DeepElevator_HelperNoCounterpart".Translate(LevelLabels.LevelLabel(otherMap)), Color.yellow);
}
```
- `DrawLine`: `Text.Font = GameFont.Small; GUI.color = color; Widgets.Label(new Rect(curX, curY, 999f, 29f), text); GUI.color = Color.white; curY += 29f;`. This is the vanilla place-worker pattern; check one vanilla `DrawPlaceMouseAttachments` override with ilspycmd and match its spacing.
- `DrawRectOutline(CellRect r, SimpleColor c)`: four `GenDraw.DrawLineBetween` calls between the rect's outer corners: `new Vector3(r.minX, alt, r.minZ)`, `(r.maxX + 1, alt, r.minZ)`, `(r.maxX + 1, alt, r.maxZ + 1)`, `(r.minX, alt, r.maxZ + 1)`, with `alt = AltitudeLayer.MetaOverlays.AltitudeFor()`. **Don't** use `DrawFieldEdges` for big rects (it walks every cell).
- Labels in the cursor text are computed per frame only for the hovered cell's entries (a few strings). Acceptable, but if the profiler shows cost, cache the strings in `EnsureCache`.
- Also measure `DrawPlaceMouseAttachments` with `ElevatorProfiler.Measure("PlaceWorker.DrawPlaceMouseAttachments")`.
- Colors are placeholders; the human may adjust them.

## 7. Probes (keep; user decision)
- Keep `ElevatorProfiler`, `ElevatorDiagnostics`, `MapComponent_ElevatorProbe` (hitch detector + `MapRemoved` log).
- `PlaceWorker.AllowsPlacing` measurement disappears with the override. The hitch detector still reports `DrawGhost` / `DrawPlaceMouseAttachments` timings.
- New logs: `LogLinkState` lines (§5); `TryAutoPair` candidate list. Startup logs from each label provider: "DeepRim labels: enabled/disabled (reason)", "MultiFloors labels: enabled/disabled (reason)".

## 8. Human test checklist (Dev mode on, watch the log)
Use "DEV: log link state" to see offset, group root and matching cells.
1. **Vanilla only** (no DeepRim, no MultiFloors): the mod loads without dependency errors; an elevator can be built; it shows "Not connected"; there are no provider warnings (only "disabled: mod not active" lines).
2. **DeepRim, same layer size as the surface:**
   - An elevator on the surface and one on a layer at the **same absolute cell** auto-link.
   - Label shows e.g. "70m (DeepRim)"; after renaming the layer in DeepRim: "70m Mines (DeepRim)".
   - The surface side shows the settlement name with "(map N)".
3. **DeepRim, smaller layer** (set the DeepRim map size below the surface size before drilling):
   - The placement helper outlines the overlap area on the surface.
   - Outside it the cursor text says "No counterpart on: …".
   - Elevators at center offset (0,0) on both maps link.
   - An elevator outside the overlap never gets a candidate.
4. **MultiFloors only:** build an upper level and a basement; elevators at the same cell link; labels "G (MultiFloors)", "L2 (MultiFloors)", basement symbol.
5. **Both:**
   - A MultiFloors level opened from a DeepRim layer: its label shows both tags. It links to the DeepRim layer, to the surface, and to MultiFloors levels of the surface stack, wherever the matching cell is in bounds.
   - The Connect menu lists ambiguous candidates in a sensible order with distinguishable labels.
6. **Pocket maps:**
   - An Anomaly undercave (or another vanilla pocket map) links to its source map at the same center offset.
   - Two different pocket maps from different source maps never see each other.
   - A DeepRim layer or MultiFloors level opened from a pocket map joins that group.
   - Check that `root=` in the state log is the source map.
7. **Rename:**
   - The Rename gizmo works, and the name shows in the inspect title, in the partner's "Connected to …", in the Connect menu, and in link/unlink messages.
   - Survives save/load.
   - Clearing the name restores the default label.
8. **Label fallback:** with a mod's members missing (simulate by disabling a provider in code only if easy, otherwise skip), labels fall back to "… (map N)" with one warning.
9. **Placement helper:**
   - Unconnected elevators on other levels show as highlighted cells; the cursor over one shows "Connects to: …".
   - No hitch lines while dragging with Designator Shapes active.
   - The highlight updates within about a second after building or linking elsewhere.
10. **Regression (Step A):**
    - Deconstruct each side.
    - DeepRim abandon a layer; Dev remove map (now also a MultiFloors level, and a pocket map).
    - Save/load with linked pairs and an orphan.
    - The load-time link check still repairs a one-sided link.
    - No `Validation:` warning in normal play.
11. **Old saves from Step A:** existing links load and stay linked even if their positions no longer match under the new key. Links are references and are never re-checked against positions; that's the intended behaviour, so report if not.

Report: results per item, log excerpts for failures, any hitch lines, red errors.

## 9. Out of scope
Storage / buffer / transport / delivery / Unload / Contents tab (Step C); VFE checks (Step D); manifest; mod settings; Harmony; probe removal; custom icons (Connect still uses `UI/Commands/LoadTransporter`; Rename uses `TexButton.Rename`).
