# DeepElevator – Step F: visuals (icons, glow, night-visible lights)

Handoff spec for the implementing agent. Background: `plan.md`, `pre_stepF.md`. RimWorld 1.6 only.
The human tests in game and makes decisions about implementation.
Log findings, decisions and anything beyond this spec in `results_stepF.md`.
**Do not edit other documentation files**, such as `plan.md`, `pre_stepF.md` and `plan_stepF.md`. Log all updates in the results document, including decisions that change the original plan. Another agent will review them and fold them into the plan documentation.

Build with MSBuild or `dotnet build` (0 errors) before handing over.

Logging stays unconditional, like the existing code (`ElevatorUtil.Log`).

Create a new git branch `stepF` from `stepE` and make step-by-step commits as you go. The first commit is the planner's uncommitted work in the tree (icons, docs), as a baseline.

## 1. Already done by the planner: icons (F1)

Uncommitted on `stepE`, built with 0 errors, **not tested in game**. Don't redo it; include it in the baseline commit.

- `ElevatorTextures` loads each supplied icon with `LoadIcon(fileName, fallback)` from `Things/Buildings/DeepElevator/`. A missing file falls back to the previous vanilla placeholder.
- Wired: elevator Connect to... (`DeepElevator_ConnectToIcon`), Go to partner (`DeepElevator_GoToPartnerIcon`), FIFO, merge, drop and insert toggles (`DeepElevator_FIFOIcon`, `_MergeIcon`, `_DropNearbyIcon`, `_InsertIcon`); loader Connect to... (`DeepElevatorLoader_ConnectToIcon`) and transfer toggle (`DeepElevatorLoader_TransferToElevatorIcon`). The priority icons were already wired.
- **Unload (decided, human):** the elevator's Unload now uses the vanilla transport pod and shuttle unload icon (`CompTransporter.CancelLoadCommandTex`), the same as the loader's "Unload all" (`ElevatorTextures.Unload`). Before this, the elevator used `TexButton.Drop`.
- **Kept vanilla (decided, human):** slots − and +, the loader's Open transport menu, the vanilla Cancel loading, and the large elevator's build-menu icon (its sprite).
- `DeepElevator_Large_Orage.png` was moved out by the human; its git deletion is part of the baseline.
- The four lights-only PNGs (§5) were added by the human and are part of the baseline too.

## 2. Goal (F2): the elevator as a weak lamp in its status colour

Option C of `pre_stepF.md` §2: the elevator lights the cells around it like a weak lamp, green while working and red while blocked. It's dark when the sprite shows no light (disconnected, no power, partner unpowered). Plus optional night-visible state lights (§5), drawn only once the human supplies the files.

No new Harmony patches. The loader doesn't glow.

## 3. Verified vanilla facts (decompiled 1.6)

- `CompProperties_Glower`: `glowRadius` (default 14, maximum 40), `glowColor` (`ColorInt`; vanilla writes the alpha as 0), `overlightRadius`, `colorPickerEnabled`, `darklightToggle`. With the last two false, the comp adds no gizmos.
- `CompGlower.ShouldBeLitNow` (protected virtual) = spawned, `FlickUtility.WantsToBeOn` (true without a flick comp), the parent's `IThingGlower.ShouldBeLitNow()` if the parent implements it, and the same for every comp. `CompPowerTrader` implements it as `PowerOn`.
- `UpdateLit(map)` switches registration when `ShouldBeLitNow` differs from the stored state. Power signals (`PowerTurnedOn`/`Off`) call it automatically. `PostSpawnSetup` and `PostMapInit` refresh it.
- `GlowColor` setter (`SetGlowColorInternal`): if `ShouldBeLitNow`, it deregisters, stores the override, and registers again. The override and the lit state are saved with the comp.
- **Order matters:** the setter decides by `ShouldBeLitNow`, not by the stored lit state. So call `UpdateLit` first, which makes the two agree, then set the colour.
- **Cost:** a registration change re-floods that light in a Burst job (dirty lights only) and rebuilds the ground-glow and roof mesh sections in its radius. It's cheap for rare changes.
- `ShaderDatabase.TransparentPostLight` exists in 1.6. Its name says it's drawn after lighting; **not verified** that it stays bright at night when drawn through `Graphic.Draw`. That's test 6.

## 4. Glow implementation

### 4.1 One light rule for sprite and glow

New enum or private helper: `LightState { Off, Green, Red }` from `Status`:
- `Disconnected`, `NoPower`, `PartnerUnpowered` → `Off`
- `Blocked` → `Red`
- otherwise → `Green`

`CurrentLightGraphic()` uses it instead of its own condition, so the sprite and the glow can't diverge.

### 4.2 Def (`DeepElevatorBase` comps, or per size where values differ)

```xml
<li Class="CompProperties_Glower">
  <glowRadius>4</glowRadius>              <!-- large: 5; first values, tune in game -->
  <glowColor>(60,200,80,0)</glowColor>    <!-- the green; replaced per instance at runtime -->
</li>
```

`ElevatorSettingsExtension` gets `ColorInt greenGlowColor` and `ColorInt redGlowColor`, set in the XML of both sizes. Start with green `(60,200,80,0)` and red `(220,60,50,0)`. These are starting values: they should match the lamp colour in the PNGs, so sample the lamp pixels of `DeepElevator_Green.png` and `DeepElevator_Red.png`, log the values in the results, and let the human tune. Brightness comes from the colour value; vanilla multiplies above 1 for bright lamps. Keep it dim.

### 4.3 Code (`Building_Elevator.Power.cs`)

- `Building_Elevator : ..., IThingGlower` with `bool IThingGlower.ShouldBeLitNow() => CurrentLightState != LightState.Off`. Power on this end is also checked by `CompPowerTrader`, which agrees.
- Cache `CompGlower glowComp` with the power comp. A def without one simply doesn't glow; log once, like the missing power comp.
- `UpdateGlow()`, called from the existing `IsHashIntervalTick(PowerUpdateInterval)` block in `Tick`, next to `LogStatusIfChanged`:
  - If the light state is unchanged since the last call (runtime field), return.
  - `glowComp.UpdateLit(Map)` first (on/off), then, if lit, set `glowComp.GlowColor` to the green or red colour only when it differs (the setter re-registers).
  - Log the change: `Glow: <LogId> <old> → <new>`.
- Also call `UpdateGlow()` at the end of `SpawnSetup`, and when a link is made or lost (`Link`/`Unlink`), so the light doesn't wait up to 30 ticks there.
- **Accepted lag:** the sprite follows the status every frame, while the glow follows within 30 ticks (half a second). Blocked and unblocked flips are rare, and no blinking exists.
- No per-frame work. Nothing in `DrawAt`.

## 5. Night-visible state lights

The current green and red overlays are full sprites drawn with `Cutout`, so they darken at night. Add optional **lights-only** overlays drawn on top with `TransparentPostLight`:

- `ElevatorSettingsExtension`: `greenNightTexPath`, `redNightTexPath`. **The files are supplied** (human), so set them in the XML of both sizes. When a path is unset, nothing changes.
- **Size check:** `DeepElevator_Large_RedLight.png` is 256x256 on disk, while the large sprite and `DeepElevator_Large_GreenLight.png` are 512x512. The human will re-export it. If it's still 256x256 when you start, wire it anyway and note it in the results; the code doesn't depend on the pixel size, since the graphic is scaled to the draw size, but the lamp detail will look blurrier than the green one.
- `ElevatorUtil.LoadOverlayGraphic` gets an optional shader parameter (default `Cutout`). The night graphics load with `ShaderDatabase.TransparentPostLight`, cached per def like the state lights. A missing file is logged once and skipped, as now.
- `DrawAt`: after the state light, draw the matching night graphic (same index), a little higher (for example `Altitudes.AltIncVect * 0.6f`).
- Expected files (human): `DeepElevator_GreenLight`, `DeepElevator_RedLight` (256x256), `DeepElevator_Large_GreenLight`, `DeepElevator_Large_RedLight` (512x512), on the same canvas as the base sprite.

## 6. Language keys

None expected. A dev log line only.

## 7. Tests for the human

1. Icons (F1): every elevator and loader gizmo shows its new icon; Unload on both buildings shows the vanilla unload icon; slots ± and Open transport menu are unchanged. Remove one icon file temporarily: the vanilla placeholder appears, with no error.
2. Glow on: a working pair at night or in a dark DeepRim layer. Both ends light the cells around them in green, weakly. Check the radius on 1x1 and large (the large one may look slightly off-centre).
3. Blocked: fill the destination so the buffer holds; the light turns red with the sprite (up to half a second later); back to green after a delivery.
4. Off states: disconnect, cut power on this end, cut power on the partner. The light goes off in each case, and comes back.
5. Save and reload with a green and a red elevator: same light after load, no log errors. Deconstruct a lit elevator: the light disappears.
6. Night lights: the lamp pixels stay bright at night and look normal in daylight. If `TransparentPostLight` doesn't stay bright, report it; the shader is the only change needed.
7. Room and darkness: a lit elevator in a dark room clears the "dark" penalty only near itself, and doesn't change a lit room noticeably. Note the radius and colour values that look right.

## 8. Known and accepted

- The glow follows the status within 30 ticks; the sprite follows at once.
- No player toggle for the light (option D), and no mod setting yet (`plan.md` §7).
- The loader has no glow.
