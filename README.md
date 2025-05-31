# 🔘 Spoke - _A reactive framework for simulated worlds_

**Spoke** is a tiny declarative reactivity engine for Unity.

It lets you write gameplay logic that reacts to state automatically: no flag-checking, no brittle events, no manual cleanup.

Instead of scattering logic across `Update()`, `OnEnable()`, and coroutines, Spoke structures it into **scoped, self-cleaning blocks** that mount and unmount on their own. For eventful logic, **you can often remove** `Update()` completely.

- ✨ **Declarative logic** — express _what_ should happen, not _when_ to check it
- 🧠 **Scoped effects** — stay in sync with state and clean up automatically
- 🎯 **Predictable** — reactive scopes flush in a stable, deterministic order
- 📦 **Drop-in simple** — two files, no setup, no dependencies
- 🧪 **Use anywhere** — adopt in one script, one system, or your whole project

---

## 🤔 What is Spoke-style Reactivity?

Spoke shares a DNA with frontend reactivity engines like `React` or `SolidJS`. The mental model is the same, but the problem domain is different. Spoke was built from the ground up to express general game logic, not for building UIs. Think of it as "React for simulations — behaviour trees, not DOM trees."

If you have experience with reactive frameworks, Spoke will feel natural. If not, it's a paradigm shift, but once it clicks, it unlocks a new level of clarity and control.

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
    s.UsePhase(IsEnabled, s => {
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

Copy **Spoke.cs** and **Spoke.Unity.cs** into your project.

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

        s.UsePhase(IsAwake, s => {
            // Runs at the end of Awake (useful for dependency timing)
        });

        s.UsePhase(IsEnabled, s => {
            // OnEnable logic here
            s.OnCleanup(() => {
                // OnDisable logic here
            });
        });

        s.UsePhase(IsStarted, s => {
            // Start logic here
        });
    }
}
```

---

## ⚙️ Prefer manual control?

You can also create a `SpokeEngine` manually in any `MonoBehaviour`:

```csharp
using Spoke;

public class MyBehaviour : MonoBehaviour {

    State<bool> isEnabled = State.Create(false);
    Effect effect;

    void Awake() {

        var engine = new SpokeEngine(FlushMode.Immediate, new UnitySpokeLogger(this));

        effect = new Effect("MyEffect", engine, s => {

            s.UsePhase(isEnabled, s => {
                // OnEnable logic
                s.OnCleanup(() => {
                    // OnDisable logic
                });
            });
        });
    }

    void OnDestroy() => effect.Dispose();

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
- **SpokeEngine** - executor of reactive computation

---

## 🎮 Origins

Spoke was developed to support my passion project: **_Power Grip Dragoons_** — a VR mech game that leans heavily into systems, emergent behaviour and runtime composability:

- **Mechs** are containers for **Servos**
- **Servos** are containers for **Modules**
- **Reactors** provide power to other **Modules**
- **Sensors** detect blips and feed a shared, live targeting system
- **Deflectors** increase armour across **Servos**, but draw power from **Reactors**
- **Modules** are damaged, destroyed, repaired, disabled and reconfigured on the fly
- Cockpit displays reflect functionality — based on which **Modules** are mounted, powered, and still operational

This game has brutal requirements for dynamic, eventful logic. For 6 years I tried to build an architecture that brought sanity to this chaos.

**Spoke is the crystallized form for what I've learned.** Once it emerged in this form, all the complexity of my previous code evaporated. Suddenly, building a _modular-vehicle immersive simulator_ felt easy.

I believe Spoke could be a general-purpose pattern for game programming with huge potential. I've found it works across all code domains. It’s useful in any project, and seems to shine as complexity grows. The more deeply systems interact, the more value Spoke provides.

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
