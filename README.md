# 🔘 Spoke - _A reactive framework for Unity_

**Spoke** is a reactive framework for Unity that lets you express chaotic, indirect and entangled event-driven logic in a clear, composable, top-to-bottom structure.

Games are full of long-lived behaviours managed by symmetric Setup/Teardown functions:

- `OnEnable`/`OnDisable`
- `Awake`/`OnDestroy`
- `OnEnemyDetected`/`OnEnemyLost`
- `OnLaserStart`/`OnLaserEnd`

Even `OnValueChanged` handlers fit the pattern: teardown for the old value, setup for the new.

Sometimes they're wired up with events, sometimes with polling and diff-checking in `Update()`. Either way, the symmetry is maintained by hand, and bugs slip through. For example, an enemy is destroyed while shooting its laser beam, and the laser asset is left behind because nothing cleaned it up.

In Spoke, these long-lived behaviours are modelled as localised blocks in a tree, where setup, reaction and cleanup are co-located in one function body. Lifecycle bugs are easy to avoid, the code becomes simpler to reason about and extend, and event-driven behaviour ends up feeling as straightforward as imperative code.

- ✨ **Control complexity** — entangled, event-driven logic stays local and readable
- 🧪 **Use anywhere** — adopt it in one script, one system, or a whole project
- 🪶 **Lightweight** — ~2,800 lines, zero dependencies, unit-tested, MIT

---

## ⚡ Example

_Show a HUD over the nearest enemy._

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

The Spoke version reads top to bottom: if there is no nearest enemy, do nothing. Otherwise spawn a HUD, and destroy it when the block ends. The block re-runs whenever `NearestEnemy` changes, because `s.D(...)` subscribes to it.

For a complete game built entirely with Spoke, see **[Base Defence](./Examples/05_BaseDefence/)**.

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

- **State** — a reactive value; read it, set it, subscribe to it
- **Trigger** — a reactive event; fire it, subscribe to it
- **Effect** / **Phase** / **Reaction** — self-cleaning blocks of logic; all re-run when a value they read changes, and differ in when they start: Effect immediately, Phase while a condition is true, Reaction when a trigger fires
- **Memo** — a computed value, recalculated when its inputs change
- **Dock** — attach and remove blocks dynamically, by key

---

## 🤔 Spoke-Style Reactivity

Spoke was inspired by frontend reactive frameworks like **React** and **SolidJS**. It most closely resembles **SolidJS** of the two. There are some important differences, though.

### Many reactive trees

In Spoke, it's normal to have lots of small reactive trees. Each `SpokeBehaviour`, for example, creates its own tree. This helps Spoke integrate with Unity and its existing imperative code. Spoke is glue between imperative systems, it's not the master.

### Imperative-ordered execution

Spoke orders its reactive computations by source-code order, instead of doing any topological sorting. You can read Spoke code top-to-bottom to understand what order effects and memos will run in. Spoke allows you to modify reactive state inside an effect body, even if it causes ping-ponging, because execution order is deterministic. The consequence is that Spoke is not suitable for any arbitrary dependency graph. Or put another way, you wouldn't build Excel in Spoke.

---

## 🎮 Base Defence

**[Base Defence](./Examples/05_BaseDefence/)** is a complete RTS-lite tower defence written entirely in Spoke, in around 2,600 lines. It has a power grid, spatial queries, enemy waves, building placement and object pooling, and no `Update()` methods. Per-frame work runs in coroutines whose lifetime Spoke manages.

It's the best place to see how Spoke is intended to be used in a real game. The smaller examples build up to it one concept at a time: [Lifecycle](./Examples/01_Lifecycle/), [State](./Examples/02_State/), [Effect](./Examples/03_Effect/), [Memo](./Examples/04_Memo/).

---

## 🔍 More Patterns

### Resource lifecycles

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

### Chained event subscriptions

When an enemy is detected on radar, and later destroyed, the cockpit voice should announce it:

```cs
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

- Unity 2022.3 or later (For Examples)
- No packages, no dependencies

---

## 📜 License

MIT — free to use in personal or commercial projects.
