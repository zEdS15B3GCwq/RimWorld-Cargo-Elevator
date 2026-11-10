# Cargo Elevator

A RimWorld 1.6 mod featuring elevators that move items, colonists and animals between floors: the surface, DeepRim layers and MultiFloors levels. It can be part of an automated transport system, similar to a conveyor belt.

![Cargo Elevator](CargoElevator/About/Preview.png)

## Changelog

- 0.1 - Initial version: all planned functionality included, tested on my own.

## Disclaimer

This mod was mostly implemented by an AI coding agent, as I (human) didn't have the time to do so. I also used Nano Banana to draft building images and icons as I have no drawing skills. I made all design decisions, discussed and controlled implementation details, and thoroughly tested the results.

## Why this mod

Cargo Elevator brings conveyor-style automation to multi-level colonies. Items put into an elevator on one level come out at its partner on another level, no pawn has to carry them across.

The level mods already offer some transfer capability, but neither covers automation well:

- **DeepRim** has a simple automatic transfer from one floor to another. It doesn't support transporting a specified amount of items, and it drops the items on random cells around the shaft. Pawns must be ordered to stand on the shaft and manually sent to another level. It's also limited to one shaft per floor.
- **MultiFloors** has a more complex system, but it doesn't work together with automation such as conveyor belts.

Cargo Elevator can receive items from pawns or VFE conveyor belts, and put items into storage, onto conveyor
belts or drop them on the floor, depending on its configuration.

## Features

- **Two elevator sizes**, linked in pairs, one on each level:
  - **Cargo elevator** (1x1): 1 intake cell, delivers to the 8 cells around it, sends one stack per second, buffer of up to 4 stacks.
  - **Large cargo elevator** (2x2): 4 intake cells, delivers to the 12 cells around it, four times as fast, buffer of up to 8 stacks.
- **Works like storage on the sending side.** Colonists haul into an elevator like into a shelf, using its storage filter and priority. Conveyor belts and other mods can feed it too.
- **Delivers into storage on the receiving side.** Items go into stockpiles, shelves or hoppers next to the partner elevator, highest priority first. If none accepts them, they are put on free floor next to it (optional). Partial deliveries are fine; whatever doesn't fit waits in the buffer.
- **Transport orders:** Building a **loader** next to an elevator enables transport orders (using the portal
UI), where you choose items, people and animals to send. Items ride the elevator; people and animals step out at the other end.
- **Per-elevator options:** buffer size, first in first out, merge stacks, drop on floor, allow insert (whether colonists may haul into it), rename.
- **Status indicators:**
  - a green lamp while it works and a red lamp while it's blocked, bright even at night;
  - a status line that says why transfer is paused (storage full, no storage, not connected, no power, ...);
  - a soft green or red glow that lights dark underground rooms a little (optional);
  - an orange light on the loader lights up when a transport order is in progress; blinks when the order
  completed and items still waiting in the loader's storage.
- **Consumes power on both ends:** more while moving items, little while idle.
- **Mod settings:** transfer speed, buffer size and power for each size, the lights, and verbose logging for bug reports.

## Requirements and installation

**Required:** [Harmony](https://github.com/pardeike/HarmonyRimWorld/releases/latest) (`brrainz.harmony`) mod.

**Optional, in any combination:** DeepRim, MultiFloors, Vanilla Furniture Expanded - Factory (conveyors), Better Architect Menu.

**Install:**

1. Download this repository (Code > Download ZIP) or a release.
2. Copy the `CargoElevator` folder (the one that contains `About` and `Defs`) into your RimWorld `Mods` folder, for example `RimWorld/Mods/CargoElevator`. The compiled DLL is included in `CargoElevator/1.6/Assemblies`.
3. Start the game, open **Mods**, enable Harmony and Cargo Elevator, and restart when asked.

**Load order:** Harmony first. Cargo Elevator loads after Harmony, DeepRim and MultiFloors; the game's **Auto-sort** button sets this for you. The order relative to Better Architect Menu shouldn't matter.

**Saves:** you can add the mod to an existing save. To remove it, deconstruct all elevators and loaders first and empty them; items in their buffers are lost with the mod.

## How to use

### 1. Research and find the buildings

Research **cargo elevators** (Industrial, after Electricity). The buildings appear in their own **Cargo elevator** tab in the architect menu. With Better Architect Menu they're under **Structure > Elevation** instead, next to MultiFloors' elevators if it's installed.

### 2. Build a pair of elevators

1. Build an elevator on one level.
2. On another level, build an elevator of the same size **at the same position measured from the centre of the map**. On maps of the same size, this is simply the same cell. To make these locations easy to find, while you place an elevator, the cells that line up with an unconnected elevator on another level are highlighted in green. The text under the cursor shows which level it will connect to. Place the second elevator on a green spot.
3. They link by themselves. If several unconnected elevators match (on different levels), use **Connect to...** to pick one. The link can't be changed later; to re-link, deconstruct an elevator and build it again.
4. Ensure that both ends are powered. Transfer pauses whenever either end has no power.

Elevators need heavy ground: not marsh, mud, soft sand, shallow water or bridges. An elevator can be built before the other level exists; it stays "disconnected" until a partner appears.

### 3. Send items

- **Storage filters** define what an elevator can **send**. Colonists will treat the elevator like normal storage and haul items to it. Nothing is allowed by default, so colonists won't haul anything into a new elevator.
- Raise its storage priority if colonists should prefer it over other storage.
- Or feed it with conveyor belts (VFE Factory).
- Turn off **Allow insert** if only belts or other mods should feed it, not colonists.
- Items that enter the elevator disappear into its buffer and are sent one stack at a time.

At the other end, put storage next to the partner elevator: a stockpile, a shelf or a hopper. Only the cells touching the partner are used. A large stockpile counts only where it touches the elevator.

When no storage next to the partner accepts an item, the receiving end can put items on a free adjacent cell if **Drop on floor** is on (default).

When the receving end has no free storage to place items into, and it isn't allowed to drop items on the floor or there aren't empty adjacent cells, then items wait in the buffer until there's room to resume transfer. This *blocked* state is indicated by a red light on the sending end.

Both ends of a pair can send; each end's own filter decides what it sends, so ore up and food down works on one pair.

**Don't let an end's filter allow what it receives**, or the items get hauled back in and bounce in an endless loop. Since elevators don't pick up items from the floor next to them, this endless loop won't happen when the items are placed on the floor.

### 4. Send a specified amount of items, pawns and animals

1. Select an elevator and use its transport loader build button, or build a loader from the architect menu right next to an elevator. It links to that elevator by itself (use **Connect to...** if several elevators touch it).
2. Select the loader and use **Open transport menu**. Choose items, people and animals as in any portal window.
3. Colonists haul the chosen items into the loader. The elevator takes them from the loader into its buffer and sends them along with its normal traffic. People and animals walk into the loader and arrive next to the partner elevator.

The loader's orange lamp is on while an order is in progress, and blinks while items are still waiting in it after the order is complete. **Transfer items to elevator** can be turned off to hold items in the loader. The transfer option does not govern pawns and animals, which effectively *teleport* to the destination. When several loaders touch one elevator, their **priority** decides which one it empties first. The loader needs no power, but the elevator pair must be connected and powered for anything to go through.

When a loader's orange light is blinking to indicate that items are still waiting in its storage to be transferred, ensure that the **Transfer items to elevator** is enabled, and that the elevator is capable of transferring the items (it's paired, powered and not blocked).

Loaders can be uninstalled (minified) and reinstalled. Uninstalling a loader will drop items in storage. A reinstalled loader automatically links to an adjacent elevator when possible, or use **Connect to...** in ambiguous cases.

When a loader loses its linked elevator (it's deconstructed, for example), the **Transfer items to elevator** option will automatically turn off and will stay turned off even if a new elevator is linked to. This is intended to prevent sending old transport orders to the wrong destination. Keep in mind that the transfer option only affects items, it does not stop pawns and animals from being transported.

### 5. Controls

#### Elevator

| Control | What it does |
| --- | --- |
| Fewer slots / More slots | Buffer size; one slot holds one full stack. |
| First in, first out | On: items are sent strictly in order (similar to a conveyor belt), so a stuck first item holds up the rest. Off: the first item that can be delivered is sent. |
| Merge stacks | On: arriving items join a buffered stack of the same kind. Off: each arrival takes its own slot (like on a conveyor belt). |
| Drop on floor near target | On: items no storage accepts are put on the floor next to the partner. Off: they wait. |
| Allow insert | Off: colonists stop hauling into it. Conveyors, other mods and the loaders still work. |
| Unload | Empties the internal buffer by placing all contained items on nearby empty cells. Works while disconnected. |
| Go to partner | Jumps to the linked elevator. |
| Rename | Gives the elevator a name, shown in menus and messages. |

The Contents tab lists the buffer and lets you drop single entries.

#### Loader

| Control | What it does |
| --- | --- |
| Open transport menu | Opens a menu to specify items, pawns and animals to transfer through the linked elevator, or to change orders already in progress. |
| Transfer items to elevator | Items from the loader are transferred to the linked elevator as quickly as the elevator can take them. Disable to keep items in the loader. |
| Unload | Empties the internal buffer by placing all contained items on nearby empty cells. |
| Cancel loading | Cancel the transport order. Items already transferred to the elevator are not returned. |
| Priority | Sets priority from low to critical. Elevators get items from higher priority loaders first. |

### 6. Mod settings

Options > Mod settings > **Cargo Elevator**. Changes apply when the window is closed, also to elevators and loaders already built.

- For each elevator size: ticks between sent stacks (speed), maximum buffer slots, power while transferring and while idle.
- Lights: whether elevators and loaders light up their surroundings, whether the large elevator uses its even (centred) light, whether loaders blink.
- Verbose logging: off by default. Turn it on to write every transfer, link and state change to the log, for bug reports.
- Reset to defaults.

## Supported Mods

- **DeepRim:** elevators work between the surface and any layer, and between layers. If you set DeepRim's layer size smaller than the surface map, DeepRim will proportionally shrink coordinates, meaning that its deep shaft will be placed closer to the centre of the map than on the surface. Cargo elevator does not follow this pattern, it uses the smaller maps actual size, cell for cell, with all maps sharing the same centre. Elevators outside the smaller map's area can't connect to it, and an elevator's position relative to the DeepRim shaft may differ between levels. When placing elevators, map contours and hints indicate that a certain location is outside a smaller map's area. Watch out: DeepRim's shaft pulls items out of storage next to it. Keep the elevators' output storage away from the shaft unless you want that.
- **MultiFloors:** elevators work between any levels of a stack. Level names in menus and messages follow MultiFloors' symbols, for example "B1 (MultiFloors)".
- **DeepRim and MultiFloors together:** all levels on one world tile can connect to each other, including MultiFloors levels opened from a DeepRim level. For example: "70m (DeepRim), B1 (MultiFloors)".
- **Vanilla Furniture Expanded - Factory:** conveyor belts can push into an elevator, and an elevator delivers into hoppers and other storage next to it.
- **Better Architect Menu:** the buildings move into Structure > Elevation, and the separate Cargo elevator tab is removed. Load order shouldn't matter.
- **Other level mods:** in principle, maps that other mods place on the same world tile should work the same way, but only DeepRim and MultiFloors have been tested to work.

There is no compile-time reference to any of these mods; each one is optional.

## Known limitations

- Each elevator links to exactly one partner of the same size. The link is fixed once made.
- Pocket maps (for example the Anomaly undercave) are meant to pair with the map they were opened from, but this is untested.
- Preventing a transport loader to be used as the way out of a pocket map is untested. The game might treat a loader as an exit since it's implemented as a *portal*; a patch is in place to prevent this from happening.
- In the transport menu, removing one colonist from an order of several may not stop that colonist from walking in. This is the vanilla portal window's behaviour.

## Generative AI use

This mod was designed and built with Claude Opus 5.5 (Anthropic) as the main engineer, working as a planner. Claude Sonnet 5.5 acted as the agent for mosts implementation steps. Claude did the following work:

- **Reverse-engineering:** worked out implementation details from the decompiled RimWorld 1.6 code and the DeepRim, MultiFloors and VFE code.
- **Planning:** drew up the specification for each implementation step, and kept them up to date with the test results.
- **Implementation:** wrote all the C# and XML code.
- **Documentation:** drafted this README, the mod description; maintained the project's plan and step notes.

**Art:** initial versions of building images and icons were created with Nano Banana 2 (Google).
