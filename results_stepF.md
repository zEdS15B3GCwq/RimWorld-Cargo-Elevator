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
