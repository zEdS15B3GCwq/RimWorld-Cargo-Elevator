# Transport manifest vs MultiFloors' transfer UI

Scope: only how the player picks things to send and how they reach the elevator. Elevator storage, sending, delivery and power are the same under every option and aren't compared.

Basis: decompiled RimWorld 1.6 and MultiFloors 1.6.1.3, plus our `stepE` branch. Nothing was run in game. Effort figures are my estimates.

## 1. Recommendation

**Decided (human): option B, the portal loader, specified in `plan_stepE2.md`.** Items ride the elevator; pawns and animals use vanilla portal arrival, accepted for simplicity. The vanilla dialog is used as it is. The rest of this section is the earlier reasoning, kept for reference.

**Earlier recommendation: stay on the transport manifest (option A).** This replaced the first recommendation of option B.

Design rule: everything goes in the elevator, takes time to arrive and is delivered as if it rode the elevator. Nothing teleports. Pawns therefore can't skip the buffer, slots and rate.

Option B's main advantage was vanilla pawn arrival, and that is exactly the teleport the rule forbids. Under the rule, B has to build pawn sending just as A does, on top of a workaround for walking pawns, and it adds the portal side effects in §5. A's remaining cost is the pod wording and extra columns.

Where sections below describe pawns arriving directly at the partner, or call B's E2 small, they describe vanilla portal behaviour that the rule now rejects.

## 2. The options

**A. Transport manifest (current E1).** The loader has vanilla `CompTransporter` and `Dialog_LoadTransporters`.

**B. Portal loader (recommended).** The loader subclasses vanilla `MapPortal` and uses `Dialog_EnterPortal`, the same base and dialog MultiFloors' stairs use. "Trade UI" here means this dialog. It's vanilla, and MultiFloors adds no UI code to it.

**C. Own dialog.** Our own dialog on the vanilla list widget, plus our own order list, `WorkGiver` and `JobDriver`. This is option D in `plan.md`. It's the only option that can put the Load button on the elevator itself and need no loader building.

**Why B can't remove the loader:** `MapPortal` and `Building_Storage` are both classes, and a building can only inherit from one. The elevator has to stay `Building_Storage`.

All three use the same list widget (`TransferableOneWayWidget`), so the rows and the +/- controls look the same. The differences are around the list and behind it.

## 3. What differs in the UI

| | A. Manifest | B. Portal | C. Own |
| --- | --- | --- | --- |
| Pauses the game | Yes | Yes | Our choice |
| Caravan info bar | Off through `showOverallStats` | Doesn't exist | Doesn't exist |
| Item columns | Mass, market value, nutrition, days until rot | Mass only | Our choice |
| Pawn columns | Includes market value, nutrition eaten, foraged food | Mass, equipped weapon | Our choice |
| Pawns tab | Always | Always | Can be left out |
| Title | Pod or shuttle label | `EnterString` (virtual, so ours) | Ours |
| Mass limit | Limit from `massCapacity`, set very high | None | None |
| Empty list | Needs the def setting that allows it | Clears the order | Ours |
| Reopen during a load | Needs `canChangeAssignedThingsAfterStarting` | Always works | Ours |
| Status tab | Two columns: to load and inside | Order list only, so items waiting in the loader need our own tab | Ours |
| **Text the player sees** | "Transport pod loading complete. Launch when ready.", "...loaded into the transport pods.", "...because the shuttle has been destroyed." | Only the "can't finish entering the {0}" warning, with our label | Ours |
| Listed items on a non-pocket map | Outside the home area too when the map isn't a player home | Home area or storage only | Our choice |

**Rot days is the one useful column B loses.** The items list on maps such as DeepRim layers needs a test: if a layer counts as a player home, A and B behave the same. If not, B won't list loose items outside the home area, such as freshly mined ore, until they're hauled to storage or the area is added.

## 4. How well each supports our goals

| Goal | A. Manifest | B. Portal | C. Own |
| --- | --- | --- | --- |
| Pick chosen items and amounts | Yes | Yes | Yes |
| No launch or pod concepts | **Worked around:** hidden cancel button, finish check, pod wording stays | **Yes:** no launch concept exists | Yes |
| Manifest changes while running | Yes | Yes | Yes |
| Load ends by itself | Our per-tick finish check calling `CancelLoad` | Automatic: the list empties | Ours |
| Cancel | Vanilla button flickers, so we hide it and add our own | Vanilla clears the list; we add unload for items inside | Ours |
| Loaders feed elevator with priority | Yes (built) | Yes (same pull) | Pull not needed if deposited directly |
| Haulers wait when the elevator is full | No, loader absorbs | No, loader absorbs | **Yes, the only one that can** |
| Items only for now, no dead ends | Pawn stays stuck in the loader until cancelled | Pawns can work (§6) | Tab can be left out |
| **Colonists walk in themselves** | They enter the container; sending is still to build | **Vanilla, ending at the partner** | Own lord and duty: expensive |
| **Animals, downed pawns** | Carried into the container; sending is still to build | **Vanilla, carried to the portal** | Own job handling |
| Pawns respect buffer, slots and rate | Possible, costs extra work | No, they skip all three | Possible, costs extra work |
| No extra building | No | No | **Yes** |

## 5. Harmony and side effects

| | A. Manifest | B. Portal | C. Own |
| --- | --- | --- | --- |
| **Required patches** | 0 | 0 | 0 |
| Fix wording | 1 or more on `CompTransporter`. A language override would change real pods too | Not needed | Not needed |
| Hide columns or Pawns tab | 1 or more postfixes (private fields) | 1 or more postfixes, needed less | Not needed |
| Remove right-click "Enter" | Doesn't apply | **Optional 1 prefix**, as MultiFloors does; or keep the option | Doesn't apply |

B needs no patches because the parts we need are virtual or public: `EnterString`, `CancelEnterString`, `EnteringString`, `EnterTex`, `IsEnterable`, `GetOtherMap`, `GetDestinationLocation` and `OnEntered`. The `containerProxy` field can also be replaced with our own subclass.

**Side effects of B, which are its real cost:**
- **Right-click:** any selected pawn gets "Enter" on the loader. While enterable, that's a feature. When not enterable, the pawn sees a greyed-out reason. MultiFloors removes the option with a patch.
- **Exit logic on pocket maps:** `JobGiver_ExitMap` and `RCellFinder.TryFindExitPortal` treat every `MapPortal` on a pocket map as an exit, and MultiFloors levels are pocket maps. A pawn trying to leave could pick our loader, and with `IsEnterable` false that job fails. Vanilla's search doesn't check `IsEnterable` first. **This is the main thing to test.** If it misbehaves, it may need a patch, which would change the "0 patches" count.
- **Other mods:** any mod scanning `MapPortal` will see the loader. I didn't check this across the installed mods.
- **Rot and contents:** the portal's holder returns the empty proxy, so the loader must tick its own container (one line). Vanilla searches through holders may not see items waiting in the loader. They usually wait only briefly, so this is minor, but wealth counting is unchecked.

**Side effects of A:** none new. It's built, and it has the same fragile points already recorded: the vanilla button is matched by icon, and the pod messages remain.

## 6. Implementation effort

Estimates are for an implementing agent, starting from `stepE`.

| Work | A. Manifest | B. Portal | C. Own |
| --- | --- | --- | --- |
| **E1 code** | Done, untested | About 100–150 lines changed, mostly in the loader | About 400–500 new lines, plus defs |
| **New pieces** | None | Proxy (about 25 lines), small tab (about 40 lines) | Dialog, order list with saving, `FindThingToLoad` copy, `WorkGiver`, `JobDriver`, tab |
| **Removed** | Nothing | `CompTransporter` use, finish check, hidden-cancel code | Loader building, link code, pull, and its placement worker |
| **Elevator changes** | None | None | Deposit path, full check, Load gizmo |
| **E1 retesting** | §8 as written | Loader tests redone; elevator tests unchanged | Most loading and linking tests rewritten |
| **E2 pawns and animals** | **Medium, about 100–200 lines:** vanilla loads them into the loader. We add pawns to pull, slots and send, then spawn at the partner; lord clean-up; saving and vitals while inside | **Rejected as small (it teleports). Under the design rule: A's work, plus redirecting walking pawns into the loader** | **Largest:** own lord and duties, own carry jobs |

B's core is small:

```csharp
public class Building_TransportLoader : MapPortal
{
    private ThingOwner<Thing> container;
    // GetOtherMap, GetDestinationLocation: the partner elevator's partner.
    // IsEnterable: linked, and powered at both ends.
    // OnEntered: Notify_ThingAdded only (the base one needs vanilla portal props).
}

// Set in SpawnSetup after base: containerProxy = new LoaderContainerProxy { portal = this }.
public class LoaderContainerProxy : PortalContainerProxy
{
    public override bool TryAdd(Thing item, bool canMerge = true)
    {
        if (item is Pawn) return base.TryAdd(item, canMerge);   // carried pawns go to the partner
        portal.Notify_ThingAdded(item);                         // subtracts from the order list
        return loader.Container.TryAdd(item, canMerge);         // items wait for the pull, as now
    }
}
```

`PullFromLoaders`, the priority list, links, the transfer toggle, status lines and placement stay as they are.

## 7. Trade-offs in choosing B

**You give up:**
- Rot days and market value in the dialog.
- The ready-made two-column tab.
- Pawns going through the buffer, unless walking pawns are redirected. They'd arrive directly, with only the link and power checks, which the design rule in §1 forbids.

**You keep the limits of A:** haulers still don't wait when the elevator is full, so a large order piles up in the loader.

**Why not C:** it's the only option with hauler back-pressure and no extra building, but that's much more code, and vanilla's walk-in pawns would have to be rebuilt. It would also drop the multi-loader feeding and priorities you specified for E1. It's worth revisiting only if pawns are dropped from the goals.

**Why A:** E1's code is already written, and pawns can respect the buffer, slots and rate because they load into a real container. Its remaining problems are mismatches with pods: the wording, the columns, the cancel button and the finish check.

## 8. Unverified

- Exit logic on MultiFloors and vanilla pocket maps with a loader present (§5).
- Whether DeepRim layers count as player home (§3).
- Pawn arrival at the partner when its surroundings are blocked. Vanilla searches 5 cells for a standable spot and otherwise cancels with a message.
- Wealth counting of items waiting in the loader.
- The base deposit time. `JobDriver_HaulToPortal` sets 90 ticks per deposit, and I didn't compare this with the transporter driver.
