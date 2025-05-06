# Spoke

Spoke is a tiny, declarative reactivity engine for Unity.

It helps you write gameplay logic that automatically responds to changes in game state — without needing to write custom events, update loops, or spaghetti condition checks.

- ✨ React to state changes without boilerplate
- 🧠 Smart, efficient updates with full determinism
- 🧩 Integrates via `SpokeBehaviour` — no setup required
- ⚡ Just two C# files. Drop it in and go.

## Example

```csharp
public class MyActor : SpokeBehaviour {
    State<float> health = State.Create(100f);

    protected override void Init(EffectBuilder s) {

        s.UsePhase(IsStarted, s => {
            Debug.Log($"Actor spawned with {s.D(health)} HP");

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
