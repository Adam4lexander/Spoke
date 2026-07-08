# Base Defence

A complete RTS-lite / Tower Defence game.

It was built to show how a game looks when it goes all-in on Spoke's idioms, using `SpokeBehaviour` (or `SpokeSingleton`) in place of `MonoBehaviour` throughout.

---

## Game Mechanics

The player starts with a **core** building that seeds their power grid.

Resource sites are scattered across the map, and are harvested while they overlap the power grid. The game is won once all their resources have been collected.

Over time, escalating waves of flying-bug enemies will assault the player's base. The game is lost if the **core** building is destroyed, so turtling for too long is not an option.

There are four buildings the player can place:

- **relay**: extends the base's power grid
- **radar**: tracks nearby enemies for turrets to attack
- **turret**: fires at nearby enemies that are tracked by a radar
- **repair**: repairs nearby buildings

Every building must be connected to the power grid to function, tracing a path through the relays back to the **core**. If a relay is destroyed, it can cause an entire branch of buildings to lose power.

Under the hood it's all overlapping circles: buildings power up inside a relay's circle, enemies are tracked inside a radar's, and turrets fire on tracked enemies inside their own.

---

## Reading the code

`GameState` is the central hub, and a good place to start. The main folders are:

- `Units/`: buildings and enemies
- `UI/`: the sidebar, hover info, and board overlays
- `Controls/`: camera and input
- `Spatial/`: circle geometry and collision detection
