# 🔘 Spoke - _A reactive framework for simulated worlds_

**Spoke** is a tiny reactivity engine for Unity.

It lets you write gameplay logic that reacts to state automatically: no flag-checking, no brittle events, no manual cleanup.

Instead of scattering logic across `Update()`, `OnEnable()`, and coroutines, Spoke structures it into **scoped, self-cleaning blocks** that mount and unmount on their own. For eventful logic, **you can often remove** `Update()` completely.

- ✨ **Control complexity** — simplifies eventful, state-driven logic
- 📦 **Drop-in simple** — three files, no setup, no dependencies
- 🧪 **Use anywhere** — adopt in one script, one system, or your whole project

---

## 💡 Why Spoke?

Spoke was built to solve recurring pain points in Unity:

- Spaghetti logic across `Awake`, `OnEnable`, `OnDisable` and `OnDestroy`
- Difficulty managing initialization order between dependent components
- Scene teardown chaos: accessing destroyed objects in `OnDisable`
- Polling state in `Update` to detect and respond to changes
- Gameplay systems that grow brittle as complexity increases
- Solving them with the smallest possible framework

Spoke enables a programming paradigm called _reactive programming_. It makes eventful, state-driven logic easier to express.

You write logic like this:

> "When this state exists — run this behaviour — and clean it up afterward."

Here's an example:

_When an enemy is nearby, turn my head to face them. When no enemy, face forwards._

### 🟧 Vanilla Unity:

```csharp
void OnEnable() {
    OnNearestEnemyChanged.AddEventListener(OnNearestEnemyChangedHandler);
    if (NearestEnemy != null) {
        OnNearestEnemyChangedHandler(NearestEnemy);
    }
}

void OnDisable() {
    OnNearestEnemyChanged.RemoveEventListener(OnNearestEnemyChangedHandler);
    if (NearestEnemy != null) {
        OnNearestEnemyChangedHandler(null);
    }
}

void OnNearestEnemyChangedHandler(GameObject toEnemy) {
    if (toEnemy != null) {
        FaceEnemy(toEnemy);
    } else {
        FaceForwards();
    }
}
```

### 🟦 Spoke:

```csharp
void Init(EffectBuilder s) {
    s.Phase(IsEnabled, s => {
        if (s.D(NearestEnemy) == null) return;
        FaceEnemy(NearestEnemy.Now);
        s.OnCleanup(() => FaceForwards());
    });
}
```

The vanilla Unity version splits logic across multiple methods, with edge cases and bookkeeping. In Spoke, everything lives in one expressive block: **setup, reaction, and teardown**.

It's not just shorter. It's **closer to how you actually think.**

- Code is easier to understand
- Fewer edge cases and lifecycle bugs
- You can scale logic without losing clarity

---

## 🔰 Getting Started

Copy **Spoke.Runtime.cs**, **Spoke.Reactive.cs** and **Spoke.Unity.cs** into your project.

Then create a new script and subclass `SpokeBehaviour` instead of `MonoBehaviour`:

```csharp
using Spoke;

public class MyBehaviour : SpokeBehaviour {

    // Replaces Awake, OnEnable, Start, OnDisable, OnDestroy
    protected override void Init(EffectBuilder s) {

        // Run Awake logic here

        s.OnCleanup(() => {
            // Run OnDestroy logic here
        });

        s.Phase(IsAwake, s => {
            // Runs at the end of Awake (useful for dependency timing)
        });

        s.Phase(IsEnabled, s => {
            // OnEnable logic here
            s.OnCleanup(() => {
                // OnDisable logic here
            });
        });

        s.Phase(IsStarted, s => {
            // Start logic here
        });
    }
}
```

---

## ⚙️ Prefer manual control?

You can also create a `FlushEngine` manually in any `MonoBehaviour`:

```csharp
using Spoke;

public class MyBehaviour : MonoBehaviour {

    State<bool> isEnabled = State.Create(false);
    SpokeTree<FlushEngine> tree;

    void Awake() {
        // A FlushEngine is the execution scheduler for the reactive objects it contains
        tree = SpokeRuntime.SpawnTree(new FlushEngine(s => {
            s.Phase(isEnabled, s => {
                // OnEnable logic
                s.OnCleanup(() => {
                    // OnDisable logic
                });
            });
        }));
    }

    void OnDestroy() => tree.Dispose();

    void OnEnable() => isEnabled.Set(true);
    void OnDisable() => isEnabled.Set(false);
}
```

Spoke integrates with Unity through a very thin wrapper.
Take a peek at SpokeBehaviour if you're curious — it's tiny.

---

## 🧠 Core Concepts

The reactive model behind Spoke is built around a few simple primitives:

- **Trigger** - fire-and-forget events
- **State** - reactive container for values
- **Effect** / **Phase** / **Reaction** - self-cleaning blocks of logic
- **Memo** - computed reactive value
- **Dock** - dynamic reactive container
- **FlushEngine** - executor of reactive computation

---

## 🤔 What is Spoke-style Reactivity?

Spoke shares DNA with frontend reactivity engines like `React` and `SolidJS`. The mental model is the same, but instead of managing a DOM tree, you're sculpting simulation logic: behaviour trees, system interactions, and stateful gameplay.

These frameworks transformed how we write UI. Spoke applies the same reactivity model to general game logic. Once it clicks, it unlocks a new level of clarity and control.

---

## 🎮 Origins

Spoke was developed to support my passion project: **_Power Grip Dragoons_** — a VR mech game that leans heavily into systems, emergent behaviour and vehicle modularity. This game has brutal requirements for dynamic, eventful logic. Over 6 years I refined an architecture to express it. Architecture is a priority for me to keep development fun and engaging. It's a passion project after all, not a job.

From the outset, I designed Spoke to simplify two patterns I had everywhere in my code:

---

### Scattered Resource Management

```cs
// ----------------------------- MonoBehaviour Version ---------------------
// There are 3 separate code-points to manage the lifecycle of myResource.
public class MyBehaviour : MonoBehaviour {

    IDisposable myResource;

    void OnEnable() {
        myResource = new SomeCustomResource();
    }

    void OnDisable() {
        myResource.Dispose();
    }
}

// ----------------------------- Spoke Version -----------------------------
// In Spoke, resource and lifecycle management collapses into one coherent bundle.
public class MySpokeBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        s.Phase(IsEnabled, s => {
            s.Use(new SomeCustomResource());
        });
    }
}
```

---

### Chained Event Subscriptions

```cs
// ----------------------------- MonoBehaviour Version ---------------------
// When an enemy is detected on radar, and it becomes destroyed. Then the
// cockpit voice (BitchinBetty) should speak the phrase: "Enemy Destroyed".
public class MyBehaviour : MonoBehaviour {

    public UnityEvent<RadarBlip> EnemyDetected;
    public UnityEvent<RadarBlip> EnemyLost;

    void Awake() {
        EnemyDetected.AddListener(HandleEnemyDetected);
        EnemyLost.AddListener(HandleEnemyLost);
    }

    void OnDestroy() {
        EnemyDetected.RemoveListener(HandleEnemyDetected);
        EnemyLost.RemoveListener(HandleEnemyLost);
    }

    void HandleEnemyDetected(RadarBlip enemy) {
        enemy.OnDestroyed.AddListener(HandleEnemyDestroyed);
    }

    void HandleEnemyLost(RadarBlip enemy) {
        enemy.OnDestroyed.RemoveListener(HandleEnemyDestroyed);
    }

    void HandleEnemyDestroyed() {
        BitchinBetty.SpeakEnemyDestroyed();
    }
}

// ----------------------------- Spoke Version -----------------------------
// Again, Spoke collapses the problem into one cohesive bundle
public class MySpokeBehaviour : SpokeBehaviour {

    public UnityEvent<RadarBlip> EnemyDetected;
    public UnityEvent<RadarBlip> EnemyLost;

    protected override void Init(EffectBuilder s) {
        var dock = s.Dock();
        s.Subscribe(EnemyDetected, enemy => dock.Effect(enemy, s => {
            s.Subscribe(enemy.OnDestroyed, BitchinBetty.SpeakEnemyDestroyed);
        }));
        s.Subscribe(EnemyLost, enemy => dock.Drop(enemy));
    }
}
```

The first pattern: _Scattered Resource Management_, is annoying, but manageable. The second pattern can be soul-crushing. Chaining event subscriptions gets complicated very quickly. Spoke makes it effortless.

Both these patterns are manifestations of the same core shape. They are lifecycle windows. OnEnable/OnDisable is a window, and so is EnemyDetected/EnemyLost. With Spoke, you declare what behaviour should exist in a window, how the windows are nested, and how to clean up when the window ends. It's all expressed in one cohesive bundle, and it feels as simple as writing imperative code.

These problems prompted Spoke's creation, and I keep finding new and surprising ways to use it. Today, it powers everything in my game, from CPU-budgeted task management to procedural generation. For me, it's unlocked a way of programming I've always wanted — where I'm sculpting logic instead of fighting complexity. In simulation-heavy code, it makes a big difference.

---

## 📘 Documentation

[Read the full documentation →](./Docs/)

---

## 🚀 Performance

[See performance notes →](./Docs/Topics/Performance.md)

---

## 🧰 Requirements

- Unity 2021.3 or later (For Examples)
- No packages, no dependencies

---

## 📜 License

MIT — free to use in personal or commercial projects.
