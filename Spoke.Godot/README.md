# Spoke.Godot

Godot 4 integration for Spoke. Requires **Spoke.Runtime** and **Spoke.Reactive**; those two have no
engine dependency, this folder is the only part that references Godot.

C# only — Spoke is a C# library, so the project needs the .NET version of Godot.

## Install

Copy `Spoke.Runtime`, `Spoke.Reactive` and `Spoke.Godot` anywhere under your project. `Godot.NET.Sdk`
globs `**/*.cs`, so they compile with no csproj changes and nothing to register.

> If you also keep `Spoke.Unity` around, make sure it stays **out** of the Godot project directory —
> it references `UnityEngine` and will break the build.

## Getting started

Extend `SpokeNode` instead of `Node` — or `SpokeNode2D`, `SpokeNode3D`, `SpokeControl` — and override
`Init`:

```csharp
using Godot;
using Spoke;

public partial class Turret : SpokeNode2D {

    State<Node2D> target = State.Create<Node2D>(null);
    [Export] public float Range { get; set; } = 400f;

    protected override void Init(EffectBuilder s) {

        // Runs once, as the node enters the tree. Cleanup runs when the node is freed.
        s.OnCleanup(() => GD.Print("turret gone"));

        // A window that opens and closes with a condition
        s.Phase(IsInTree, s => {
            s.OnProcess(delta => Scan());
        });

        // Re-runs whenever target changes; the old beam is freed first
        s.Effect(s => {
            if (s.D(target) == null) return;
            var beam = BeamScene.Instantiate<Line2D>();
            AddChild(beam);
            s.OnCleanup(() => beam.QueueFree());
            s.OnProcess(_ => beam.SetPointPosition(1, target.Now.Position - Position));
        });
    }
}
```

`Init` replaces `_EnterTree` and the teardown half of `_ExitTree`. Every other virtual — `_Ready`,
`_Process`, `_Input`, `_Draw` — is untouched and still yours to override.

Reactive state reaches the inspector through a normal exported property, no wrapper type needed:

```csharp
State<float> speed = State.Create(5f);
[Export] public float Speed { get => speed.Now; set => speed.Set(value); }
```

## Lifecycle signals

| Signal | True while | Available on |
|---|---|---|
| `IsInTree` | the node is inside the SceneTree | all |
| `IsReady` | `_Ready` has fired — this node's children are set up | all |
| `IsShown` | the node is visible in the tree (self and ancestors) | `SpokeNode2D`, `SpokeNode3D`, `SpokeControl` |

Gate work with `s.Phase(IsInTree, ...)` and friends.

### Which way does your dependency point?

`Init` runs as the node **enters the tree**, and `_EnterTree` propagates top-down — so a node's
`Init` runs before any of its descendants'. That's the direction dependencies usually run: a hub
node publishes what the nodes beneath it read, and it can do that from `Init` like everything else,
with no separate `_EnterTree` override.

The other direction is `IsReady`. `_Ready` propagates bottom-up, so anything that has to see its own
children initialised waits for it:

```csharp
protected override void Init(EffectBuilder s) {
    var camera = GetNode<Camera2D>("Camera2D");   // the node exists now...

    s.Phase(IsReady, s => camera.MakeCurrent());  // ...but it isn't in the tree yet
}
```

Once true, `IsReady` stays true — including across a reparent — exactly as `Node.IsNodeReady()`
does. Both orderings are signals, which is the point: callback order is the thing Spoke exists to
stop you depending on.

### Reacting to pause

There's no `IsPaused` signal. `s.OnProcess` already stops while a node can't process, which covers
the common case, and pause-bracketed setup/teardown is rare enough that it doesn't belong on every
node. If one node needs it, `OnNotification` is the hook:

```csharp
State<bool> isPaused = State.Create(false);

protected override void OnNotification(int what) {
    switch ((long)what) {
        case NotificationPaused: isPaused.Set(true); break;
        case NotificationUnpaused: isPaused.Set(false); break;
    }
}
```

Two things worth knowing. Godot sends `NOTIFICATION_PAUSED` for **both** causes — `SceneTree.paused`
reaching the node, *and* the node's own `ProcessMode` being set to `Disabled` — so this tracks
either, and always agrees with `CanProcess()`. And seed it from `!CanProcess()` in `_Ready` if a node
can start out disabled, since no notification fires for the initial state.

## EffectBuilder extensions

**Everything on `s` is scoped to the block it's called in** — that's the convention across all of
Spoke, and these follow it. Two entries, because only two things here have no clean equivalent in
plain code and no policy to presume.

```csharp
s.Subscribe(button, Button.SignalName.Pressed, () => ...);   // connects, auto-disconnects
s.Subscribe<Node2D>(area, Area2D.SignalName.BodyEntered, b => ...);
s.Subscribe(area, Area2D.SignalName.AreaShapeEntered,        // any arity, via Callable
            Callable.From<Rid, Area2D, long, long>(OnHit));

s.OnProcess(delta => ...);                                   // _Process, scoped to the block
s.OnPhysicsProcess(delta => ...);                            // _PhysicsProcess, scoped to the block
```

`OnProcess` skips while the host is out of the tree or paused, matching `_Process`. To run through a
pause, set the node's `ProcessMode` to `Always` — the same thing you'd do for a hand-written
`_Process`.

## Node lifetimes

There's no node helper here on purpose. Scoping a node to a block means deciding what happens when
the block ends — free it, return it to a pool, hide it and reuse it — and that's a per-game
decision. Inside a Spoke node `this` **is** the node, so the plain form is two lines:

```csharp
var hud = HudScene.Instantiate<Control>();
AddChild(hud);
s.OnCleanup(() => hud.QueueFree());
```

If your game does this often enough to want it wrapped, write the extension that matches your
policy. The free-it version:

```csharp
public static T Spawn<T>(this EffectBuilder s, Node parent, T node) where T : Node {
    parent.AddChild(node);
    s.OnCleanup(() => {
        if (GodotObject.IsInstanceValid(node)) node.QueueFree();
    });
    return node;
}
```

...or the pooled version, same shape, different cleanup:

```csharp
public static T Rent<T>(this EffectBuilder s, Pool<T> pool, Node parent) where T : Node {
    var node = pool.Rent();
    parent.AddChild(node);
    s.OnCleanup(() => pool.Return(node));
    return node;
}
```

Three things to get right in either version, all learned the hard way:

- **`QueueFree()`, not `Free()`.** Cleanup runs mid-flush, which can be reached from a signal handler
  or notification propagation, and Godot is explicit that freeing a node still in use is unsafe.
- **Guard with `IsInstanceValid`.** The node may have freed itself first — a bullet on impact, a
  one-shot effect. Godot throws on a freed node rather than no-oping the way Unity's `Destroy()`
  does. Spoke catches exceptions in cleanup and logs them without faulting the tree, so it isn't
  fatal, but you'll get an error per teardown.
- **Only accept nodes that aren't in the tree yet.** Godot refuses to re-parent a node that already
  has a parent — it logs an error and carries on — so a helper that took one anyway would register
  cleanup for a node it never added, then later destroy something belonging elsewhere in the scene.
  A `node.GetParent() != null` check up front turns that into a loud failure.

> **Don't reach for `s.Use(node)`.** It compiles — `GodotObject` implements `IDisposable` — but
> disposing a node destroys it *immediately*, the `Free()` path rather than `QueueFree()`, with the
> hazard described above.

The Base Defence example writes both, and is worth a look if you're deciding what yours should do:
`Examples/05_BaseDefence/Scripts/SpokeExtensions.cs` has an `s.Own` that frees and an `s.Spawn` that
returns to a pool. Its pooling also shows why `IsInTree` matters — `RemoveChild` takes a unit out of
play without destroying it, so everything under `s.Phase(IsInTree, ...)` unwinds and comes back
fresh on reuse, with no reset path to write.

## Everything else

Anything not in that list is an ordinary Godot call, with `s.OnCleanup` as the teardown half. C#
event-style signals, for instance:

```csharp
button.Pressed += OnPressed;
s.OnCleanup(() => button.Pressed -= OnPressed);
```

## Nodes you can't rebase

`SpokeHost` attaches a tree to any existing node as a child, so the base class doesn't matter:

```csharp
public partial class Player : CharacterBody2D {
    public override void _Ready() {
        SpokeHost.Attach(this, s => {
            s.OnPhysicsProcess(delta => MoveAndSlide());
        });
    }
}
```

It enters and leaves the tree with its target, inherits its pause state, and is freed when the
target is freed. Use it for third-party nodes, or when one node needs several independent trees.

## Design notes

**Four base classes.** Unity attaches behaviour by composition, so one `SpokeBehaviour` covers
everything. Godot attaches scripts by inheritance and `Node2D`/`Node3D`/`Control` are separate
hierarchies, so there's no single base class that works everywhere. All four are thin shims over
`SpokeNodeCore`, which holds the logic once. `SpokeHost` covers the rest.

**The tree spawns at `_EnterTree`, not `_Ready`,** so `Init` runs parent-before-child. A node whose
descendants depend on it — a hub, a context, a service — can set that up in `Init` along with
everything else. Spawning at `_Ready` inverts the order and forces those nodes into a separate
`_EnterTree` override, which is the abstraction leaking.

The cost is that a node's `Init` can't see state its *children's* `Init` established, and `IsReady`
is the answer to that. Between the two, both orderings are available as signals.

> An earlier version of this file claimed `AddChild` was forbidden during `_EnterTree`, and used
> that to justify spawning at `_Ready`. It isn't forbidden: `data.blocked` guards a node while *it*
> is iterating its own children, and the node receiving the notification isn't the one iterating.
> The reason was wrong, and it was the wrong thing to weigh anyway.

**The tree is disposed at `PREDELETE`, not `_ExitTree`,** so it survives reparenting — a node removed
and re-added keeps its state, and `IsInTree` just cycles. A `QueueFree`d node is torn down at
`_ExitTree` instead, while its cleanup can still reach the tree.

**`_Notification` is sealed** on the base classes, because a user override that forgets `base` would
silently orphan the tree. Override `OnNotification(int what)` instead. Routing everything through
notifications is also what keeps `_Ready`, `_Process` and the rest free for you.

**No `UState<T>`.** Unity needs 188 lines of `ISerializationCallbackReceiver` and a custom
`PropertyDrawer` because it serializes *fields*. Godot exports *properties* with setter bodies, so a
two-line getter/setter pair does the job.

**No `SpokeSingleton`.** Godot has autoloads. Register the node in Project Settings → Autoload and
expose a static `Instance` from its `_EnterTree` if you want typed access. Note that Godot scripts
can't be generic, so Unity's `SpokeSingleton<T>` pattern has no direct translation anyway.

**No `GodotSignals`.** Unity's `UnitySignals` exists to paper over domain reload, play-mode
transitions, and the missing pre-scene-unload event. Godot has none of those problems: `_ExitTree`
fires reliably, and app-level events like `NOTIFICATION_WM_CLOSE_REQUEST` arrive at nodes, where
`OnNotification` can handle them.

**The extension set is deliberately small**, and got smaller as it was written. Drafts included a
`HostNode()` accessor, an `Own(node)`, and an `AddChild` that freed the node at cleanup. All came
out. `HostNode()` only saved typing, since inside a Spoke node `this` is already the node. The node
helpers foundered on the same rock: scoping a node to a block means choosing what cleanup *does*, and
free-it is only one answer — pooling is at least as common in a real game. A library that picks for
you is worse than two plain lines. What's left is the pair that has no policy to get wrong.

**Bootstrap is `[ModuleInitializer]`.** Runs once on assembly load, before any script — covering the
running game, `[Tool]` scripts, and post-build reloads, with nothing to register.
