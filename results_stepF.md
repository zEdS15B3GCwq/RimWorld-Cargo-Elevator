# DeepElevator – Step F results: glow and night-visible lights

Status: **complete and tested in game by the human.** Built with 0 errors and 0 warnings. Branch `stepF` (from `stepE`). Verified: the glow in red and green on both sizes; the lights good underground; the light reach (measured, see below); green, red and Blocked restored after a save and load; a deleted glowing building loses its light; the loader's waiting (blinking) look and its order-in-progress look restored after a save and load; no log errors. Not tested: a large number of blinking loaders (planned for later). This document is the final state; it replaces the earlier per-change notes. The plan documents are to be updated from the **Decisions** section.

## Decisions (for the plan update)

1. **State look = base sprite + a lights-only overlay.** The elevator's green and red states, and the loader's orange state, are the base sprite plus a transparent-except-the-lamp overlay (`*Light.png`) drawn with `ShaderDatabase.TransparentPostLight`. The full state sprites (`Cutout`) were dropped. Reason: the full state sprites already carried a lamp, and the post-light overlay (always at full strength) added on top, which was too bright in daylight. Decided by the human after testing.
2. **Extension fields:** `greenTexPath`/`redTexPath` on `ElevatorSettingsExtension` became `greenLightTexPath`/`redLightTexPath`, pointing at the `*Light` PNGs. `LoaderSettingsExtension.loadingTexPath` became `lightTexPath`. (An earlier intermediate `greenNightTexPath`/`redNightTexPath` pair no longer exists.)
3. **Textures removed** (the human moved the files; committed): `DeepElevator_Green`, `_Red`, `_Large_Green`, `_Large_Red`, `DeepElevatorLoader_Orange` and the already unused `DeepElevator_Orange`. `plan.md` and `pre_stepF.md` §1.1 should drop them from the texture tables and add the `*Light` files as the state overlays.
4. **One light rule per building.** Elevator: private enum `LightState { Off, Green, Red }` from `Status` (Disconnected, NoPower, PartnerUnpowered → Off; Blocked → Red; else Green). Loader: `ShowsOrangeLook()`. The sprite overlay and the glow both read the same rule, so they cannot diverge.
5. **Glow is option C of `pre_stepF.md`** for the elevator (status colour, off when the sprite shows no light) and the same idea for the loader (orange, follows the look, including the blink). No player toggle (option D) and no mod setting yet.
6. **Loader blink runs on real time:** one blink per second (`BlinkPeriodSeconds = 1`) at every game speed. The first version used game ticks (60-tick period), which sped up 6 times at speed 3. The step is advanced in `Tick`, so it holds while the game is paused, and the drawn overlay and the glow show the same step.
7. **Per-loader blink shift:** 0–0.49 of a period from `thingIDNumber`, so loaders do not flip, and re-register their light, in the same tick. Derived from the id rather than random-and-saved: it differs per loader, is the same after loading, and needs no save field.
8. **Glow lag is accepted:** the elevator glow follows its status within 30 ticks (the sprite follows at once); the loader glow follows non-blink look changes within 30 ticks.

## What was done

### Elevator (`Building_Elevator.Power.cs`, `Building_Elevator.cs`)

- `LightState` and `CurrentLightState` (decision 4). `CurrentLightGraphic()` is gone; `DrawAt` draws the overlay for the current state at `AltIncVect * 0.5f` (above the base sprite, below items on the cell). Graphics are cached per def (`lightGraphicsByDef`), loaded lazily in `DrawAt` with `TransparentPostLight`; a missing file is logged once and skipped.
- `Building_Elevator : IThingGlower` (`ShouldBeLitNow() => CurrentLightState != Off`). `CacheGlow()` stores the `CompGlower` in `SpawnSetup` (after `CachePower()`); a def without one logs a warning and doesn't glow.
- `UpdateGlow()`: returns when the state equals the last applied one (`LightState? lastGlowState`, null after spawning so the first call always writes). Otherwise `UpdateLit(Map)` first, then `GlowColor` set only if it differs (the setter re-registers a lit light; this order is required, see `plan_stepF.md` §2). Logs `Glow: <LogId> <old> → <new>`.
- Called from: the end of `SpawnSetup`, `Link` (both ends), `Unlink` (this end and the former partner), and the existing `IsHashIntervalTick(PowerUpdateInterval)` block in `Tick`. Nothing per frame beyond reading the state.
- Defs (`DeepElevator_Elevator.xml`): `CompProperties_Glower` in each size's `comps` (the radius differs per size, and list items of a child are appended to the parent's): radius 4 (1x1), 5 (large), `glowColor` the green. `ElevatorSettingsExtension` has `greenGlowColor` `(60,200,80,0)`, `redGlowColor` `(220,60,50,0)` (defaults equal the XML), plus the two `*LightTexPath` fields.
- `ElevatorUtil.LoadOverlayGraphic(def, path, Shader shader = null)`: null = Cutout (the old behaviour); the lights pass `TransparentPostLight`.
- `ElevatorDiagnostics` lists the light texture paths.

### Loader (`Building_TransportLoader.cs`, `DeepElevator_TransportLoader.xml`, `LoaderSettingsExtension.cs`)

- Overlay: `DeepElevatorLoader_OrangeLight.png` (256x256, supplied) with `TransparentPostLight`, at `AltIncVect * 0.5f`.
- The look (`ShowsOrangeLook()`): an order in progress → constant orange; items waiting without an order → orange and normal alternating (`blinkOn`); otherwise normal.
- `Building_TransportLoader : IThingGlower` returns `ShowsOrangeLook()`. `CompProperties_Glower` in the def: radius 3, colour `(200,120,40,0)`. The loader has one colour, so there is no runtime colour override. It has no power comp, so the only condition is the look.
- Blink: in `Tick`, only while items wait and no order is in progress, `UnityEngine.Time.realtimeSinceStartup + BlinkOffsetSeconds` modulo the period decides the step; on a change `blinkOn` is stored and `UpdateGlow()` runs. `UpdateGlow()` calls `UpdateLit(Map)` only when the on/off value changes. At high speeds several ticks run per frame, so a flip is seen at most one tick late (about 17 ms at normal speed). The wall clock is read on every tick for a waiting loader; the cost is negligible and throttling would make the flip late by the throttle interval.
- Other look changes (order starts or ends, items arrive or leave) are caught by `IsHashIntervalTick(30)`. `UpdateGlow()` also runs at the end of `SpawnSetup`.
- Log: `Glow: <ThingID> <old> → <new>` where the look is `loading`, `waiting (blinking)` or `idle`; the blink steps are not logged.

## Sampled lamp colours

Mean RGB of the pixels with alpha ≥ 200 in the supplied PNGs:

| File | Opaque pixels | Mean RGB |
| --- | --- | --- |
| `DeepElevator_GreenLight` (256) | 1356 | (55, 249, 105) |
| `DeepElevator_RedLight` (256) | 1356 | (252, 74, 59) |
| `DeepElevator_Large_GreenLight` (512) | 5228 | (57, 224, 79) |
| `DeepElevator_Large_RedLight` (512) | 5228 | (224, 41, 48) |
| `DeepElevatorLoader_OrangeLight` (256) | 698 | (223, 142, 57) |

The glow colours have the same hue as the lamps but are dimmer, since the glow brightness comes from the channel values and the request was to keep it weak. The values are the human's to tune: radius 4 / 5 / 3 and the three colours, all in the def XML.

## Notes and known behaviour

- **Blocked state across a save (fixed):** the human saved a red elevator and after loading saw green with the status Idle, then red and Blocked again after a short while. The cause was `blockedSinceTick` being runtime-only: it was -1 after loading, so `IsBlocked` was false until the next failed send set it again and two send intervals passed. The light only followed the status. `blockedSinceTick` is now saved (`Scribe_Values.Look`, default -1; it is an absolute game tick, which stays valid after loading), so a blocked elevator loads as blocked. The held-entry list used for the inspect text stays empty until the first retry. Confirmed in game by the human: after a save and load the red light and the Blocked status are there at once.
- **Short wrong colour:** a glower relights itself on the power signals with its stored colour. After a power loss during a red state, it may light green for up to half a second before `UpdateGlow` corrects it. The same applies for up to 30 ticks after loading a save (the glower registers in `PostSpawnSetup` before `CachePower()` runs). Reasoned, not seen.
- **Despawn and map removal:** `Unlink` calls `UpdateGlow()` for this end while it is still spawned; `CompGlower.PostDeSpawn` then finds the light already off. Not specifically tested on map removal; if the log shows an error there, guard `UpdateGlow` in `Notify_MyMapRemoved`.
- **First blink step after a load:** the first orange or normal step of a waiting loader can be shorter than the others, since the blink phase comes from the wall clock and `blinkOn` starts as false. Seen by the human, accepted as negligible.
- **Paused game:** the loader blink and glow hold their step. A look change made while paused shows on the overlay at once but on the glow only after unpausing.
- **Cost:** a registration change re-floods that light in a Burst job and rebuilds the glow and roof mesh sections in its radius. The elevator changes rarely. The loader changes twice a second per blinking loader, so it is the thing to watch with many waiting loaders. The fallback is to hold the glow on for the whole waiting time instead of blinking it.
- **Rejected:** a game-tick blink with a long period (360 ticks gives about 1 blink per second at speed 3, but 6 seconds per blink at normal speed). Fading the overlay by ambient light (`GroundGlowAt`) was offered as an option for the daylight brightness and not needed once the full state sprites were dropped.
- **Line endings:** edited source and XML files keep their CRLF; `ElevatorDiagnostics.cs` and the loader XML were and are LF.

## Files changed

`Building_Elevator.cs`, `Building_Elevator.Power.cs`, `Building_TransportLoader.cs`, `ElevatorSettingsExtension.cs`, `LoaderSettingsExtension.cs`, `ElevatorUtil.cs`, `ElevatorDiagnostics.cs`, `Defs/ThingDefs/DeepElevator_Elevator.xml`, `Defs/ThingDefs/DeepElevator_TransportLoader.xml`, the rebuilt `DeepElevator.dll`; `DeepElevatorLoader_OrangeLight.png` added; six unused state sprites removed. No new Harmony patches, no language keys.

## Light reach measured by the human

Brightness of the glow with distance (cells out from the elevator): large (radius 5) 50%, 50%, 43%, 4%, so a visible reach of about 3 cells; 1x1 (radius 4) 50%, 50%, 5%, about 2 cells. The visible reach is shorter than the radius because the last cells before the cutoff are almost dark. **Decision (human): the radii stay as they are** (4, 5, and 3 for the loader); they look appropriate.

## Open items

- **Large-scale test, later:** many blinking loaders at once, to see the cost of the light registration changes (twice a second per blinking loader). The fallback is to hold the loader glow on for the whole waiting time instead of blinking it.
- **Colours:** the human does not want colour tuning; the values stay as in the XML.
- **Plan update:** apply the **Decisions** section to `plan.md` and `pre_stepF.md` (texture tables, extension fields, the real-time loader blink).
