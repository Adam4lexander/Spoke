# Base Defence

**Base Defence** is a complete RTS-lite / Tower Defence game.

It was built to show how a game looks when it goes all-in on Spoke's idioms, using `SpokeBehaviour` (or `SpokeSingleton`) in place of `MonoBehaviour` throughout.

---

## Game Mechanics

The player starts with a **core** building that seeds their power grid. New buildings may only be built on the power grid.

Resource sites are scattered across the map, and are harvested while they overlap the power grid. The game is won when all the resource sites are harvested.

Over time, waves of flying-bug enemies will assault the player's base. The game is lost if the **core** building is destroyed. Turtling for too long is not an option.

There are four buildings the player can place:

- **relay**: Extends the base's power grid
- **radar**: Tracks nearby enemies for turrets to attack
- **turret**: Fires at nearby enemies that are tracked by any radar
- **repair**: Repairs nearby buildings

Buildings only function when they are connected to the power grid, and trace a path through the relays to the **core**. If a relay is destroyed, it can cause an entire branch of buildings to lose power.

Under the hood it's all overlapping circles: buildings power up inside a relay's circle, enemies are tracked inside a radar's, and turrets fire on tracked enemies inside their own.

---

## Reading the code

`GameState` is the central hub, and a good place to start. The main folders are:

- `Units/`: buildings and enemies
- `UI/`: the sidebar, hover info, and board overlays
- `Controls/`: camera and input
- `Spatial/`: circle geometry and collision detection
