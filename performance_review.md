# Cargo Elevator – performance review

State: branch `stepH`, after the Step H settings work (verbose logging, glow and blink switches; the profiler and the hitch detector removed). Basis: our source, and the decompiled RimWorld 1.6 code for the vanilla calls we trigger. This is a code review. Nothing was measured in game.

**Summary.** None of our code does heavy work per tick or per frame in normal play. Two things grow with the number of buildings and are worth knowing:

1. **Verbose logging**, when switched on: every transfer writes a line, and each line captures a stack trace. It's off by default.
2. **The loader's blink**: each waiting loader re-registers its light twice a second, and vanilla rebuilds the light and two mesh layers around it each time. It can now be turned off in the settings.

Everything else is small, bounded by the buffer size (4 or 8 slots) and the ring of 8 or 12 output cells, or only runs while the player has one building selected.

## 1. Ranked list

| # | Code | Runs | Cost | Grows with | Verdict |
| --- | --- | --- | --- | --- | --- |
| 1 | `ElevatorUtil.Log` → `Verse.Log.Message` | with **verbose logging on**: every receive, absorb, delivery, drop, hold, power, glow and status change | **highest when on**: each call runs `StackTraceUtility.ExtractStackTrace()` (checked in `Log.Message`), queues the line in the in-game log and writes it to `Player.log`. A busy large pair writes several lines a second | busy elevators | **Fixed in Step H**: off by default (§2.1) |
| 2 | Loader blink: `Building_TransportLoader.Tick` → `UpdateGlow` → `CompGlower.UpdateLit` | per **waiting** loader (items inside, no order), 2 times a second of real time | each step registers or removes the light. `GlowGrid.RegisterGlower`/`DeRegisterGlower` marks every cell in the light's rectangle dirty (radius 3, about 50 cells). Each dirty cell marks the `Roofs` and `GroundGlow` mesh layers of its section dirty and raises `Notify_GlowChanged`. The next `GlowGridUpdate_First` recomputes the light (Burst job) and the dirty cells (Burst, over all lights). Those 1–4 sections' two layers are re-meshed on the main thread | number of waiting loaders | fine for a few loaders; **untested with many**. Offsets per loader spread the steps over half a second. **New setting** "Transport loaders blink while items wait": off = steady light, no re-registration while waiting (§2.3). "Loaders light up their surroundings" off removes the light entirely |
| 3 | `TrySend` → `TryDeliverEntry` → `OutputCells`, `BuildDeliveryTargets`, LINQ | per send attempt while items are buffered: 1/s (small) or 4/s (large), or every 30 ticks while not operational (cheap path: link and power checks only) | per considered entry: a new output-cell list (8 or 12 cells), one `GetSlotGroup` per cell, `Accepts` per distinct storage, `StorageCellRoom` per cell until the piece fits, two `ToList` and an `OrderByDescending`. With **FIFO off and everything held**, this runs once per buffered entry per retry (up to 8 on the large) | buffered entries × attempts | small. The target-description string (pre_stepH §2 item 3) is now built only with verbose logging on (§2.2). Possible later: compute `OutputCells(partner)` once per `TrySend` instead of once per entry |
| 4 | `PullFromLoaders` | every tick, per elevator, while operational with linked loaders | exits at once with no loaders or not operational. Otherwise, per linked loader with transfer on and per container entry, `RoomFor` (= `UsedSlots` plus one buffer scan, ≤ 8 entries). With a full buffer the same "no room" answer is recomputed every tick per waiting entry | loader entries × buffer entries | negligible at these sizes. Could skip while nothing changed, not worth the extra state |
| 5 | Harmony postfix on `StoreUtility.IsGoodStoreCell` | per cell checked by **any** haul search on **any** map, also maps without elevators | when vanilla said yes: one `haulDestinationManager.SlotGroupParentAt(c)` (a 3-D array read) and a type check. Only on elevator cells: `AcceptsPawnDelivery` → `GetRefusalReason` (link, power, the storage filter, a merge scan and `UsedSlots`, both ≤ 8 entries) | haul activity | negligible |
| 6 | `Accepts` (game's haul job check, VFE conveyors, our own deliveries into adjacent elevators) | per offered item | `GetRefusalReason` as above. A refusal is logged only when it differs from the last one | conveyor throughput | negligible |
| 7 | Elevator `GetGizmos` and `GetInspectString` | every frame **while one elevator is selected** | gizmos: `BuildCopyCommandUtility.FindAllowedDesignator` (vanilla's own lookup; it scans the architect designators), and for an unlinked elevator `UnconnectedCandidates` (one cell per other level, then `OrderBy` with the reflection-based level sort key). Inspect: level labels via DeepRim/MultiFloors reflection (members cached, labels not), translations, `DescribePausedTransfer` with LINQ | number of levels | fine: one building, only while selected |
| 8 | Loader `GetGizmos` / `GetInspectString` | every frame while one loader is selected | `Candidates()` (4 cells) when unlinked; `DescribeStatus` → `ElevatorCannotAcceptNow` → `CanPullFrom` (container × buffer, both small) | – | fine |
| 9 | `DrawAt` (elevator, loader) | every frame per building on screen | elevator: `Status` (fields, the partner's `PowerOn`, `IsBlocked`), then one `Graphic.Draw` of a cached overlay when lit. Loader: `ShowsOrangeLook` (fields) and one draw. No allocation, no mesh rebuild | buildings on screen | negligible. 100 elevators on screen = 100 small draw calls, like any realtime-drawn vanilla building |
| 10 | Place workers (`PlaceWorker_Elevator`, `PlaceWorker_TransportLoader`) | every frame **while placing** our buildings | cached; rebuilt every 60 frames (elevator: scans the other levels' elevators of that def; loader: this map's elevators). Per frame `GenDraw.DrawFieldEdges` and a dictionary lookup for the cursor text. `targetCells.Contains` is a linear search during the rebuild (fine for tens of elevators) | elevators on other levels | measured fine in Step B (Designator Shapes case); the profiler that measured it is removed |
| 11 | Periodic blocks | elevator every 30 ticks: `UpdatePowerDraw` (writes `PowerOutput` only on change), `LogStatusIfChanged` (computes `Status`; logs only if verbose), `UpdateGlow` (re-registers only on a light-state change). Every 250 ticks: partner validation (`Find.Maps.Contains`) and the intake backstop (1 or 4 cells). Loader every 30 ticks: `UpdateGlow`; every 250: order pruning, partner validation (4 cells) | hash-spread, so not all in one tick | negligible |
| 12 | Harmony postfixes on `JobGiver_ExitMap.TryGiveJob`, `RCellFinder.TryFindExitPortal`, `JobGiver_EnterPortal.TryGiveJob` | per call by the pawn AI (exit and portal jobs, mostly on pocket maps) | an early return unless vanilla chose a loader; then a scan of the map's portals (a few) | – | negligible |
| 13 | Buffered items ticking | every tick per held item, by vanilla (`Thing.DoTick` → `ThingOwner.DoTick`) because the elevator and the loader's holder are thing holders | the same as items in a vanilla transporter or shuttle: rot and deterioration run on the item's own ticker interval | items buffered | vanilla cost, by design (food keeps rotting) |
| 14 | Mod settings applied (`CargoElevatorSettings.ApplyToDefs`) | at startup and when the settings window closes | writes a few def fields, then one pass over the elevators and loaders on every map (`ThingsOfDef`, indexed) | buildings | one-off |
| 15 | Edge and sun shadows (Step H, task 7) | baked into the map section meshes | `SectionLayer_EdgeShadows` / `SectionLayer_SunShadows` add a few vertices per cell when the section is built | – | no per-frame cost |

## 2. Changes made in Step H

### 2.1 Verbose logging (item 1)

`ElevatorUtil.Log` writes only when the setting "Verbose logging" is on (default **off**); `LogWarn` always writes. All existing log lines are kept.

What remains when it's off: the call sites still build their interpolated message before `Log` sees the flag (C# evaluates arguments first). On the busiest paths that means a few `string.Format`-like operations and `LabelCapNoCount` lookups per transfer: `Received`, `Absorbed`, `Delivered`/`Dropped`, `Power`, `Glow`. That costs microseconds per second, far below the stack-trace cost that is gone. If it ever matters, wrap those calls in `if (ElevatorUtil.Verbose)`, as `LogTargetsIfChanged` now does.

### 2.2 Send path (item 3)

`LogTargetsIfChanged` returns at once when verbose logging is off, so the target-description string (a LINQ `Select` plus `string.Join` per attempt) is no longer built just to be compared.

### 2.3 Loader blink and glow (item 2)

- "Transport loaders blink while items wait" (default on). Off: the loader's lamp and light stay on steadily while items wait. The light is registered once when items arrive and removed once when the loader empties. No re-registration twice a second.
- "Transport loaders light up their surroundings" (default on). Off: no light at all; the orange lamp on the sprite still shows the state.
- The same switch for elevators. Their light changes only on a state change, so it isn't a performance item, just a look option.
- **Even glow** for the large elevator (added later in Step H, `results_stepH.md` §14–§16; default on). It replaces the large elevator's single light with four (one per cell, radius 5). Nothing runs per tick. On each light change (green, red, off), the game now recomputes four small lights instead of one, and marks their cells dirty. That happens at most every 30 ticks per elevator, and only when the state changes, so it isn't a hot path. "Large elevators light up evenly" off goes back to the single light.

### 2.4 Removed (pre_stepH §2 item 9)

`ElevatorProfiler` (stopwatches in the place workers) and `MapComponent_ElevatorProbe` (a per-frame hitch detector, and a map-removal log line). `results_stepH.md` §3 has the details.

## 3. Check of `pre_stepH.md` §2

| pre_stepH item | Holds? | Note |
| --- | --- | --- |
| 1 Logging is the largest steady cost | yes | stack trace confirmed in `Log.Message`; now gated (§2.1) |
| 2 Loader blink re-floods the light and rebuilds glow and roof sections | yes | `DirtyCell` marks `Roofs` and `GroundGlow` dirty per cell; the combine job covers only dirty cells, but loops over all lights per dirty cell. Now optional (§2.3) |
| 3 Send attempt builds targets plus a description string every time | yes | string now verbose-only; the allocations of the target lists stay (small) |
| 4 `PullFromLoaders` per tick, `RoomFor` recomputed | yes | as described |
| 5 Inspect string only while selected | yes | `GetGizmos` has the same pattern (not listed there; item 7 here) |
| 6 `DrawAt` negligible | yes | |
| 7 Store-cell postfix negligible | yes | array lookup confirmed (`HaulDestinationManager.SlotGroupAt` → `groupGrid[x, y, z]`) |
| 8 Placement helper cache every 60 frames | yes | |
| 9 Probes | removed | the place workers' profiler scopes and the map component; `ElevatorDiagnostics` stays (startup only, verbose only) |
| 10 30-tick and 250-tick blocks negligible | yes | |
| (not listed) | – | exit/enter job patches (item 12), buffered item ticking (item 13), settings apply (item 14), shadows (item 15): all negligible |

## 4. Worth testing in game

- Many waiting loaders (10+), blinking on versus off, with a performance mod (for example Dubs Performance Analyzer) open on `GlowGrid` and `MapDrawer`.
- A long busy run with verbose logging on versus off (TPS at speed 3 with two large pairs moving full stacks).
- Neither is expected to show a problem at normal colony sizes.
