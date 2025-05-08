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
            s.D(IsEnabled) && s.D(ActorManagar.Instance.IsEnabled)
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

## 🧠 Core Concepts

- **State** – Reactive container for any value
- **Trigger** – Fire-and-forget pulse for one-shot updates
- **Memo** – Derived reactive value (computed from state or triggers)
- **EffectBuilder** – Creates modular, self-cleaning logic blocks
- **UseEffect** / **UsePhase** / **UseReaction** – Declarative control over when logic mounts

All logic is structured around **scopes**: once a condition stops being true, the associated logic is automatically cleaned up.

## 🧰 Requirements

- Unity 2021.3 or later (For Examples)
- No packages, no dependencies

## 📜 License

MIT — free to use in personal or commercial projects.
