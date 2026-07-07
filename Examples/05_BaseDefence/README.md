# Base Defence

**Base Defence** is a complete game to demonstrate how Spoke can be used in real projects.

It's an RTS-lite / tower defence hybrid, where the player gathers resources, and places buildings to expand their base and defend it from waves of enemies.

The game leans heavily on Spoke, exclusively using `SpokeBehaviour` and `SpokeSingleton`.

It demonstrates most of the patterns I've used in Spoke.

---

## Game Mechanics

The player starts with a **core** building that seeds their power grid and must be defended.

The map has resource sites scattered around to be mined. And is assaulted by increasingly strong waves of flying-bug enemies. The player wins if they can harvest all of the resources on the map, before they are overwhelmed.

The player affects the game by placing buildings:

- **relay**: Extends the base's power grid
- **radar**: Tracks nearby enemies for turrets to attack
- **turret**: Fires nearby at enemies that are tracked by radar
- **repair**: Repairs nearby buildings

All buildings must be connected to the power grid, and trace a path through relays to the core in order to function. If a relay is knocked out, it will trigger a cascade of power loss to its now-disconnected branch.

Abstractly, it's a game of overlapping circles. Buildings become powered when they overlap a relays circle of influence. Enemies become _tracked_ when they overlap the radars circle. Turrets shoot at enemies who are both tracked and overlap their circle.

---

## Architecture

---

### GameState

---

### CollisionWorld

---

### Pool
