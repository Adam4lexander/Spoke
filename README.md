# Spoke

**Spoke** is a tiny declarative reactivity engine for Unity.

It helps you write gameplay logic that automatically responds to state changes — no custom events, manual update loops, or tangled condition checks required.

- ✨ **Declarative logic** — express behavior in terms of what should happen, not when to check
- 🧠 **Scoped effects** — logic mounts and cleans up automatically based on state.
- 🎯 **Deterministic and predictable** — flushes in a stable order every time
- 📦 **Drop-in simple** — just two files, no setup or dependencies
- 🧪 **Use it anywhere** — start small, add it to just one script, and grow from there

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

## ⚙️ Prefer manual control?

You can also create a `SpokeEngine` manually in any `MonoBehaviour`:

```csharp
using Spoke;

public class MyBehaviour : MonoBehaviour {

    SpokeEngine engine = new SpokeEngine(FlushMode.Immediate, new UnitySpokeLogger(this));
    State<bool> isEnabled = State.Create(false);
    Effect effect;

    void Awake() {
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

## 🧠 Core Concepts

- **Trigger** – The most basic reactive signal. Emits a one-shot event that remounts effects and recomputes memos.

```csharp
var damageTaken = Trigger.Create<DamageEvent>();
damageTaken.Invoke(new DamageEvent(/*...*/));
```

Triggers are fire-and-forget pulses.
They implement `ITrigger` / `ITrigger<T>`, so they can be subscribed to or used as dependencies in effects and memos.

- **State** – Reactive container for any value. When updated, it notifies dependent logic automatically.

```csharp
var isVisible = State.Create(true);
isVisible.Set(false); // Triggers effects or memos that depend on it
```

`State<T>` implements `ISignal<T>` and `ITrigger<T>`, making it usable as both a value and a reactive trigger.

- **Effect** / **Phase** / **Reaction** – Declarative logic blocks that mount, unmount, and remount automatically based on reactive state.

```csharp
s.UseEffect(s => renderer.sharedMaterial.color = s.D(ColourSignal)); // Always mounted

s.UsePhase(isAlive, s => Debug.Log("I'm alive!")); // Only mounted when condition is true

s.UseReaction(s => CheckIsGameOver(), PlayerHealth, NumberEnemies); // Mounts only after first dependency triggers
```

Effects can **own disposables**, including other effects, forming a nested ownership hierarchy.  
When any dependency changes, the effect is **fully remounted** — its previous logic is cleaned up, then re-executed.  
This keeps your logic in sync with state, and prevents stale behavior from lingering.

- **EffectBuilder** – Passed into every reactive block. Used to mount effects, subscriptions, and disposables within a scope.

```csharp
s.UseEffect((EffectBuilder s) => {
    // Use[...] *means* to take ownership of a IDisposable
    s.Use(new MyCustomDisposable());
    s.UseSubscribe(someTrigger, evt => { /* ... */ });
    s.UseEffect(s => { /* ... */ });
});
```

Every `Effect`, `Phase`, and `Reaction` receives an `EffectBuilder` —
it defines what logic is mounted, and ensures automatic cleanup when the scope ends.

- **Memo** – A computed signal. Automatically re-evaluates when any of its reactive dependencies change.

```csharp
var isAlive = s.UseMemo(s => s.D(health) > 0);
```

Memos are like derived values — they track the signals they access,
and update whenever those signals change.

### Dependency Tracking

> All reactive scopes (Effect, Phase, Reaction, Memo) can track dependencies either:
>
> - **Dynamically**, using `s.D(...)` inside the block
> - **Explicitly**, by passing signals as parameters

## 🧰 Requirements

- Unity 2021.3 or later (For Examples)
- No packages, no dependencies

## 📜 License

MIT — free to use in personal or commercial projects.
