# DeepElevator – Step A implementation spec (handoff)

Read `plan.md` (rev 4) first, especially §1 (decisions) and §2 (verified facts). This file is self-contained for Step A. Don't implement anything beyond it: no storage, no buffer, no transport, no Harmony, no mod settings.

**Goal of Step A:** the elevator can be built; placement rules work; two elevators on different layers of the same tile pair automatically (or via the "Connect to…" gizmo when ambiguous). Every lifecycle path that can break a link is detected and **logged**, so the human tester can see which callbacks fire. Links survive save/load.

**Done when:** the solution builds with 0 errors in VS 2022 / MSBuild, `DeepElevator\1.6\Assemblies\DeepElevator.dll` is produced with no game/DeepRim DLLs copied next to it, and the human test checklist (end of file) can be run. The human does the in-game testing; the agent only builds.

## 0. Environment

- Repo root: `C:\Code\Rimworld\DeepElevator`. The VS project (`DeepElevator.csproj`, `DeepElevator.slnx`, `Properties\AssemblyInfo.cs`) stays in the root. The mod folder is `DeepElevator\`. Source goes into `DeepElevator\Source\`.
- Game assemblies: `C:\Games\RimWorld\RimWorldWin64_Data\Managed\` (already referenced via relative HintPath).
- DeepRim assembly: `C:\Games\RimWorld\Mods\2164179860\1.6\Assemblies\DeepRim.dll`. DeepRim source for reading: `Examples\DeepRim\Source\DeepRim\` (git-ignored, not shipped).
- Decompiler: run `dotnet ilspycmd -t <Type.FullName> "C:\Games\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll"` **from the repo root** (it's a local dotnet tool, so it fails from other directories).
- Conventions: namespace `DeepElevator`; one class per file; file-scoped namespaces are OK once LangVersion is raised (below). Logging always goes through `ElevatorUtil.Log*` (prefix `[DeepElevator]`).

## 1. Verified behaviour this step relies on

| Fact | Source |
| --- | --- |
| On map removal, `Destroy`/`DeSpawn` are **not** called. `Thing.Notify_MyMapRemoved()` (public virtual) is called for every thing on the map; afterwards `thing.Destroyed == true`, `Spawned == false` | `Verse.MapDeiniter.NotifyEverythingWhichUsesMapReference`, `Verse.Thing.Notify_MyMapRemoved` |
| After the thing notifications, the map is removed from `Find.Maps`, then `MapComponent.MapRemoved()` (virtual) runs | `Verse.Game.DeinitAndRemoveMap` |
| `Destroy()` calls `DeSpawn(mode)` if spawned, then `Discard()` if `def.DiscardOnDestroyed` | `Verse.Thing.Destroy` |
| On load, cross-map references resolve before any map spawns; maps then spawn one by one. A partner on another map may be unspawned during our `SpawnSetup(respawningAfterLoad: true)` | `Verse.Game.LoadGame` |
| `Scribe_References` saves null for destroyed things | `Verse.Scribe_References.CheckSaveReferenceToDestroyedThing` |
| The ticker type selects the call: `tickerType Rare` → only `TickRare()` (every 250 ticks); `tickerType Normal` → only `Tick()` (every tick), and `TickRare()` is **not** called | `Verse.Thing.DoTick` |
| Overridable signatures (1.6): `public override void SpawnSetup(Map map, bool respawningAfterLoad)`, `public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)`, `public override void Destroy(DestroyMode mode = DestroyMode.Vanish)`, `public override void Discard(bool silentlyRemoveReferences = false)`, `public override void Notify_MyMapRemoved()`, `protected override void Tick()`, `public override void TickRare()`, `public override void ExposeData()`, `public override string GetInspectString()`, `public override IEnumerable<Gizmo> GetGizmos()` | decompiled `Verse.Thing` / `ThingWithComps` / `Building` |
| `PlaceWorker.AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)` returns `AcceptanceReport` | `Verse.PlaceWorker` |
| DeepRim: surface anchor = building with def `DeepRim.ShaftThingDefOf.miningshaft`; deep anchor = `DeepRim.Building_SpawnedLift` (public `int depth`); deep map's `map.Parent` is `DeepRim.UndergroundMapParent` (public `int depth`); `DeepRim.UndergroundManager` is a MapComponent on the **surface** map with `public string GetLayerName(int key)`. DeepRim shows depth as `depth * 10` m. | `Examples\DeepRim\Source\DeepRim\*.cs` |

## 2. Project changes

### 2.1 `DeepElevator.csproj` (root)
- OutputPath is already `DeepElevator\1.6\Assemblies\` for both configurations; leave it.
- Add to the first `PropertyGroup`: `<LangVersion>latest</LangVersion>`. Avoid features that need runtime/BCL support missing in .NET Framework 4.7.2 (records/`init`, default interface methods, `Index`/`Range`). Pattern matching, `is not`, switch expressions and `??=` are fine.
- Set `<Private>False</Private>` on the existing `Assembly-CSharp` and `UnityEngine.CoreModule` references.
- Add a reference:
  ```xml
  <Reference Include="DeepRim">
    <HintPath>C:\Games\RimWorld\Mods\2164179860\1.6\Assemblies\DeepRim.dll</HintPath>
    <Private>False</Private>
  </Reference>
  ```
- Compile items (explicit, old-style csproj):
  ```xml
  <Compile Include="Properties\AssemblyInfo.cs" />
  <Compile Include="DeepElevator\Source\Building_Elevator.cs" />
  <Compile Include="DeepElevator\Source\ElevatorUtil.cs" />
  <Compile Include="DeepElevator\Source\PlaceWorker_Elevator.cs" />
  <Compile Include="DeepElevator\Source\MapComponent_ElevatorProbe.cs" />
  <Compile Include="DeepElevator\Source\ElevatorTextures.cs" />
  <Compile Include="DeepElevator\Source\DeepElevatorDefOf.cs" />
  ```
- Optional but recommended: `<None Include="DeepElevator\**\*.xml" />` so the Defs are visible in VS. Don't make them build items.
- If `System.Net.Http` / `System.Data*` refs cause trouble, remove them (they aren't needed).

### 2.2 `DeepElevator\About\About.xml`
Keep name, packageId (`FT.deepelevator`), supportedVersions and description; leave `author` to the user. Add:
```xml
<modDependencies>
  <li>
    <packageId>Mlie.DeepRim</packageId>
    <displayName>DeepRim (Continued)</displayName>
    <steamWorkshopUrl>steam://url/CommunityFilePage/2164179860</steamWorkshopUrl>
  </li>
</modDependencies>
<loadAfter>
  <li>Mlie.DeepRim</li>
</loadAfter>
```
Append to the description: "Build only one DeepRim mining shaft per map." Remove the empty `modIconPath` element unless an icon exists.

### 2.3 `DeepElevator\loadFolders.xml`
Already correct (`/`, `1.6`). No change.

## 3. Defs

### 3.1 `DeepElevator\Defs\ThingDefs\DeepElevator_Elevator.xml`
```xml
<?xml version="1.0" encoding="utf-8"?>
<Defs>
  <ThingDef ParentName="BuildingBase">
    <defName>DeepElevator_Elevator</defName>
    <label>deep elevator</label>
    <description>Transports items to a linked elevator on another DeepRim layer. Build a second elevator at the same position relative to the mining shaft / lift on another layer to link them.</description>
    <thingClass>DeepElevator.Building_Elevator</thingClass>
    <graphicData>
      <texPath>Things/Buildings/DeepElevator/DeepElevator</texPath>
      <graphicClass>Graphic_Multi</graphicClass>
      <drawSize>(1,1)</drawSize>
    </graphicData>
    <uiIconPath>Things/Buildings/DeepElevator/DeepElevator_MenuIcon</uiIconPath>
    <size>(1,1)</size>
    <rotatable>true</rotatable>
    <altitudeLayer>Building</altitudeLayer>
    <passability>PassThroughOnly</passability>
    <fillPercent>0.4</fillPercent>
    <pathCost>42</pathCost>
    <tickerType>Rare</tickerType>
    <canOverlapZones>false</canOverlapZones>
    <statBases>
      <MaxHitPoints>200</MaxHitPoints>
      <WorkToBuild>1500</WorkToBuild>
      <Mass>30</Mass>
      <Flammability>0</Flammability>
    </statBases>
    <costList>
      <Steel>75</Steel>
      <ComponentIndustrial>2</ComponentIndustrial>
    </costList>
    <designationCategory>Misc</designationCategory>
    <placeWorkers>
      <li>DeepElevator.PlaceWorker_Elevator</li>
    </placeWorkers>
    <building>
      <destroySound>BuildingDestroyed_Metal_Medium</destroySound>
    </building>
    <constructEffect>ConstructMetal</constructEffect>
  </ThingDef>
</Defs>
```
Notes:
- `Graphic_Multi` loads `DeepElevator_north/_east/_south/_west.png` (they exist). `DeepElevator.png` would be the `Graphic_Single` path; it's unused in Step A.
- Not minifiable in Step A (no `minifiedDef`), so the "uninstall" path doesn't exist yet.
- No research prerequisite for now.
- `tickerType Rare`: all periodic work runs every 250 ticks. The code supports `Normal` too (§4.4 "Periodic work"), so switching is an XML-only change.

### 3.2 `DeepElevator\Languages\English\Keyed\DeepElevator.xml`
```xml
<?xml version="1.0" encoding="utf-8"?>
<LanguageData>
  <DeepElevator_NoAnchor>Needs a DeepRim mining shaft (surface) or lift (layer) on this map.</DeepElevator_NoAnchor>
  <DeepElevator_MultipleAnchors>This map has more than one DeepRim shaft or lift.</DeepElevator_MultipleAnchors>
  <DeepElevator_AdjacentToAnchor>Too close to the mining shaft / lift.</DeepElevator_AdjacentToAnchor>
  <DeepElevator_Surface>surface</DeepElevator_Surface>
  <DeepElevator_Layer>layer at {0}m</DeepElevator_Layer>
  <DeepElevator_LayerNamed>layer at {0}m ({1})</DeepElevator_LayerNamed>
  <DeepElevator_ConnectedTo>Connected to {0}</DeepElevator_ConnectedTo>
  <DeepElevator_NotConnected>Not connected</DeepElevator_NotConnected>
  <DeepElevator_CandidatesAvailable>{0} unconnected elevator(s) at this position on other layers. Use "Connect to...".</DeepElevator_CandidatesAvailable>
  <DeepElevator_Connect>Connect to...</DeepElevator_Connect>
  <DeepElevator_ConnectDesc>Link this elevator to an unconnected elevator at the same position on another layer. The link cannot be changed afterwards.</DeepElevator_ConnectDesc>
  <DeepElevator_MsgLinked>Elevator connected to {0}.</DeepElevator_MsgLinked>
  <DeepElevator_MsgUnlinked>Elevator lost its connection to {0}.</DeepElevator_MsgUnlinked>
</LanguageData>
```
Use `"Key".Translate(arg…)`, which supports `{0}`/`{1}` placeholders.

## 4. Code

All files go in `DeepElevator\Source\`. Usings as needed: `System`, `System.Collections.Generic`, `System.Linq`, `System.Text`, `RimWorld`, `Verse`, `DeepRim`.

### 4.1 `ElevatorUtil.cs` – `public static class ElevatorUtil`

```csharp
public const string LogPrefix = "[DeepElevator] ";
public static void Log(string msg)      => Verse.Log.Message(LogPrefix + msg);
public static void LogWarn(string msg)  => Verse.Log.Warning(LogPrefix + msg);
```
In Step A, log unconditionally (this is a probe build). Later steps may gate on `Prefs.DevMode`.

```csharp
public static ThingDef ElevatorDef => DefDatabase<ThingDef>.GetNamed("DeepElevator_Elevator");
```
(Or a `[DefOf]` class `DeepElevatorDefOf { public static ThingDef DeepElevator_Elevator; }` with the usual `static ctor DefOfHelper.EnsureInitializedInCtor`. Either is fine; prefer DefOf.)

```csharp
/// Shaft (surface) or lift (deep layer). Null + reason if none or more than one.
public static Building FindAnchor(Map map, out string failReason)
```
- `shafts = map.listerBuildings.AllBuildingsColonistOfDef(ShaftThingDefOf.miningshaft)`
- `lifts = map.listerBuildings.AllBuildingsColonistOfClass<Building_SpawnedLift>()`
- total = shafts.Count + lifts.Count. 0 → `failReason = "DeepElevator_NoAnchor".Translate()`, return null. >1 → `"DeepElevator_MultipleAnchors"`, return null. Otherwise return the single one, `failReason = null`.
- Note: `allBuildingsColonist` contains only player-faction buildings. DeepRim sets the lift's faction to player, so this is fine.

```csharp
public static bool TryGetKey(Thing elevator, out IntVec3 key)
// anchor = FindAnchor(elevator.Map, out _); null → key = IntVec3.Invalid, false
// key = elevator.Position - anchor.Position; true
```

```csharp
/// Other maps of the same world tile that are DeepRim layers or the surface (i.e. have exactly one anchor).
public static IEnumerable<Map> OtherLayerMaps(Map map)
// foreach m in Find.Maps: m != map && m.Tile == map.Tile && FindAnchor(m, out _) != null → yield m
```
(`Map.Tile` is a `PlanetTile` in 1.6. Compare with `==`; it has equality operators. If the compiler complains, compare `m.Tile.tileId`. Check with ilspycmd if needed.)

```csharp
/// Unconnected, spawned elevators on other layer maps of the same tile with the same key.
public static List<Building_Elevator> UnconnectedCandidates(Building_Elevator self)
```
- If `!TryGetKey(self, out key)` → empty list.
- For each `m` in `OtherLayerMaps(self.Map)`: `anchor = FindAnchor(m, out _)`, `cell = anchor.Position + key`. If `!cell.InBounds(m)` → skip. Look for a `Building_Elevator` at `cell` (`cell.GetFirstThing<Building_Elevator>(m)` or iterate `cell.GetThingList(m)`), with `e != self && e.Spawned && e.Partner == null`. Add it.
- Searching by cell is preferred over scanning all elevators: it's O(maps) and exact.

```csharp
/// "surface", "layer at 30m", "layer at 30m (Mines)"
public static string LayerLabel(Map map)
```
- `map.Parent is UndergroundMapParent ump` → depth = `ump.depth`. Name: find the surface map = `Find.Maps.FirstOrDefault(m => m.Tile == map.Tile && !(m.Parent is UndergroundMapParent))`, then `surface?.GetComponent<UndergroundManager>()?.GetLayerName(depth)`. Empty/null → `"DeepElevator_Layer".Translate(depth * 10)`, else `"DeepElevator_LayerNamed".Translate(depth * 10, name)`.
- Otherwise → `"DeepElevator_Surface".Translate()`.
- Must not throw when `map` is null or already removed: return `"?"` for null.

```csharp
/// For logs: "map#<uniqueID> (<LayerLabel>)"; safe for null/removed maps.
public static string MapDesc(Map map)
// map == null → "map#null"; wrap LayerLabel in try/catch → fall back to "map#<id>"
```

### 4.2 `PlaceWorker_Elevator.cs` – `public class PlaceWorker_Elevator : PlaceWorker`

```csharp
public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
    Thing thingToIgnore = null, Thing thing = null)
```
- `anchor = ElevatorUtil.FindAnchor(map, out var reason)`; null → `return new AcceptanceReport(reason)`.
- `if (anchor.OccupiedRect().ExpandedBy(1).Contains(loc))` → `"DeepElevator_AdjacentToAnchor".Translate()`. ExpandedBy(1) covers both the anchor's own cells and the 8-neighbourhood; DeepRim's `NearbyStorages` uses `OccupiedRect().AdjacentCells`.
- Otherwise `true`. Cell occupancy etc. is handled by vanilla.
- Optional (nice, cheap): `DrawGhost` override that highlights the anchor with `GenDraw.DrawFieldEdges(anchor.OccupiedRect().ExpandedBy(1).Cells.ToList(), Color.red)`. Skip if it causes any trouble.

### 4.3 `MapComponent_ElevatorProbe.cs`

```csharp
public class MapComponent_ElevatorProbe : MapComponent
{
    public MapComponent_ElevatorProbe(Map map) : base(map) { }

    public override void MapRemoved()
    {
        ElevatorUtil.Log($"MapComponent.MapRemoved: map#{map.uniqueID} tile={map.Tile}");
    }
}
```
Vanilla instantiates MapComponents for every map by reflection (it needs the `(Map)` constructor). No def is needed. It stores nothing, so `ExposeData` needs no override. It is removed again after testing if `Notify_MyMapRemoved` proves sufficient. Don't call `LayerLabel` here: the map is already out of `Find.Maps`, and `map.Parent` may be in flux.

### 4.4 `Building_Elevator.cs` – `public class Building_Elevator : Building`

Fields / properties:
```csharp
private Building_Elevator partner;
private Map partnerMap;
public Building_Elevator Partner => partner;
public bool IsConnected => partner != null;
```

**ExposeData**
```csharp
base.ExposeData();
Scribe_References.Look(ref partner, "partner");
Scribe_References.Look(ref partnerMap, "partnerMap");
if (Scribe.mode == LoadSaveMode.PostLoadInit && (partner == null) != (partnerMap == null))
{
    ElevatorUtil.LogWarn($"{ThingID}: inconsistent link after load (partner={partner?.ThingID ?? "null"}, partnerMap={(partnerMap == null ? "null" : "set")}); clearing.");
    partner = null; partnerMap = null;
}
```

**SpawnSetup**
```csharp
base.SpawnSetup(map, respawningAfterLoad);
ElevatorUtil.Log($"SpawnSetup: {ThingID} on {ElevatorUtil.MapDesc(map)} respawningAfterLoad={respawningAfterLoad} partner={partner?.ThingID ?? "none"}");
if (!respawningAfterLoad && partner == null) TryAutoPair();
```
No partner validation here (partner may be unspawned during load, see §1).

**TryAutoPair**
```csharp
private void TryAutoPair()
{
    var candidates = ElevatorUtil.UnconnectedCandidates(this);
    ElevatorUtil.Log($"TryAutoPair: {ThingID} found {candidates.Count} candidate(s)");
    if (candidates.Count == 1) Link(candidates[0]);
}
```

**Link / Unlink**
```csharp
public void Link(Building_Elevator other)
{
    if (other == null || other == this || partner != null || other.partner != null) {
        ElevatorUtil.LogWarn($"Link refused: {ThingID} -> {other?.ThingID ?? "null"} (self partner={partner?.ThingID ?? "none"}, other partner={other?.partner?.ThingID ?? "none"})");
        return;
    }
    partner = other; partnerMap = other.Map;
    other.partner = this; other.partnerMap = Map;
    ElevatorUtil.Log($"Linked: {ThingID} ({ElevatorUtil.MapDesc(Map)}) <-> {other.ThingID} ({ElevatorUtil.MapDesc(other.Map)})");
    Messages.Message("DeepElevator_MsgLinked".Translate(ElevatorUtil.LayerLabel(other.Map)), this, MessageTypeDefOf.PositiveEvent, historical: false);
}

/// Idempotent. Clears both sides (the other side only if it still points at us).
public void Unlink(string reason)
{
    if (partner == null) { partnerMap = null; return; }
    var p = partner; var pMap = partnerMap;
    partner = null; partnerMap = null;
    if (p.partner == this) { p.partner = null; p.partnerMap = null; }
    ElevatorUtil.Log($"Unlinked ({reason}): {ThingID} x {p.ThingID} (partner map {ElevatorUtil.MapDesc(pMap)})");
    // Tell the player about the survivor, if any: whichever of this/p is still spawned
    var survivor = Spawned ? this : (p.Spawned ? p : null);
    if (survivor != null)
        Messages.Message("DeepElevator_MsgUnlinked".Translate(ElevatorUtil.LayerLabel(survivor == this ? pMap : Map)), survivor, MessageTypeDefOf.NeutralEvent, historical: false);
}
```
Keep `ElevatorUtil.LayerLabel` null/removed-map safe (§4.1), because `pMap` may be gone.

**Lifecycle callbacks.** All of them go through one shared handler. Each override only names itself and adds details, then calls the handler **before** `base` (the base calls change state: `DeSpawn` clears the map, `Notify_MyMapRemoved` sets `mapIndexOrState = -3` so `Map` becomes null).
- `DeSpawn`/`Destroy` are needed because buildings can be deconstructed or destroyed while the map stays.
- `Notify_MyMapRemoved` is needed because map removal calls neither of those.
- `Discard` is a probe only.
- Because `Unlink` is idempotent, a normal destroy (Destroy → DeSpawn → maybe Discard) logs every callback but unlinks only once; later lines show `partner=none`.
```csharp
/// Shared handler for every callback that ends this elevator's life on its map.
private void OnLifecycleEnd(string callback, string details)
{
    ElevatorUtil.Log($"{callback}: {ThingID} on {ElevatorUtil.MapDesc(Map)} spawned={Spawned} {details} partner={partner?.ThingID ?? "none"}");
    Unlink(callback);
}

public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
{
    OnLifecycleEnd(nameof(DeSpawn), $"mode={mode}");
    base.DeSpawn(mode);
}

public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
{
    OnLifecycleEnd(nameof(Destroy), $"mode={mode}");
    base.Destroy(mode);
}

public override void Discard(bool silentlyRemoveReferences = false)
{
    OnLifecycleEnd(nameof(Discard), $"silently={silentlyRemoveReferences}");
    base.Discard(silentlyRemoveReferences);
}

public override void Notify_MyMapRemoved()
{
    OnLifecycleEnd(nameof(Notify_MyMapRemoved), "");
    base.Notify_MyMapRemoved();
}
```
Expected log for a deconstruct (vanilla `Destroy()` calls `DeSpawn()` from inside itself, so `Destroy:` comes first):
```
[DeepElevator] Destroy: Building_Elevator123 on map#2 (layer at 10m) spawned=True mode=Deconstruct partner=Building_Elevator77
[DeepElevator] Unlinked (Destroy): Building_Elevator123 x Building_Elevator77 (partner map map#0 (surface))
[DeepElevator] DeSpawn: Building_Elevator123 on map#2 (layer at 10m) spawned=True mode=Deconstruct partner=none
[DeepElevator] Discard: Building_Elevator123 on map#null spawned=False silently=False partner=none   (only if the def discards on destroy)
```
A `DeSpawn:` line with a partner still set and no `Destroy:` before it means despawn without destroy (e.g. minify, later steps).

**Periodic work (validation backstop), ticker-type independent**

All periodic logic lives in one method. Both tick overrides call it, and only the one matching `def.tickerType` is ever invoked by the game. So switching `<tickerType>` between `Rare` and `Normal` in XML needs no code change.
```csharp
public const int PeriodicInterval = GenTicks.TickRareInterval; // 250

// tickerType Rare: called every 250 ticks
public override void TickRare()
{
    base.TickRare();
    DoPeriodicWork();
}

// tickerType Normal: called every tick; throttle to the same interval
protected override void Tick()
{
    base.Tick();
    if (this.IsHashIntervalTick(PeriodicInterval)) DoPeriodicWork();
}

private void DoPeriodicWork()
{
    if (partner != null && !IsValidPartner(out var why))
    {
        ElevatorUtil.LogWarn($"Validation: {ThingID} partner {partner.ThingID} invalid ({why}); unlinking.");
        Unlink("validation: " + why);
    }
}

public bool IsValidPartner(out string why)
{
    if (partner == null) { why = "no partner"; return false; }
    if (partnerMap == null) { why = "partnerMap null"; return false; }
    if (!Find.Maps.Contains(partnerMap)) { why = "partner map removed"; return false; }
    if (partner.Destroyed) { why = "partner destroyed"; return false; }
    if (!partner.Spawned) { why = "partner not spawned"; return false; }
    if (partner.Map != partnerMap) { why = "partner on different map"; return false; }
    if (partner.partner != this) { why = "partner links elsewhere (" + (partner.partner?.ThingID ?? "none") + ")"; return false; }
    why = null; return true;
}
```
Expected: the event callbacks catch every case, so the validation warning should **never** appear in tests. If it does, that is a finding to report.

**Gizmos**
```csharp
public override IEnumerable<Gizmo> GetGizmos()
{
    foreach (var g in base.GetGizmos()) yield return g;
    if (partner == null)
    {
        var candidates = ElevatorUtil.UnconnectedCandidates(this);
        if (candidates.Count > 0)
            yield return new Command_Action {
                defaultLabel = "DeepElevator_Connect".Translate(),
                defaultDesc = "DeepElevator_ConnectDesc".Translate(),
                icon = ElevatorTextures.Connect,   // placeholder vanilla icon, see below
                action = () => Find.WindowStack.Add(new FloatMenu(
                    candidates.Select(c => new FloatMenuOption(ElevatorUtil.LayerLabel(c.Map), () => {
                        // re-check: state may have changed while the menu was open
                        if (partner == null && c.Spawned && c.Partner == null) Link(c);
                    })).ToList()))
            };
    }
    if (Prefs.DevMode)
        yield return new Command_Action {
            defaultLabel = "DEV: log link state",
            action = () => ElevatorUtil.Log($"State: {ThingID} on {ElevatorUtil.MapDesc(Map)} key={(ElevatorUtil.TryGetKey(this, out var k) ? k.ToString() : "n/a")} partner={partner?.ThingID ?? "none"} partnerMap={ElevatorUtil.MapDesc(partnerMap)} valid={(partner == null ? "-" : IsValidPartner(out var w) ? "yes" : w)} candidates={ElevatorUtil.UnconnectedCandidates(this).Count}")
        };
}
```
Icon: load textures in a `[StaticConstructorOnStartup]` static class (e.g. `ElevatorTextures`), never in the gizmo loop. Pick a vanilla path that exists in 1.6 (e.g. `"UI/Commands/LoadTransporter"`; verify it exists, or fall back to `BaseContent.BadTex`). A proper icon comes later.

`GetGizmos` runs every frame for selected things. `UnconnectedCandidates` is O(number of maps), so that's acceptable. Don't add caching in Step A.

**GetInspectString**
```csharp
var sb = new StringBuilder();
var baseStr = base.GetInspectString();
if (!baseStr.NullOrEmpty()) sb.AppendLine(baseStr);
if (partner != null) sb.AppendLine("DeepElevator_ConnectedTo".Translate(ElevatorUtil.LayerLabel(partnerMap)));
else {
    sb.AppendLine("DeepElevator_NotConnected".Translate());
    var n = ElevatorUtil.UnconnectedCandidates(this).Count;
    if (n > 0) sb.AppendLine("DeepElevator_CandidatesAvailable".Translate(n));
}
return sb.ToString().TrimEndNewlines();
```
(`n == 1` while unconnected can happen when a partner was lost and another orphan exists. Auto-pair only runs on build, by decision, so show the hint for any `n > 0`.)

## 5. Out of scope for Step A (don't implement)
Storage settings / `Building_Storage`, internal buffer, `Accepts`, transport, rate limit, delivery, Unload gizmo, Contents tab, CompTransporter/manifest, placement helper ghosts on other layers, Harmony, mod settings, custom graphics swap.

## 6. Build check (agent)
- Build Debug with MSBuild from the VS 2022 install, e.g. `"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" DeepElevator.csproj /p:Configuration=Debug`, or ask the user to build in VS.
- Expect 0 errors. `DeepElevator\1.6\Assemblies\` contains only `DeepElevator.dll` (+ `.pdb` in Debug). No `Assembly-CSharp.dll`, `DeepRim.dll`, or `UnityEngine*.dll` copies.
- Don't commit unless the user asks.

## 7. Human test checklist (in game, Dev mode on, watch the log)
Each item lists the expected log lines; record which actually appear.
1. **Placement refusals:** a map without a shaft → "Needs a DeepRim mining shaft…". A cell adjacent to the shaft → "Too close…". On a deep layer next to the lift → same.
2. **Single end:** build on the surface → `SpawnSetup … respawningAfterLoad=False`, `TryAutoPair … 0 candidate(s)`; inspect shows "Not connected".
3. **Auto-pair:** on layer 1, build at the same offset from the lift → `TryAutoPair … 1 candidate(s)`, `Linked: …`, message; both inspect strings show "Connected to …".
4. **Deconstruct each side:** `Destroy … mode=Deconstruct partner=<id>`, `Unlinked (Destroy)`, `DeSpawn … partner=none`, maybe `Discard …` (see the example in §4.4); the survivor shows "Not connected" + message. Rebuild → re-pairs.
5. **Destroy via Dev tool** (Destroy/Kill): same as 4 with other modes.
6. **Ambiguity:** make pairs surface–L1 and L2–L3 at the same key (needs 3 layers), destroy the L1 and L3 elevators → surface and L2 are both unconnected. Build at the key on L1 → `2 candidate(s)`, no auto-link, the "Connect to…" gizmo lists surface and L2; pick one → `Linked`; the other stays unconnected and shows the candidate hint (`1`).
7. **DeepRim abandon a layer** with an elevator linked to the surface → expect `Notify_MyMapRemoved: … partner=<id>` on the layer's elevator, `Unlinked (Notify_MyMapRemoved)`, then `MapComponent.MapRemoved: map#…`. No `DeSpawn`/`Destroy` lines (that confirms §1). The surface elevator shows "Not connected". **No** `Validation:` warning should appear.
8. **Dev "remove map"** of a layer (if available in Dev tools): same as 7.
9. **Destroy the mining shaft** (DeepRim abandons all layers): every layer elevator logs `Notify_MyMapRemoved`; surface elevators unlink. Note the order of log lines.
10. **Save/load** with 2 linked pairs and 1 orphan: after load, `SpawnSetup … respawningAfterLoad=True partner=<id>` for each linked elevator, inspect strings unchanged, no warnings. "DEV: log link state" shows `valid=yes`.
11. **Save after a partner was lost**, then load: no "could not resolve reference" errors, and the orphan stays unconnected.

Report: which callbacks fired per scenario, any `Validation:` warnings, any red errors.
