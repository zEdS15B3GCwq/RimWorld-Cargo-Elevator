# DeepElevator – Step F preparation: visuals

Findings for the human's decisions before `plan_stepF.md` was written. **Decisions (human):** icons wired as supplied (done before Step F, tested in game); one vanilla unload icon on both buildings; slots ±, Open transport menu, Cancel loading and the large menu icon stay vanilla or as they are; glow option C and the night-visible lights, whose files are supplied, are Step F (`plan_stepF.md`). The tables below show the current state. Basis: the `stepE` branch as of `5446e38`, the texture folder, and decompiled RimWorld 1.6 (`include/decompiled`). No game data folder was available on this machine, so vanilla def values below are from memory and marked as such. Nothing was run in game.

## 1. Textures and icons

All mod textures are in `DeepElevator/Textures/Things/Buildings/DeepElevator/`. Paths below are relative to `Textures/`, without `.png`.

### 1.1 Building sprites (all present)

| Use | Path | Size | Referenced by |
| --- | --- | --- | --- |
| 1x1 elevator: base (off look, blueprint, ghost) | `.../DeepElevator` | 256x256 | def `texPath` |
| 1x1 elevator: green / red state | `.../DeepElevator_Green`, `.../DeepElevator_Red` | 256x256 | def extension |
| 1x1 elevator: build-menu icon | `.../DeepElevator_MenuIcon` | 256x256 | def `uiIconPath` |
| Large elevator: base | `.../DeepElevator_Large` | 512x512 | def `texPath` |
| Large elevator: green / red state | `.../DeepElevator_Large_Green`, `.../DeepElevator_Large_Red` | 512x512 | def extension |
| Loader: base | `.../DeepElevatorLoader` | 256x256 | def `texPath` |
| Loader: loading look | `.../DeepElevatorLoader_Orange` | 256x256 | `LoaderSettingsExtension` |
| Lights-only night overlays (§2.4) | `.../DeepElevator_GreenLight`, `.../DeepElevator_RedLight` | 256x256 | Step F |
| Large lights-only night overlays | `.../DeepElevator_Large_GreenLight`, `.../DeepElevator_Large_RedLight` | 512x512 | Step F |
| Unused (kept for reuse) | `.../DeepElevator_Orange` | | nothing |

### 1.2 Gizmo icons

Every supplied icon is wired and tested in game. Each loads with a fallback to the vanilla icon in the second column if its file is missing.

**Elevator**

| Gizmo | Vanilla fallback or icon | File | Status |
| --- | --- | --- | --- |
| Connect to... | vanilla `UI/Commands/LoadTransporter` (`ElevatorTextures.ElevatorConnect`) | `DeepElevator_ConnectToIcon` | wired, tested |
| Go to partner | vanilla `UI/Widgets/Search` | `DeepElevator_GoToPartnerIcon` | wired, tested |
| Unload | vanilla pod and shuttle unload icon (`CompTransporter.CancelLoadCommandTex`) | none | vanilla, decided |
| FIFO toggle | vanilla `TexCommand.HoldOpen` | `DeepElevator_FIFOIcon` | wired, tested |
| Merge toggle | vanilla `TexCommand.GatherSpotActive` | `DeepElevator_MergeIcon` | wired, tested |
| Drop on floor toggle | vanilla `TexCommand.ForbidOff` | `DeepElevator_DropNearbyIcon` | wired, tested |
| Allow insert toggle | vanilla `TexCommand.Install` | `DeepElevator_InsertIcon` | wired, tested |
| Slots − | vanilla `TexButton.Minus` | none | vanilla, decided |
| Slots + | vanilla `TexButton.Plus` | none | vanilla, decided |
| Build loader | the loader's build-menu icon, which is its sprite (no `uiIconPath`) | none | as is |
| DEV gizmos | none | none | vanilla dev gizmos have no icon |

**Loader**

| Gizmo | Vanilla fallback or icon | File | Status |
| --- | --- | --- | --- |
| Open transport menu | vanilla `UI/Commands/LoadTransporter` (`ElevatorTextures.LoaderEnter`) | none | vanilla, decided. The code recognises this command by its icon reference |
| Cancel loading | vanilla `MapPortal` cancel icon (private, red X) | none | vanilla, decided |
| Unload all | vanilla pod and shuttle unload icon, as on the elevator | none | vanilla, decided |
| Transfer items to elevator | vanilla `TexCommand.SelectCarriedThing` | `DeepElevatorLoader_TransferToElevatorIcon` | wired, tested |
| Priority (5 levels) | vanilla `TexButton.ReorderUp` | `DeepElevatorLoader_Priority{Low,Normal,Preferred,Important,Critical}` | wired, tested |
| Connect to... | the same as the elevator's Connect to... | `DeepElevatorLoader_ConnectToIcon` | wired, tested |
| DEV gizmo | none | none | |

**Other**

| Use | File | Status |
| --- | --- | --- |
| Large elevator build-menu icon | none | the sprite, decided |
| Mod preview in the mod list | **`About/Preview.png`** (640x360 per `plan.md` §5) | missing; not part of Step F |
| Lights-only state overlays | §2.4 | supplied; wired in Step F |

**Missing:** only `About/Preview.png`, outside Step F.

**Notes**
- **Size:** 256x256 is fine for gizmos; the game scales them down.
- **Location:** the icons stay next to the building sprites (decided).

## 2. Glow: the elevator as a weak lamp

### 2.1 How vanilla light works (decompiled 1.6)

- A building glows through `CompGlower`, added in the def as `CompProperties_Glower`: `glowRadius` (default 14, maximum 40), `glowColor` (a `ColorInt`; vanilla writes the alpha as 0) and `overlightRadius`.
- It lights the glow grid: the floor, walls and pawns around it get brighter. That's what makes a cell count as lit for work speed, mood and plant growth. **It needs no texture.** Vanilla lamps have no glow sprite either.
- **On/off:** lit while spawned and `ShouldBeLitNow`. That checks a flick switch if there is one, the parent if it implements `IThingGlower`, and every comp that implements `IThingGlower`. **`CompPowerTrader` is one** (`ShouldBeLitNow => PowerOn`), and the glower updates itself on the `PowerTurnedOn`/`PowerTurnedOff` signals. The elevator already has `CompPowerTrader`.
- **Colour per elevator:** `GlowColor` has a per-instance override with a setter, saved with the comp. After a change, `ForceRegister(map)` re-registers the light.
- **Cost of a change:** the light is re-flooded in a Burst job, only for dirty lights, and the map mesh sections it touches are rebuilt (ground glow, roofs). That's cheap for one small light changing now and then. It's the cost to watch only if many lights change often.

### 2.2 Options, simplest first

| Option | What it does | Code | Assets |
| --- | --- | --- | --- |
| **A. Steady lamp while powered** | Lit whenever the elevator has power, also when disconnected or idle | **None:** about 5 lines of XML in `DeepElevatorBase` | none |
| B. Lamp while operational | Lit only when connected and both ends powered | small: `Building_Elevator` implements `IThingGlower`, and the status update calls `UpdateLit` when it changes | none |
| C. Lamp in the status colour | Green while working, red while blocked; off when disconnected or unpowered | small: B plus `GlowColor` and `ForceRegister` on a status change, and a two-colour table | none. The colours must match the sprite lights, so they're chosen to match the PNGs |
| D. Player toggle per elevator | A light on/off gizmo, saved | small, on top of A, B or C | **one icon**, `DeepElevator_LightIcon` |

- **Blinking** isn't needed any more. Step D's glow notes planned it for the loading state, which E1 removed. The elevator's green and red are steady now.
- **The loader** could glow orange while loading in the same way, but it has no power comp. It would need B-style code, and I'd leave it out unless you want it.
- **Mod setting** (`plan.md` §7 lists "elevators as light sources" as a setting): waits for the settings step. Until then, D is the per-building switch if one is wanted.

### 2.3 Values to tune in game

- **Radius:** a weak lamp suggests about 3–5 cells, against vanilla's standing lamp at about 12 (from memory). 1x1 and large can differ.
- **Colour and brightness:** dim and neutral for A or B. In vanilla, brightness comes from the colour value itself, which is often multiplied above 1.
- **Large elevator:** the light starts at `Position`, which on a 2x2 is one of the four cells, not the exact centre. With a small radius that may look slightly off-centre; test it.
- **Power:** a glower draws nothing extra. The elevator's idle 10 W could be raised to justify a lamp, which is a balance choice.

### 2.4 Lights that stay bright at night (optional, needs assets)

The state overlays are drawn with the `Cutout` shader (`ElevatorUtil.LoadOverlayGraphic`), so they darken at night like the rest of the sprite. A light that should look bright in the dark needs a self-lit shader, `ShaderDatabase.TransparentPostLight`, which exists in 1.6 and is drawn after lighting.

But the overlays are full sprites of the whole building. A full sprite drawn with that shader would make the whole elevator look lit. This needs **lights-only overlays**: same canvas, transparent except the lamp pixels and a faint spill. The human supplied them:
- `DeepElevator_GreenLight`, `DeepElevator_RedLight` (256x256)
- `DeepElevator_Large_GreenLight`, `DeepElevator_Large_RedLight` (512x512)

The code change is small: the overlay loader takes the shader, and the def extension takes the new paths. This is independent of the glow and can be combined with any option above.

### 2.5 Recommendation

A or C. A costs only XML and gives the lamp. C adds the status colour with small code and still no new assets. Add D only if players should be able to switch it off before the settings step exists. §2.4 is separate polish that needs four new PNGs.

## 3. Decisions (human)

1. Glow option C, plus §2.4's night-visible lights: Step F.
2. Icon location: the current folder.
3. Loader "Unload all": the same vanilla unload icon as the elevator.
4. The vanilla "Cancel loading" icon stays.
