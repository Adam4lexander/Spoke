# Base Defence

**Base Defence** is a complete RTS-lite / Tower Defence game using Spoke.

It was built to demonstrate how Spoke can be used in real projects.

---

## Game Mechanics

The player starts with a **core** building that seeds their power grid. Buildings may only be built inside the power grid.

Resource sites are scattered around the map that are harvested when they are overlapping the power grid. The game is won when all of the resource sites are harvested.

Over time, waves of flying-bug enemies will assault the players base. The game is lost if the **core** building is destroyed. The player must harvest all resources before they are overwhelmed. Turtling for too long is not an option.

There are four buildings the player can place:

- **relay**: Extends the base's power grid
- **radar**: Tracks nearby enemies for turrets to attack
- **turret**: Fires at nearby enemies that are tracked by any radar
- **repair**: Repairs nearby buildings

Buildings only function when they are connected to the power grid, and trace a path through the relays to the **core**. If a relay is destroyed, it can cause an entire branch of buildings to lose power.

---

## Architecture

---

### GameState

---

### CollisionWorld

---

### Pool
