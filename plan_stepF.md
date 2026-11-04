# DeepElevator – Step F: glow and night-visible lights

Handoff spec for the implementing agent. Background: `plan.md`, `pre_stepF.md`. RimWorld 1.6 only.
The human tests in game and makes decisions about implementation.
Log findings, decisions and anything beyond this spec in `results_stepF.md`.
**Do not edit other documentation files**, such as `plan.md`, `pre_stepF.md` and `plan_stepF.md`. Log all updates in the results document, including decisions that change the original plan. Another agent will review them and fold them into the plan documentation.

Build with MSBuild or `dotnet build` (0 errors) before handing over.

Logging stays unconditional, like the existing code (`ElevatorUtil.Log`).

Create a new git branch `stepF` from `stepE` and make step-by-step commits as you go. The first commit is the uncommitted work already in the tree, as a baseline: the gizmo icon wiring (`ElevatorTextures` and the gizmos using it; built and tested in game), the four lights-only textures (§4) and the docs. Don't change the icon wiring.

## 1. Goal

1. **Glow:** the elevator lights the cells around it like a weak lamp, in its status colour: green while working, red while blocked. It's dark when the sprite shows no light (disconnected, no power, partner unpowered). This is option C of `pre_stepF.md` §2.
2. **Night-visible lights:** the lamp pixels of the elevator stay bright at night, using the lights-only textures the human supplied.

No new Harmony patches. The loader doesn't glow.

## 2. Verified vanilla facts (decompiled 1.6)

- `CompProperties_Glower`: `glowRadius` (default 14, maximum 40), `glowColor` (`ColorInt`; vanilla writes the alpha as 0), `overlightRadius`, `colorPickerEnabled`, `darklightToggle`. With the last two false, the comp adds no gizmos.
- `CompGlower.ShouldBeLitNow` (protected virtual) = spawned, `FlickUtility.WantsToBeOn` (true without a flick comp), the parent's `IThingGlower.ShouldBeLitNow()` if the parent implements it, and the same for every comp. `CompPowerTrader` implements it as `PowerOn`.
- `UpdateLit(map)` switches registration when `ShouldBeLitNow` differs from the stored state. Power signals (`PowerTurnedOn`/`Off`) call it automatically. `PostSpawnSetup` and `PostMapInit` refresh it.
- `GlowColor` setter (`SetGlowColorInternal`): if `ShouldBeLitNow`, it deregisters, stores the override, and registers again. The override and the lit state are saved with the comp.
- **Order matters:** the setter decides by `ShouldBeLitNow`, not by the stored lit state. So call `UpdateLit` first, which makes the two agree, then set the colour.
- **Cost:** a registration change re-floods that light in a Burst job (dirty lights only) and rebuilds the ground-glow and roof mesh sections in its radius. It's cheap for rare changes.
- `ShaderDatabase.TransparentPostLight` exists in 1.6. Its name says it's drawn after lighting; **not verified** that it stays bright at night when drawn through `Graphic.Draw`. That's test 5.

## 3. Glow

### 3.1 One light rule for sprite and glow

New enum or private helper: `LightState { Off, Green, Red }` from `Status`:
- `Disconnected`, `NoPower`, `PartnerUnpowered` → `Off`
- `Blocked` → `Red`
- otherwise → `Green`

`CurrentLightGraphic()` uses it instead of its own condition, so the sprite and the glow can't diverge.

### 3.2 Def (`DeepElevatorBase` comps, or per size where values differ)

```xml
<li Class="CompProperties_Glower">
  <glowRadius>4</glowRadius>              <!-- large: 5; first values, tune in game -->
  <glowColor>(60,200,80,0)</glowColor>    <!-- the green; replaced per instance at runtime -->
</li>
```

`ElevatorSettingsExtension` gets `ColorInt greenGlowColor` and `ColorInt redGlowColor`, set in the XML of both sizes. Start with green `(60,200,80,0)` and red `(220,60,50,0)`. These are starting values: they should match the lamp colour in the PNGs, so sample the lamp pixels of `DeepElevator_GreenLight.png` and `DeepElevator_RedLight.png`, log the values in the results, and let the human tune. Brightness comes from the colour value; vanilla multiplies above 1 for bright lamps. Keep it dim.

### 3.3 Code (`Building_Elevator.Power.cs`)

- `Building_Elevator : ..., IThingGlower` with `bool IThingGlower.ShouldBeLitNow() => CurrentLightState != LightState.Off`. Power on this end is also checked by `CompPowerTrader`, which agrees.
- Cache `CompGlower glowComp` with the power comp. A def without one simply doesn't glow; log once, like the missing power comp.
- `UpdateGlow()`, called from the existing `IsHashIntervalTick(PowerUpdateInterval)` block in `Tick`, next to `LogStatusIfChanged`:
  - If the light state is unchanged since the last call (runtime field), return.
  - `glowComp.UpdateLit(Map)` first (on/off), then, if lit, set `glowComp.GlowColor` to the green or red colour only when it differs (the setter re-registers).
  - Log the change: `Glow: <LogId> <old> → <new>`.
- Also call `UpdateGlow()` at the end of `SpawnSetup`, and when a link is made or lost (`Link`/`Unlink`), so the light doesn't wait up to 30 ticks there.
- **Accepted lag:** the sprite follows the status every frame, while the glow follows within 30 ticks (half a second). Blocked and unblocked flips are rare, and no blinking exists.
- No per-frame work. Nothing in `DrawAt`.

## 4. Night-visible state lights

The current green and red overlays are full sprites drawn with `Cutout`, so they darken at night. Add **lights-only** overlays drawn on top with `TransparentPostLight`:

- **Files (supplied by the human):** `DeepElevator_GreenLight`, `DeepElevator_RedLight` (256x256), `DeepElevator_Large_GreenLight`, `DeepElevator_Large_RedLight` (512x512), all in `Things/Buildings/DeepElevator/`, on the same canvas as the base sprite.
- `ElevatorSettingsExtension`: `greenNightTexPath`, `redNightTexPath`, set in the XML of both sizes. When a path is unset, nothing changes.
- `ElevatorUtil.LoadOverlayGraphic` gets an optional shader parameter (default `Cutout`). The night graphics load with `ShaderDatabase.TransparentPostLight`, cached per def like the state lights. A missing file is logged once and skipped, as now.
- `DrawAt`: after the state light, draw the matching night graphic (same index), a little higher (for example `Altitudes.AltIncVect * 0.6f`).

## 5. Language keys

None expected. A dev log line only.

## 6. Tests for the human

1. Glow on: a working pair at night or in a dark DeepRim layer. Both ends light the cells around them in green, weakly. Check the radius on 1x1 and large (the large one may look slightly off-centre).
2. Blocked: fill the destination so the buffer holds; the light turns red with the sprite (up to half a second later); back to green after a delivery.
3. Off states: disconnect, cut power on this end, cut power on the partner. The light goes off in each case, and comes back.
4. Save and reload with a green and a red elevator: same light after load, no log errors. Deconstruct a lit elevator: the light disappears.
5. Night lights: the lamp pixels stay bright at night and look normal in daylight, with no halo, on both sizes and both colours. If `TransparentPostLight` doesn't stay bright, report it; the shader is the only change needed.
6. Room and darkness: a lit elevator in a dark room clears the "dark" penalty only near itself, and doesn't change a lit room noticeably. Note the radius and colour values that look right.

## 7. Known and accepted

- The glow follows the status within 30 ticks; the sprite follows at once.
- No player toggle for the light (option D), and no mod setting yet (`plan.md` §7).
- The loader has no glow and no night overlay.
