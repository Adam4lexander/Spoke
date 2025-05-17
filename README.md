# 🔘 Spoke - _A reactive framework for simulated worlds_

**Spoke** is a tiny declarative reactivity engine for Unity.

It lets you write gameplay logic that reacts to state automatically — no flag-checking, no brittle events, no manual cleanup.

Instead of scattering logic across `Update()`, `OnEnable()`, and coroutines, Spoke structures it **into scoped, self-cleaning blocks** that mount and unmount on their own.

- ✨ **Declarative logic** — express _what_ should happen, not _when_ to check it
- 🧠 **Scoped effects** — stay in sync with state and clean up automatically
- 🎯 **Predictable** — reactive scopes flush in a stable, deterministic order
- 📦 **Drop-in simple** — two files, no setup, no dependencies
- 🧪 **Use anywhere** — adopt in one script, one system, or your whole project

---

## 💡 Why Spoke?

Most complexity in code doesn't come from systems — it comes from **the glue.**

We call it _plumbing code_ — messy, brittle, and usually dismissed with a shrug:

> _"It's just plumbing"_

Spoke elevates plumbing into a satisfying engineering problem.

It turns scattered glue code into **clean, declarative logic.**<br>
It scales with complexity. It cleans up after itself. It makes **architecture feel like gameplay.**

Spoke is the missing tool for making modular systems interconnect.

---

## 🔁 What can it do?

```csharp
public class MyActor : SpokeBehaviour {

    public State<bool> CanHear { get; } = State.Create(true);
    public Trigger<SoundStim> ReceiveSoundStim { get; } = Trigger.Create<SoundStim>();

    protected override void Init(EffectBuilder s) {

        var isActive = s.UseMemo(s =>
            s.D(IsEnabled) && s.D(ActorManager.Instance.IsEnabled)
        );

        s.UsePhase(isActive, s => {

            ActorManager.Instance.RegisterActor(this);
            s.OnCleanup(() => ActorManager.Instance.UnregisterActor(this));

            s.UsePhase(CanHear, ReactSoundStim);
        });
    }

    EffectBlock ReactSoundStim => s => {
        s.UseSubscribe(ReceiveSoundStim, evt => {
            if (evt.IsHostile) Chase(evt.Target);
        });
    };
}
```

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
                })
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

**Spoke is the crystallized form of everything I've learned.** Once it emerged in this form, all the complexity of my previous code evaporated. Suddenly, building a _modular-vehicle immersive simulator_ felt easy.

I believe Spoke is a general-purpose pattern for game programming with huge potential. It works across all code domains. It’s useful in any project, but it shines as complexity grows. The more deeply systems interact, the more value Spoke provides.

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
