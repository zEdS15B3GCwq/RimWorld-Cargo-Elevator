# CargoElevator – rate test (Step D)

Temporary measurement after Step D. The probe code lives on a git snapshot and is restored away afterwards (human). Raw log excerpts: `rate_test_small.log` (1x1, `CargoElevator_Elevator`), `rate_test_large.log` (2x2, `CargoElevator_ElevatorLarge`).

## 1. Goal
Confirm the configured send rates in the game: 1 piece/s for the 1x1 (`ticksPerStack` 60), 4 pieces/s for the large one (15 ticks). Find out how fast pawns can deliver into the intake cells, in total and per cell, and whether the large elevator's 4 intake cells really allow parallel deliveries.

## 2. What was measured
Proposed by the human: the shortest time between events, the peak events/s over 1 s, and the average counts/s over 5 s, all reported every 5 s. The planner added two values: items per window, because pieces range from a few items to a full stack, and the highest buffer fill, which shows whether the buffer or the send rate is the limit.

| Value | Definition |
| --- | --- |
| absorbed (total, per intake cell) | one event per item stack moved from an intake cell into the buffer (event or backstop path) |
| sent | one event per successful send: one piece, at most `def.stackLimit`, possibly spread over several cells. Items = drop in buffer items across the send |
| avg/s | events in the window / 5 |
| peak/s | most events in any sliding 60-tick window during the report window |
| min gap | shortest time between two consecutive events, in ticks; may span two windows; "-" = fewer than two events so far |
| buffer max / now | highest used slots during the window (sampled at each absorb) and at report time |

All times are game ticks (60 = 1 game second), so the results don't depend on game speed. Reports come every 300 ticks at the elevator's hash phase, and only for windows with activity.

## 3. Implementation
- `Building_Elevator.RateProbe.cs` (partial class, nothing saved): a `RateCounter` (window count and items, sliding 1 s queue for the peak, last event tick for the gap) for the total intake, one per intake cell, and one for sending.
- Hooks, each marked `// rate probe`:
  - `Tick` calls `RateProbeTick`.
  - `AbsorbIntake` captures the cell and the stack count before `DeSpawn` and records them after `AddToBuffer`.
  - `TrySend` counts buffer items before and after a successful send.
- Log format:
  ```text
  Rate (5 s): <elevator> absorbed: N stacks (M items), avg x/s, peak p/s, min gap g ticks
      cell <c>: …
      sent: N pieces (M items), …
      buffer max a/b slots, now c/b
  ```

## 4. Test setup
In-game hauling by several pawns into a connected, powered elevator; the small and large elevators were tested separately. The exact items and settings were not recorded. From the logs: mostly bulk items in pawn loads of 25–75 items, and merging was on, since sent pieces were larger than absorbed loads.

## 5. Results

### 1x1 (`rate_test_small.log`, 8 windows)
- **Sending:** at most 1 piece/s. Min gap is always ≥ 60 ticks, peak 1/s, and the best window averages 1.00/s. As configured.
- **Intake:** up to 1.80 stacks/s on average (9 stacks in 5 s), peak 2/s, **min gap 31 ticks** on the single cell.
- **Intake faster than sending in stacks, yet the buffer never went over 1/4 slots.** With merge on, the 75-item pawn loads merge into entries up to the stack limit, so one piece carries more than one load. Example: absorbed 9 stacks (675 items) and sent 5 pieces (750 items) in the same window. Item throughput is therefore set by the stack limit: about one full stack per second, here up to 150 items/s.

### 2x2 (`rate_test_large.log`, 21 windows)
- **Sending:** at most 4 pieces/s. Min gap 15 ticks whenever there was a queue, peak 4/s, and the best window averages 4.00/s (20 pieces, 1716 items). As configured.
- **Intake in total:** up to 5.60 stacks/s on average (28 stacks, 1641 items), peak 7/s, min gap 0–3 ticks. Several cells receive items in the same or adjacent ticks, so **the 4 cells are used in parallel**.
- **Intake per cell:** at most 2/s, with the **same 31-tick minimum gap** as on the 1x1. All 4 cells were used. How evenly they were used depended on the haulers' paths (often one or two cells carried most of the load).
- **Buffer:** at most 3/8 slots. It grew only while intake (5.6/s) exceeded sending (4/s). The buffer was never the limit.

### Conclusions
- The configured send rates are met exactly (1/s and 4/s).
- Intake per cell is limited to about one stack per 31 ticks (~2/s). This limit comes from the pawns: cell reservation, the drop toil and the next hauler's arrival. The elevator absorbs on the next tick and is not the limit. The large elevator's advantage comes from its 4 cells; intake well above 4 stacks/s was measured.
- Throughput in items depends on the item's stack limit and on merging: with merge on, small pawn loads are combined into full pieces before sending.
- The buffer never filled (at most 1/4 and 3/8 slots), so no buffer-full holds limited the test.
