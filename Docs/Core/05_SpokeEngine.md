# SpokeEngine

The `SpokeEngine` is an implementation of `ExecutionEngine`, whose behaviour is described in detail here: [Tree-Based Execution](./00_MentalModel.md#tree-based-execution). Therefore this page will only cover `SpokeEngine` specific behaviour that's not part of the base `ExecutionEngine`.

When epochs are attached to a call-tree, they schedule themselves on the nearest `ExecutionEngine`. Unless you're hacking deep in Spoke, this will be a `SpokeEngine`, and it will be hosted by the root node of the tree.

When the engine begins a flush, it will synchronously execute all scheduled epochs until nothing remains to be done. The engine can be configured to flush immediately when an epoch is scheduled, or it can be controlled manually.

---

## Usage with SpokeBehaviour

Each `SpokeBehaviour` creates it's own personal `SpokeEngine`, that it mounts the `Init` Effect to. This default engine is configured with `FlushMode.Immediate`.

---

## Batching

_Batching_ means grouping multiple reactive updates together before the `SpokeEngine` flushes any computations.
Put simply, batching tells the engine: **"Don't flush yet."**

In Spoke, batching happens in two ways:

1. **Explicit Batching** – You call `SpokeEngine.Batch(() => { ... })` to group updates manually.
2. **Internal Deferral** – The engine automatically defers flushing during certain internal operations (like `Trigger.Invoke`) to ensure safety and consistency.

Both mechanisms cause the engine to **defer a flush**.
Both are essential for keeping your logic efficient and deterministic.

Let's break them down.

---

### Explicit Batching

First let's see the problem it's trying to solve:

```csharp
var engine = SpokeEngine.Create(FlushMode.Immediate, new UnitySpokeLogger());

var className = State.Create("Warrior");
var level = State.Create(1);

engine.Effect(s => {
    Debug.Log($"class: {s.D(className)}, lvl: {s.D(level)}");
});                       // Prints: class: Warrior, lvl: 1

className.Set("Paladin"); // Prints: class: Paladin, lvl: 1
level.Set(2);             // Prints: class: Paladin, lvl: 2
```

Creating the `Effect` caused a flush. So did each call to `Set(...)`.
Result: **3 flushes, 3 recomputations, 3 logs.**

Now let’s say you want to update both values together, and flush only once:

```csharp
engine.Batch(() => {
    className.Set("Paladin");
    level.Set(2);
});                       // Prints: class: Paladin, lvl: 2
```

**Batching is necessary** when **non-reactive code** wants to make multiple state updates as a single atomic operation.
In the next section, I'll explain why I emphasize **non-reactive code**.

---

### Internal Deferral

Let's look at that same logic, but inside a reactive scope:

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {

        var className = State.Create("Warrior");
        var level = State.Create(1);

        s.Effect(s => {
            Debug.Log($"class: {s.D(className)}, lvl: {s.D(level)}");
        });

        className.Set("Paladin");
        level.Set(2);
    }
}
```

This prints a **single message**:
`class: Paladin, lvl: 2` — even though I didn’t call `Batch()`.

Why?

Because we're already **inside a flush**.
The `Init` method is an `EffectBlock`, which means it only runs **during** a flush.
And during a flush, **all Effects and Memos are automatically deferred**.
Every `EffectBlock` is effectively inside its own `SpokeEngine.Batch()`.

That's why **manual batching only matters outside Spoke** — in external logic, like button handlers or coroutine steps.
Once a reactive trigger causes the engine to flush, **control will not return** until every computation in the queue has been drained.

---
