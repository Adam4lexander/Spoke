# Base Defence

A complete RTS-lite / tower defence game, built the way a game looks when it goes all in on Spoke.
Every node in it extends `SpokeNode2D`, `SpokeNode` or `SpokeControl`, and there is no `_Process`,
no `_Ready` and no manual show/hide anywhere in the folder.

This is a port of the Unity example of the same name. The rules and the numbers are the same; what
changed is everything the engine touches.

Open `05_BaseDefence.tscn` and press **F6**.

```
05_BaseDefence/
  05_BaseDefence.tscn     the level — the board, the camera, the UI, the Core, all 27 sites
  Art/                    15 SVGs, imported as textures
  Units/                  one scene per unit, carrying its stats as exported values
  Scripts/                the logic, and nothing else
```

Nothing about the starting state is built in C#. Open the root scene and the whole game is there to
click on, the same way `BaseDefence.unity` is in the original.

---

## The game

You start with a **Core** that seeds your power grid.

Resource sites are scattered across the map — three sit inside the Core's own coverage, so income
starts immediately; the rest you have to reach. Win by mining out all 27.

Escalating waves of flying enemies attack from one edge of the map. Lose the Core and the game is
over, so turtling isn't an option — and harvesting stops while a wave is in progress, so neither is
hiding.

Four buildings:

| | Does | Costs |
|---|---|---|
| **Relay** | extends the power grid by 3m | $10 |
| **Radar** | reveals enemies within 8m for turrets to shoot | $50 |
| **Turret** | fires at revealed enemies within 5m | $50 |
| **Repair** | mends the most damaged building within 5m | $100 |

Every building must trace a path through relays back to the Core to work. Destroy a relay and the
whole branch behind it goes dark.

Under the hood it's all overlapping circles: buildings power up inside a relay's circle, enemies are
revealed inside a radar's, turrets fire on revealed enemies inside their own.

**Controls** — WASD pans. `1`-`4` or the sidebar buttons select a building; click to place;
right-click or Escape cancels. Hover anything to see what it does and what it covers.

---

## Reading the code

`Scripts/GameState.cs` is the hub and the scene root; start there.

| Folder | |
|---|---|
| `Units/` | buildings, enemies, health, bodies |
| `UI/` | sidebar, overlays, hover, the drawing helper |
| `Controls/` | camera and input |
| `Spatial/` | circle geometry and collision detection |

Two files carry most of the load. `Units/PowerNode.cs` is the grid — each node draws from a parent
provider whose coverage it overlaps, and is powered only while that chain reaches the root. Nothing
in it polls, and nothing walks the grid on a timer; a relay dying drops one node's parent and the
loss propagates outward because each node's `HasPower` reads its parent's. `UI/BoardInteractions.cs`
is the other: hovering and placing, with every overlay a nested block, so what's on screen is
whatever the current situation calls for and there is no show/hide bookkeeping at all.

---

## The numbers are the original's

Every gameplay value is the one serialized in the Unity prefabs and scene — hit points, costs,
ranges, fire rates, wave budgets, unlock waves, spawn intervals, the lot. So are the 27 resource
site positions and the palette. A relay still costs $10 and reaches 3m; a turret still does 0.5
damage twice a second inside 5m; wave 1 still has a budget of 2 and the lull is still 30 seconds.

The one number that isn't from the original is in `Scripts/World.cs`:

```csharp
public const float PixelsPerMetre = 64f;
```

Unity's version is a 3D game measured in metres. This one is 2D and measured in pixels, and that
constant is the only place the two meet. Everything else — including every exported value in every
unit scene — stays in the original's metres, so `Radar.tscn` saying `Range = 8.0` can be read
straight against `RadarBuilding.prefab` saying `range: 8` without arithmetic.

The Unity ground plane is XZ and its +z points up the screen; Godot's 2D +y points down it, so the
resource layout is transcribed with `z` negated and nothing else changed.

---

## What changed in the port, and why

### It's 2D

The Unity version is 3D, but the gameplay was always flat — circles on the ground plane, viewed from
above. In Godot that's a 2D scene, which removes a surprising amount of code:

- **No ground plane or raycasts.** `View` in Unity casts four viewport corners and the cursor onto a
  `Plane` to work out what the camera sees and where the mouse points. In 2D the camera transform is
  already that mapping, so it's `GetGlobalMousePosition()` and the viewport rect.
- **No meshes.** `CoverageDisplay` and `LinkDisplay` in Unity each build a `GameObject`, a `Mesh` and
  a `Material` instance and fill vertex and index buffers by hand. Godot draws arcs and lines
  directly, so both reduce to "recompute the shapes, then `QueueRedraw`". The union-of-circles
  outline maths is unchanged; only the output changed.
- **No billboarding.** Half of Unity's `HealthBar` is turning the bar to face the camera.
- **The art is flat.** Unity's units are meshes with lit materials; here they're SVG sprites, drawn
  white so the unpowered tint stays a plain multiply. The 3D shatter throws pieces up and lets
  gravity land them — top-down 2D has no "up", so they slide outward against drag and fade.

### Components became child nodes

Unity attaches behaviour by composition: a building is one `GameObject` carrying `Building`,
`Health`, `MeshFX`, `HealthBar`, `PowerNode` and `Turret` side by side. Godot's unit of composition
is the node, so each component became a child node of the unit's root, and the root itself carries
no script — it's the `GameObject`:

```
TurretBuilding
  Health / FX / PowerNode / HealthBar / Building / Describes
```

References between them are `[Export]`, which is Godot's `[SerializeField]`: a node picker in the
inspector, stored in the `.tscn` as a `NodePath`. `Building` names its `Health`, `FX`, `HealthBar`
and `PowerNode`; `Turret` names its `Building` and the sprite it rotates. Nothing is looked up by
type or hunted for at runtime.

Two lookups can't be wired, because what they find is whatever a collider happened to hit — the
same two the Unity version answers with `GetComponent`. Godot has no equivalent, so they go by
node name instead: `GetNodeOrNull<Health>("Health")` for blast damage and repair targets, and
`GetNodeOrNull<Node>("Describes")` for the component that describes a hovered unit. That's why
every unit scene names its describing component `Describes`.

`UnitFX` takes whatever `Node2D` children the scene gave it as the pieces to tint, blink and
shatter, so the art decides what the body is made of.

### Prefabs became scenes, and the scene is the level

Each unit is a `.tscn` under `Units/`, and its stats are exported properties written into that file
— the same role the prefab's serialized fields play in Unity, and visible in the Inspector the same
way. `Turret.tscn` is the whole turret: script, art, power node, health bar, and

```
MaxHp = 5.0
Radius = 0.4
Cost = 50
Range = 5.0
RotationSpeed = 180.0
Damage = 0.5
FireRate = 2.0
```

The root scene instances the Core and all 27 resource sites at their positions, exactly as the
Unity scene does with its `PrefabInstance` blocks:

```
[node name="Site04" parent="Board" instance=ExtResource("14_site")]
position = Vector2(-501.76, -490.88)
```

The starting state of a level is scene data. `GameState` doesn't place anything — it finds `Board`,
`Camera`, `WaveDirector` and `Interactions` in `_EnterTree` and publishes them, and that's all the
setup there is. Moving a resource site means dragging it in the editor, not editing an array.

The kinds that get spawned *during* play — relays, turrets, enemies, bombs — are still referenced
from C#, and the root scene holds them as `[Export] PackedScene` fields. That's Godot's answer to
Unity's prefab fields on a MonoBehaviour, and it's why nothing in the C# hardcodes a `res://` path:
`Scripts/Units/Units.cs` reads them off `GameState` at `_EnterTree`, so the folder can be moved
anywhere in a consuming project.

Ordering falls out of this for free, and it's worth seeing why. `Init` runs as a node enters the
tree, and `_EnterTree` propagates top-down — so `GameState.Init` runs before every unit placed in
the scene. Everything they reach for on their way up (the collision worlds, `Board`, the
`WaveDirector`) is already published, and the per-frame collision tick registered there is the first
`ProcessFrame` handler in the game, so the sim never reads a stale world. All of it lives in `Init`;
there isn't a lifecycle override anywhere in the folder.

One place needs the *other* direction — a node's own children being set up — and says so with a
phase rather than a callback: `CameraControls` waits on `IsReady` before making its `Camera2D`
current.

### Coroutines became `s.OnProcess`

Godot C# has no coroutines, so the Unity example's `s.Coroutine` extension has no direct
translation. `Scripts/SpokeExtensions.cs` defines two replacements over `s.OnProcess`:

```csharp
s.Wait(seconds, () => ...);    // once, if the block is still mounted by then
s.Every(seconds, () => ...);   // on an interval, while the block is mounted
```

Both inherit `s.OnProcess`'s behaviour for free: they stop while the host can't process, so the
whole board freezes on `GetTree().Paused` without a single check. And a timer can't outlive its
reason to exist — the power-settle delay in `PowerNode` is a `s.Wait` inside a phase, so a pending
change that reverses before it lands simply unmounts, taking the timer with it. Nothing cancels
anything.

Some things read better restructured than transliterated. Unity's turret fires from a coroutine
holding a cooldown variable; here the beam is a `s.Phase` over "am I firing", so it's on screen for
exactly the length of the shot, and losing power mid-shot takes it away with everything else.

### `Time.timeScale` became `ProcessMode`

Unity freezes the simulation with `Time.timeScale = 0`, which is global — the UI has to opt back
out. Godot's answer is per-subtree: `GetTree().Paused` plus `ProcessMode` on the two nodes that
care. The board is `Pausable`, `GameState` and its UI are `Always`. Two lines, and pause works.

### `SpokeSingleton` became a hand-placed root

Godot scripts can't be generic, so Unity's `SpokeSingleton<T>` has no translation, and Godot's own
answer — an autoload — would need a Project Settings entry in whatever project imports this folder.
So `GameState` publishes itself from its own `Init`, which — because `Init` runs on entering the
tree — happens before any descendant's. The Unity version's comment describes a hand-placed
singleton anyway.

---

## The pooling story

The Unity example includes an object pool "for realism, and because object pooling introduces a
lifecycle-management problem, which is Spoke's strength." That's still true, and in Godot it lands
better than it did in Unity.

A pooled instance is a bug factory: it carries whatever its last life left behind, and every "reset
this on despawn" is a line someone has to remember. Unity's answer is `s.Phase(IsEnabled, ...)` —
`SetActive(false)` fires `OnDisable`, the phase unmounts, and everything scoped to it unwinds.

Godot's equivalent turns out to be **the scene tree itself**. `Pool.Despawn` calls `RemoveChild`,
which drops `IsInTree` to false; `Pool.Spawn` calls `AddChild`, which raises it. So every
`s.Phase(IsEnabled, ...)` in the Unity code became `s.Phase(IsInTree, ...)` here, and it does the
same job: a despawned unit's colliders leave their zones, its entry leaves `Building.All`, its
accumulated damage clears, and all of it comes back fresh on reuse. There is exactly one line of
hand-written reset in the whole game, and it's `damage.Set(0f)` in `Health`'s cleanup.

This works because Spoke.Godot disposes a node's tree at `PREDELETE` rather than at `_ExitTree` — a
pooled unit leaves the tree without being destroyed, so its tree survives the trip. That decision
was made for reparenting; pooling gets it for free.

It's also why the Core's death reads properly. `Core.IsStanding` is a phase over `IsInTree`, not
over health, so the Core holds its place on the board through its shatter and the game only ends
once it's actually gone — the same thing Unity gets from gating on the component's enabled state.

Spoke.Godot ships no node-lifetime helper on purpose — scoping a node to a block means choosing
what cleanup does, and that's a per-game call. This game makes it twice, and the two answers differ:
units go back to the Pool, while the transient things a block puts on screen are simply freed. Only
the second is common enough to be worth an extension, so `s.Own` is the one this folder writes:

```csharp
public static T Own<T>(this EffectBuilder s, Node parent, T node) where T : Node {
    parent.AddChild(node);
    s.OnCleanup(() => {
        if (GodotObject.IsInstanceValid(node)) node.QueueFree();
    });
    return node;
}
```

A turret's beam, a coverage overlay, a radar-tracked marker, the Core's death flare — all of them
are one of these, and none can outlive the reason it exists, because there is nowhere else to write
the teardown.
