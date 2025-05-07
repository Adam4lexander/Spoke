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
    State<float> health = State.Create(100f);

    protected override void Init(EffectBuilder s) {

        s.UsePhase(IsStarted, s => {
            Debug.Log($"Actor spawned with {health.Now} HP");

            var isAlive = s.UseMemo(s => s.D(health) > 0);

            s.UsePhase(isAlive, s => {
                Debug.Log("Actor is alive and active");
                s.OnCleanup(() => Debug.Log("Actor is no longer active"));
            });
        });
    }

    public void TakeDamage(float amount) {
        health.Set(health.Now - amount);
    }
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
