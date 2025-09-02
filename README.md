# 🔘 Spoke - _A reactive framework for simulated worlds_

**Spoke** is a tiny reactivity engine for **C#** and **Unity**. <br>
It’s tree-shaped and declarative, with imperative-ordered execution, and built to tame the chaos when many systems interact in dynamic, emergent ways.

- Start and stop behaviours at the right time, keeping them in sync with runtime state.
- Manage deeply nested logic while keeping it cohesive and clear.

Inspired by React, Spoke adds strict guarantees on execution order, making it suitable for game logic. It makes indirect, entangled logic feel imperative, with **automatic lifecycle management** and **self-cleaning reactivity.**

No flag-checking. No brittle events. No manual cleanup.<br>
Just _stateful blocks of logic_, expressed as a tree, that mount and unmount on their own.

- ✨ **Control complexity** — write clear, reactive gameplay logic
- 🧪 **Use anywhere** — adopt in one script, one system, or your whole project

---

## ⚡ Example

_Spawn a HUD over the nearest enemy_

### 🟧 Vanilla Unity:

```csharp
GameObject currHUD;

void Awake() {
    OnNearestEnemyChanged.AddEventListener(NearestEnemyChangedHandler);
}

void OnDestroy() {
    OnNearestEnemyChanged.RemoveEventListener(NearestEnemyChangedHandler);
    if (currHUD != null) Destroy(currHUD);
}

void NearestEnemyChangedHandler(GameObject enemy) {
    if (currHUD != null) Destroy(currHUD);
    if (enemy != null) currHUD = SpawnHUD(enemy);
}
```

### 🟦 Spoke:

```csharp
void Init(EffectBuilder s) {
    if (s.D(NearestEnemy) == null) return;
    var hud = SpawnHUD(NearestEnemy.Now);
    s.OnCleanup(() => Destroy(hud));
}
```

👉 In Spoke, the entire behaviour lives in one expressive block. Setup, reaction, and cleanup happen automatically.

---

## 💡 Why Spoke?

Unity’s lifecycle makes it easy for logic to get scattered:

- Systems spread across `Awake`, `OnEnable`, `OnDisable`, `OnDestroy`
- Polling state in `Update` just to detect changes
- Brittle event chains with manual subscription cleanup
- Initialization order bugs between dependent components
- Scene teardown chaos: accessing destroyed objects

Spoke collapses those problems into **scoped, self-cleaning windows of logic.**<br>
You write: _"When this state exists — run this behaviour — and clean it up afterward.”_

---

## 🔰 Install

Clone this repo or copy **Spoke.Runtime**, **Spoke.Reactive** and **Spoke.Unity** into your project.<br>
No dependencies, no setup.

---

## 🚀 Getting Started

Subclass `SpokeBehaviour` instead of `MonoBehaviour`:

```csharp
using Spoke;

public class MyBehaviour : SpokeBehaviour {

    // Replaces Awake, OnEnable, Start, OnDisable, OnDestroy
    protected override void Init(EffectBuilder s) {

        // Awake logic here

        s.OnCleanup(() => {
            // OnDestroy logic here
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

[Or spawn a SpokeTree in your own scripts.](./Docs/Core/07_SpokeTree.md#usage-with-spokebehaviour)

[Read the Quickstart →](./Docs/Core/01_QuickStart.md)

---

## 🧠 Core Concepts

The reactive model behind Spoke is built around a few simple primitives:

- **Trigger** - fire-and-forget events
- **State** - reactive container for values
- **Effect** / **Phase** / **Reaction** - self-cleaning blocks of logic
- **Memo** - computed reactive value
- **Dock** - dynamic reactive container

---

## 🤔 "Spoke-style" reactivity?

Spoke shares DNA with frameworks like **React** and **SolidJS**<br>
Instead of managing a DOM tree, you're sculpting **simulation logic:** behaviour trees, stateful systems, emergent gameplay.

These frameworks transformed how we write UI.<br>
Spoke applies the same principles to **gameplay logic.**

---

## 🎮 Origins

Spoke was born out of necessity while building my VR mech game, **Power Grip Dragoons**. The game has brutal demands for dynamic, event-driven logic on Meta Quest hardware. Over 6 years I refined this architecture until it became the foundation I now use everywhere. Spoke is the result.

---

## 🔍 Real-World Patterns

### Scattered Resource Management

Managing disposables in Unity usually means spreading logic across lifecycle methods.

```cs
// --- MonoBehaviour
public class MyBehaviour : MonoBehaviour {

    IDisposable myResource;

    void OnEnable() {
        myResource = new SomeCustomResource();
    }

    void OnDisable() {
        myResource.Dispose();
    }
}

// --- Spoke
public class MySpokeBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        s.Phase(IsEnabled, s => {
            s.Use(new SomeCustomResource());
        });
    }
}
```

In Spoke, resource allocation and cleanup collapse into one scoped block. No more lifecycle bugs scattered across methods.

---

### Chained Event Subscriptions

Nested event subscriptions (`EnemyDetected → EnemyDestroyed`) get messy fast.

```cs
// When an enemy is detected on radar, and it becomes destroyed. Then the
// cockpit voice (BitchinBetty) should speak the phrase: "Enemy Destroyed".

// --- MonoBehaviour
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

// --- Spoke
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

In Spoke, the entire subscription chain lives in one cohesive block. Setup and teardown are automatic. No missed unsubscribes.

---

Both patterns are really the same thing, they're lifecycle windows. With Spoke, you declare what happens in a window, how windows nest, and how to clean up when they end.

---

## 📘 Documentation

[Read the full documentation →](./Docs/)

---

## 🔬 Performance

[See performance notes →](./Docs/Topics/Performance.md)

---

## 🧰 Requirements

- Unity 2021.3 or later (For Examples)
- No packages, no dependencies

---

## 📜 License

MIT — free to use in personal or commercial projects.
