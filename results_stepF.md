# DeepElevator – Step F results: glow and night-visible lights

Status: implemented and built (0 errors, 0 warnings). **Not tested in game**; the tests in `plan_stepF.md` §6 are the human's. Branch `stepF` (from `stepE`).

## What was done

- **Baseline commit:** the tree held only the doc changes (`plan.md`, `plan_stepF.md`, `pre_stepF.md`). The icon wiring and the four lights-only PNGs were already committed in `9d0af2e` ("before step F"), so the baseline commit contains the docs only. The icon wiring is untouched.
- **One light rule (`Building_Elevator.Power.cs`):** private enum `LightState { Off, Green, Red }` and `CurrentLightState`, derived from `Status` (Disconnected, NoPower, PartnerUnpowered → Off; Blocked → Red; else Green). `DrawAt` and the glow both use it. `CurrentLightGraphic()` is gone.
- **Glow:** `Building_Elevator : IThingGlower` (`ShouldBeLitNow() => CurrentLightState != Off`). `CacheGlow()` (called from `SpawnSetup` after `CachePower()`) stores the `CompGlower`; a def without one logs a warning once per spawn and doesn't glow. `UpdateGlow()` returns when the state equals the last applied one (`LightState? lastGlowState`, null after spawning so the first call always writes). Otherwise it calls `UpdateLit(Map)` first, then sets `GlowColor` only if it differs, and logs `Glow: <LogId> <old> → <new>`.
- **When `UpdateGlow()` runs:** at the end of `SpawnSetup`; in `Link` (both ends); in `Unlink` (this end and the former partner); and in the existing `IsHashIntervalTick(PowerUpdateInterval)` block of `Tick`. Nothing in `DrawAt` beyond reading the state.
- **Defs:** `CompProperties_Glower` in each size's `comps` (not in the base, since the radius differs per size): radius 4 (1x1) and 5 (large), `glowColor` = the green. `ElevatorSettingsExtension` got `greenGlowColor`, `redGlowColor` (defaults equal the XML values) and `greenNightTexPath`, `redNightTexPath`; both sizes set them in XML.
- **Night lights:** `ElevatorUtil.LoadOverlayGraphic(def, path, Shader shader = null)` (null = Cutout). `DrawAt` draws the night graphic after the state light, at `AltIncVect * 0.6f` (state light stays at 0.5), loaded with `ShaderDatabase.TransparentPostLight` and cached per def in its own dictionary. The two caches share one loader method, `GetLightGraphics(cache, shader, greenPath, redPath)`.
- The `ElevatorDiagnostics` settings line now also lists the night texture paths.

## Sampled lamp colours (logged as asked)

Mean RGB of the pixels with alpha ≥ 200 in the supplied PNGs:

| File | Opaque pixels | Mean RGB |
| --- | --- | --- |
| `DeepElevator_GreenLight` (256) | 1356 | (55, 249, 105) |
| `DeepElevator_RedLight` (256) | 1356 | (252, 74, 59) |
| `DeepElevator_Large_GreenLight` (512) | 5228 | (57, 224, 79) |
| `DeepElevator_Large_RedLight` (512) | 5228 | (224, 41, 48) |

The spec's starting glow values, green `(60,200,80,0)` and red `(220,60,50,0)`, have the same hue as the sampled lamps (red is close to the large lamp; green is between the two). I kept them as given for both sizes, since the glow brightness comes from the channel values and the request was to keep it dim. Using the sampled values instead would be a little brighter and more saturated. Tuning is the human's call (radius 4/5, the two colours in the XML).

## Decisions and notes beyond the spec

- **Order of calls in `UpdateGlow`:** as specified, `UpdateLit` then the colour setter. Without an extra step, a glower that relights itself on the `PowerTurnedOn` signal uses its stored colour until the next `UpdateGlow` (at most 30 ticks): after a power loss during a red state, the comp may light green for up to half a second before the tick corrects it. This is the accepted lag. No extra code was added.
- **`Unlink` during despawn or map removal:** `UpdateGlow()` runs there on this end too (the elevator is still spawned at that point). `CompGlower.PostDeSpawn` calls `UpdateLit` again and finds it already off, so there is no double deregistration. Not tested on map removal; if the log shows an error there, guard `UpdateGlow` in `Notify_MyMapRemoved`.
- **Load:** `CompGlower` registers itself in `PostSpawnSetup` before `CachePower()` runs (our `powerComp` is still null, so `IsPowered` counts as true; `partner.IsPowered` likewise for an unspawned partner). The end-of-`SpawnSetup` call and the periodic tick correct the state, so a wrong light is possible for up to 30 ticks after load. Not seen, only reasoned.
- **Line endings:** the edited source and XML files are CRLF as before; `ElevatorDiagnostics.cs` was and is LF.
- The shader name `TransparentPostLight` compiles. Whether it stays bright at night through `Graphic.Draw` is test 5.

## Files changed

`Building_Elevator.cs`, `Building_Elevator.Power.cs`, `ElevatorSettingsExtension.cs`, `ElevatorUtil.cs`, `ElevatorDiagnostics.cs`, `Defs/ThingDefs/DeepElevator_Elevator.xml`, the rebuilt `DeepElevator.dll`.

## Not done / open for the human

- All in-game tests (§6 of `plan_stepF.md`).
- Tuning of radius and colour; whether the large elevator's light looks off-centre.
- No language keys were added (none needed).

## Follow-up: states = base sprite + lights-only overlay (experiment, revert-able)

Reason: the full `Green`/`Red` state sprites already showed a lamp, and the `*Light` overlay (drawn after lighting, so always full strength) added to it, which was too bright in daylight.

- The full state sprites (`DeepElevator_Green`, `_Red`, `_Large_Green`, `_Large_Red`, `Cutout`) are no longer drawn. A state is now the base sprite plus the matching `*Light` overlay only, drawn at `AltIncVect * 0.5f`.
- Renamed the extension fields `greenTexPath/redTexPath/greenNightTexPath/redNightTexPath` to `greenLightTexPath/redLightTexPath` (they now point at the `*Light` PNGs); one graphic cache instead of two. XML and the diagnostics line follow. The old state PNGs stay on disk, unused.
- This is a separate commit. To go back to the previous look, revert it.
- Open: whether the base sprite alone (the off look) plus the overlay looks right by day; the base sprite may need its own dim lamp or none.

### Decision (human, after testing in game)

Kept: the elevator's states are the **base sprite plus the lights-only `*Light` overlay**, drawn with `TransparentPostLight`. The full `Green`/`Red` state sprites are not used any more (plan: `greenTexPath`/`redTexPath` → `greenLightTexPath`/`redLightTexPath`; the PNGs `DeepElevator_Green`, `_Red`, `_Large_Green`, `_Large_Red` and the unused `_Orange` can be deleted). The glow works in both colours on both sizes. Fold into `plan.md`.

## Addition: loader glow and overlay

Requested after the elevator part was accepted: the loader gets the same glow and lights-only overlay, following the same rules as its orange look, including the blink. Built (0 errors, 0 warnings); **not tested in game**.

- **Overlay:** `DeepElevatorLoader_OrangeLight.png` (256x256, supplied) replaces the full `DeepElevatorLoader_Orange` sprite, loaded with `TransparentPostLight` and drawn at `AltIncVect * 0.5f` as before. `LoaderSettingsExtension.loadingTexPath` → `lightTexPath` (XML updated). `DeepElevatorLoader_Orange.png` is now unused.
- **Same rules as the look:** `ShowsOrangeLook()` is the single rule: an order in progress → constant orange; items waiting without an order → orange and normal alternating (60-tick period); otherwise normal. `Building_TransportLoader : IThingGlower` returns it, so the glow is on exactly when the overlay is drawn.
- **Glow:** `CompProperties_Glower` in the loader def: radius 3, colour `(200,120,40,0)`. The sampled lamp in the PNG is about (223,142,57) (pixels with alpha ≥ 200; 698 of them), so the glow colour is the same hue, a little dimmer, like the elevator values. No colour override at runtime; the loader has one colour. Radius and colour are first values to tune.
- **Blink timing:** `UpdateGlow()` runs on the global beat `TicksGame % 30 == 0` from `Tick` (not a per-thing hash interval), so the glow flips on the same tick as the drawn look. It calls `UpdateLit(Map)` only when the on/off value changes. Cost: one registration change (a small Burst re-flood plus mesh rebuild in the radius, as in `plan_stepF.md` §2) per half blink for each blinking loader. This is more frequent than anything on the elevator, so watch it with several waiting loaders. A coarser glow (no blink, lit for the whole waiting time) is the fallback if it costs too much.
- **Other changes of look** (an order starts or ends, items arrive or leave) reach the glow within 30 ticks; the drawn overlay follows at once.
- **Log:** `Glow: <ThingID> <old> → <new>` where the look is `loading`, `waiting (blinking)` or `idle`. The blink steps are not logged, to avoid a line every half second.
- `UpdateGlow()` also runs at the end of `SpawnSetup`. The comp is cached there; a def without it logs a warning. No power comp, so the glower's own power check does not apply.

### Loader blink offset

Each loader's blink is shifted by `BlinkOffsetTicks` (0–29 ticks, from `thingIDNumber`), so loaders do not all flip, and re-register their light, on the same tick. The drawn overlay and the glow use the same offset, so they stay in step. It is derived from the thing id rather than random-and-saved: it differs per loader, stays the same after loading, and needs no save field. The spread is over half a period, which is enough to separate the flips, since each loader flips every 30 ticks.

### Loader blink in real time (replaces the tick-based blink and offset above)

Game-tick blinking sped up with the game speed (6 times as fast at speed 3). The blink period is now **1 second of real time** (`BlinkPeriodSeconds`), at every speed.

- `Tick` reads `UnityEngine.Time.realtimeSinceStartup` (only while items wait and no order is in progress), steps a runtime field `blinkOn`, and calls `UpdateGlow()` when the step changes. `ShowsOrangeLook()` returns `blinkOn` for the waiting look, so the drawn overlay and the glow always show the same step. Paused game: no ticks, so the blink and the glow both hold their step; the blink does not run on while the game is paused.
- At high speeds several ticks run per frame, so the step is seen at most one tick late; at normal speed that is about 17 ms.
- The per-loader shift is kept, now in seconds: `BlinkOffsetSeconds` = 0–0.49 of a period, from `thingIDNumber`, so loaders do not flip on the same tick.
- Other look changes (order starts or ends, items arrive or leave) are still caught every 30 ticks (`IsHashIntervalTick`); the glow can lag them by up to 30 ticks.
- If wanted, the alternative is a slower tick period: 360 ticks per blink gives about 1 blink per second at speed 3 (6x), but 6 seconds per blink at normal speed.
