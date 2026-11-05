# CargoElevator – Step H results

Branch `stepH`, from `stepF` at `5c49417` (clean, so no commit was needed before branching). Step G (VFE checks) had no code; nothing was skipped in the code.

Task order (human): 6 (name change) first, then the human tests, then 1–5, 7, 8.

| # | Task | State |
| --- | --- | --- |
| 6 | Name change DeepElevator → CargoElevator | **done, builds; waiting for the in-game test** |
| 1 | Extant tasks of `pre_stepH.md` | pending |
| 2 | Remove repetitive logging | pending |
| 3 | Remove the profiler | pending |
| 4 | Performance review (`performance_review.md`) | pending |
| 5 | Own build menu category + BAM patch | pending |
| 7 | Useful building XML nodes from the examples | pending |
| 8 | Own research project | pending |

## 1. Name change (task 6)

**Name:** "Cargo Elevator" (`CargoElevator` in defs, code and file names). I kept it: it says what the building does without tying it to one level mod, and "cargo" fits since items ride it (pawns use the loader's portal). Alternatives I considered and rejected: "Level Lift" (vaguer), "Cargo Lift" (same meaning, "elevator" matches MultiFloors' and the vanilla wording). Before publishing, check the Workshop for an existing mod with the same name.

### 1.1 What changed

| Before | After |
| --- | --- |
| mod folder `DeepElevator/` | `CargoElevator/` |
| `DeepElevator.csproj`, `DeepElevator.slnx` | `CargoElevator.csproj`, `CargoElevator.slnx` (RootNamespace, AssemblyName, Product, OutputPath, Compile/None globs) |
| `DeepElevator.dll` | `CargoElevator.dll` (old DLL removed from git, new one built and committed) |
| About: name `DeepElevator`, packageId `FT.deepelevator` | name **`Cargo Elevator`** (with a space; it's the display name), packageId **`FT.cargoelevator`** |
| namespace `DeepElevator`, `DeepElevatorDefOf` | `CargoElevator`, `CargoElevatorDefOf` (file renamed too) |
| defs `DeepElevatorBase`, `DeepElevator_Elevator`, `DeepElevator_ElevatorLarge`, `DeepElevator_TransportLoader` | `CargoElevatorBase`, `CargoElevator_Elevator`, `CargoElevator_ElevatorLarge`, `CargoElevator_TransportLoader` |
| def files `DeepElevator_Elevator.xml`, `DeepElevator_TransportLoader.xml` | `CargoElevator_Elevator.xml`, `CargoElevator_TransportLoader.xml` |
| labels "deep elevator", "large deep elevator" (and the large one's description) | "cargo elevator", "large cargo elevator" |
| language file `Keyed/DeepElevator.xml`, keys `DeepElevator_*` | `Keyed/CargoElevator.xml`, keys `CargoElevator_*` (all uses in code renamed with them) |
| texture folder `Textures/Things/Buildings/DeepElevator/`, files `DeepElevator*.png` | `…/CargoElevator/`, files `CargoElevator*.png` (texPaths and `ElevatorTextures` icon names follow) |
| Harmony id `FT.deepelevator` | `FT.cargoelevator` |
| log prefix `[DeepElevator] ` | `[CargoElevator] ` |
| `.gitattributes` `DeepElevator/** text eol=crlf` | `CargoElevator/** text eol=crlf` |
| all `*.md` docs (plan, step plans, results, pre-step notes, `test_rates.md`) | renamed to match (human's exception for this task) |

**Extra fix found on the way:** the large elevator's sprite file was `DeepElevator_large.png` (lower-case l) while its def's `texPath` says `…_Large`. That only worked because Windows paths ignore case; on Linux or macOS the large elevator would likely have shown no texture. The file is now `CargoElevator_Large.png`, matching the def.

All texture paths named in defs and code were checked against the files after the rename (all present). `dotnet build CargoElevator.csproj`: 0 warnings, 0 errors.

### 1.2 Not renamed, on purpose

- **The repository folder** `C:\Code\Rimworld\DeepElevator`. It's this session's working folder (and the Claude memory folder is keyed by it), so renaming it mid-session would break things. Docs that name the repo root keep `C:\Code\Rimworld\DeepElevator` (`plan_stepA.md`, `plan_stepB.md`, `plan_stepD.md`). Rename it yourself between sessions if you want; nothing in the build depends on it.
- **`rate_test_large.log`, `rate_test_small.log`**: raw game output captured in Step D (`[DeepElevator]` prefixes). Left as recorded.
- `Examples/`, `include/` (not ours).
- An **empty** leftover folder `DeepElevator\Textures\Things\Buildings\DeepElevator\` in the repo could not be deleted ("device or resource busy": some program, probably Explorer or Visual Studio, has it open). It's empty and git ignores it; delete it by hand. The same lock first blocked `git mv` of the whole folder, so files were moved one by one (history is kept as renames).
- `.vs/` (Visual Studio's cache for the old `.slnx`): open `CargoElevator.slnx` instead; the old cache can be deleted.

### 1.3 For the in-game test

- The game loads the mod from a copy in `D:\G\RimWorld\Mods\_DeepElevator` (a plain folder, not a link). Copy `CargoElevator\` (About, Defs, Languages, Textures, 1.6, loadFolders.xml) to e.g. `D:\G\RimWorld\Mods\_CargoElevator` and **remove `_DeepElevator`**, otherwise both show in the mod list.
- The packageId changed, so the mod list sees a new mod: enable **Cargo Elevator** (after Harmony, and after DeepRim/MultiFloors if used).
- Def names changed, so elevators and loaders in old saves won't load. Test on a new colony or rebuild them.
- What to look for: the buildings in the architect menu (still `Misc`) with the new labels; textures for all three buildings, both elevator light overlays, the loader light, and the gizmo icons; no raw `CargoElevator_…` keys in the UI (a missing string shows the key); log lines start with `[CargoElevator]`; Harmony line at startup.

### 1.4 Commits

- `5c49417` (before; `stepF`)
- `1d01e96` H: rename DeepElevator to CargoElevator (`stepH`)
