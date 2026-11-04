# DeepElevator – Step F preparation: visuals

Findings for the human's decisions before `plan_stepF.md` was written. **Decisions (human):** icons wired as supplied; one vanilla unload icon on both buildings; slots ±, Open transport menu, Cancel loading and the large menu icon stay vanilla or as they are; the unused large orange overlay moved out; glow option C; night lights wired in Step F; the files are supplied. See `plan_stepF.md`. Basis: the `stepE` branch as of `5446e38`, the texture folder, and decompiled RimWorld 1.6 (`include/decompiled`). No game data folder was available on this machine, so vanilla def values below are from memory and marked as such. Nothing was run in game.

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
| Unused (kept for reuse) | `.../DeepElevator_Orange`, `.../DeepElevator_Large_Orage` | | nothing. **Typo:** `Orage` should be `Orange` if it's kept |

### 1.2 Gizmo icons

"Supplied" means the file exists. None of the supplied gizmo files is wired yet except the priority icons. The code uses the vanilla placeholders in the second column.

**Elevator**

| Gizmo | Placeholder in code now | File | Status |
| --- | --- | --- | --- |
| Connect to... | vanilla `UI/Commands/LoadTransporter` (`ElevatorTextures.Connect`) | `DeepElevator_ConnectToIcon` | supplied, not wired |
| Go to partner | vanilla `UI/Widgets/Search` | `DeepElevator_GoToPartnerIcon` | supplied, not wired |
| Unload | vanilla `TexButton.Drop` | **`DeepElevator_UnloadIcon`** | **missing** |
| FIFO toggle | vanilla `TexCommand.HoldOpen` | `DeepElevator_FIFOIcon` | supplied, not wired |
| Merge toggle | vanilla `TexCommand.GatherSpotActive` | `DeepElevator_MergeIcon` | supplied, not wired |
| Drop on floor toggle | vanilla `TexCommand.ForbidOff` | `DeepElevator_DropNearbyIcon` | supplied, not wired |
| Allow insert toggle | vanilla `TexCommand.Install` | `DeepElevator_InsertIcon` | supplied, not wired |
| Slots − | vanilla `TexButton.Minus` | **`DeepElevator_SlotsLessIcon`** | **missing** |
| Slots + | vanilla `TexButton.Plus` | **`DeepElevator_SlotsMoreIcon`** | **missing** |
| Build loader | the loader's build-menu icon, which is its sprite (no `uiIconPath`) | optional **`DeepElevatorLoader_MenuIcon`** | changes the build menu and this gizmo together |
| DEV gizmos | none | none | vanilla dev gizmos have no icon |

**Loader**

| Gizmo | Placeholder in code now | File | Status |
| --- | --- | --- | --- |
| Open transport menu | vanilla `UI/Commands/LoadTransporter` (`ElevatorTextures.LoaderEnter`) | **`DeepElevatorLoader_OpenMenuIcon`** | **missing**. The code recognises this command by its icon reference, which keeps working as long as one cached texture is used |
| Cancel loading | vanilla `MapPortal` cancel icon (private, red X) | optional **`DeepElevatorLoader_CancelLoadingIcon`** | vanilla gizmo; replacing its icon needs a small match by label in `GetGizmos` |
| Unload all | vanilla `CompTransporter.CancelLoadCommandTex` (red X) | **`DeepElevatorLoader_UnloadIcon`**, or reuse `DeepElevator_UnloadIcon` | **missing** |
| Transfer items to elevator | vanilla `TexCommand.SelectCarriedThing` | `DeepElevatorLoader_TransferToElevatorIcon` | supplied, not wired (`results_stepE2.md` names it without `Icon`; the file has it) |
| Priority (5 levels) | wired; vanilla `TexButton.ReorderUp` only if a file is missing | `DeepElevatorLoader_Priority{Low,Normal,Preferred,Important,Critical}` | supplied and in use |
| Connect to... | shares the elevator's placeholder | `DeepElevatorLoader_ConnectToIcon` | supplied, not wired; needs its own reference |
| DEV gizmo | none | none | |

**Other**

| Use | File | Status |
| --- | --- | --- |
| Large elevator build-menu icon | **`DeepElevator_Large_MenuIcon`** (`uiIconPath` on the large def) | **missing**. The large def has no `uiIconPath`, so the menu shows the sprite. The plan listed `DeepElevator_MenuIcon_large` before the textures were replaced |
| Mod preview in the mod list | **`About/Preview.png`** (640x360 per `plan.md` §5) | **missing** |
| Optional: lights-only state overlays | §2.4 | only if the self-lit lights are wanted |

**Missing, required for "no placeholders":** `DeepElevator_UnloadIcon`, `DeepElevator_SlotsLessIcon`, `DeepElevator_SlotsMoreIcon`, `DeepElevatorLoader_OpenMenuIcon`, `DeepElevatorLoader_UnloadIcon` (or reuse), `DeepElevator_Large_MenuIcon`, `About/Preview.png`.
**Optional:** `DeepElevatorLoader_MenuIcon`, `DeepElevatorLoader_CancelLoadingIcon`, a light-toggle icon if §2 adds one (`DeepElevator_LightIcon`).

**Notes**
- **Size:** 256x256 is fine for gizmos; the game scales them down.
- **Location:** the icons live next to the building sprites. Vanilla keeps gizmo icons under `UI/Commands/`. Moving them is optional and has no functional effect; I'd keep the current folder unless you prefer the convention.
- **Code work:** small. Each icon becomes one `ElevatorTextures` field loaded with a fallback to the current placeholder, the same pattern as the priority icons, and each gizmo uses its field. About 30 lines, plus `uiIconPath` on the large def.

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

But the overlays are full sprites of the whole building. A full sprite drawn with that shader would make the whole elevator look lit. This needs **lights-only overlays**: same canvas, transparent except the lamp pixels. That's the existing `plan.md` §7 option. The expected files would be:
- `DeepElevator_GreenLight`, `DeepElevator_RedLight` (256x256)
- `DeepElevator_Large_GreenLight`, `DeepElevator_Large_RedLight` (512x512)
- optionally `DeepElevatorLoader_OrangeLight` (256x256)

The code change is small: the overlay loader takes the shader, and the def extension takes the new paths. This is independent of the glow and can be combined with any option above.

### 2.5 Recommendation

A or C. A costs only XML and gives the lamp. C adds the status colour with small code and still no new assets. Add D only if players should be able to switch it off before the settings step exists. §2.4 is separate polish that needs four new PNGs.

## 3. Decisions needed

1. Which glow option (A–D, or none), and whether to add §2.4's night-visible lights.
2. Icon location: keep the current folder, or move gizmo icons to `UI/Commands/`.
3. Loader "Unload all": its own icon, or reuse the elevator's Unload icon.
4. Whether to replace the vanilla "Cancel loading" icon (optional file).
5. Renaming `DeepElevator_Large_Orage.png` to `..._Orange.png`, or deleting the unused orange overlays.
