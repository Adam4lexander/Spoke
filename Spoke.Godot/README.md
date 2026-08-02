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

        // Runs once, at _Ready. Cleanup runs when the node is freed.
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

`Init` replaces `_Ready` and the teardown half of `_ExitTree`. Every other virtual — `_Process`,
`_Input`, `_Draw`, `_UnhandledInput` — is untouched and still yours to override.

Reactive state reaches the inspector through a normal exported property, no wrapper type needed:

```csharp
State<float> speed = State.Create(5f);
[Export] public float Speed { get => speed.Now; set => speed.Set(value); }
```

## Lifecycle signals

| Signal | True while | Available on |
|---|---|---|
| `IsInTree` | the node is inside the SceneTree | all |
| `IsPaused` | SceneTree pause is stopping this node processing | all |
| `IsShown` | the node is visible in the tree (self and ancestors) | `SpokeNode2D`, `SpokeNode3D`, `SpokeControl` |

Gate work with `s.Phase(IsInTree, ...)` and friends.

## EffectBuilder extensions

Only two things live here, because only two things have no straightforward equivalent in plain code:

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

Everything else is an ordinary Godot call. Inside a Spoke node `this` **is** the node, so `AddChild`,
`GetNode` and `QueueFree` work as they always do, and `s.OnCleanup` covers teardown:

```csharp
var hud = HudScene.Instantiate<Control>();
AddChild(hud);
s.OnCleanup(() => hud.QueueFree());
```

Same for C# event-style signals:

```csharp
button.Pressed += OnPressed;
s.OnCleanup(() => button.Pressed -= OnPressed);
```

> **One Godot gotcha worth knowing.** Unity overloads `==` so a destroyed object compares equal to
> `null`, and `Destroy()` on an already-destroyed object is a silent no-op. Godot does neither —
> calling `QueueFree()` on a node something else already freed throws `ObjectDisposedException`.
> Spoke catches exceptions thrown in cleanup and logs them without faulting the tree, so it isn't
> fatal, but you'll get an error per teardown. If a node can free itself — a bullet on impact, a
> one-shot effect — guard it:
>
> ```csharp
> s.OnCleanup(() => { if (GodotObject.IsInstanceValid(n)) n.QueueFree(); });
> ```

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

**The tree spawns at `_Ready`, not `_EnterTree`.** `_Ready` is Godot's setup callback: children have
run their own `_Ready`, and the node is no longer blocked, so `Init` can call `AddChild`. Both are
forbidden during `_EnterTree`.

**No `IsReady` signal.** Unity needs `Awake` vs `Start` because `Awake` can't see other objects'
initialization. Godot's bottom-up `_Ready` already solves that, so `Init` *is* the ready state.

**The tree is disposed at `PREDELETE`, not `_ExitTree`,** so it survives reparenting — a node removed
and re-added keeps its state, and `IsInTree` just cycles. A `QueueFree`d node is torn down at
`_ExitTree` instead, while its cleanup can still reach the tree.

**`_Notification` is sealed** on the base classes, because a user override that forgets `base` would
silently orphan the tree. Override `OnNotification(int what)` instead. Routing everything through
notifications is also what keeps `_Ready`, `_Process` and the rest free.

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

**The extension set is deliberately small.** An earlier draft had node-ownership helpers and a
`HostNode()` accessor. They came out: inside a Spoke node `this` is already the node, so they only
saved typing, and guessing at what users want from an engine they know better than the library does
is how an API gets fat. Signals and per-frame work stay because neither has a one-line equivalent.

**Bootstrap is `[ModuleInitializer]`.** Runs once on assembly load, before any script — covering the
running game, `[Tool]` scripts, and post-build reloads, with nothing to register.
