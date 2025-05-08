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

- **Trigger** – Fire-and-forget pulse for one-shot updates

```csharp
var trigger = Trigger.Create();
trigger.Invoke();
```

- **State** – Reactive container for any value

```csharp
var isVisible = State.Create(true);
isVisible.Set(false);
```

- **Effect** / **Phase** / **Reaction** – All are kinds of 'Effect'. Forms an ownership hierarchy of IDisposable. Remounts from reactive signals

```csharp
s.UseEffect(s => renderer.sharedMaterial.color = s.D(ColourSignal));
s.UsePhase(isAlive, s => Debug.Log("I'm alive!"));
s.UseReaction(s => CheckIsGameOver(), PlayerHealth, NumberEnemies);
```

- **EffectBuilder** – Object passed into `EffectBlock` for conveniently mounting IDisposables

```csharp
s.UseEffect((EffectBuilder s) => {
    // Use[...] *means* to take ownership of a IDisposable
    s.Use(new MyCustomDisposable());
    s.UseSubscribe(someTrigger, evt => { /* ... */ });
    s.UseEffect(s => { /* ... */ });
});
```

- **Memo** – Derived reactive value (computed from state or triggers)

```csharp
var isAlive = s.UseMemo(s => s.D(health) > 0);
```

## 🧰 Requirements

- Unity 2021.3 or later (For Examples)
- No packages, no dependencies

## 📜 License

MIT — free to use in personal or commercial projects.
