# SpokeEngine

The `SpokeEngine` is the core runtime that powers reactive computation in Spoke. You'll rarely need to interact with the `SpokeEngine` directly — but it's the **most foundational part** of Spoke's behaviour.

Understanding it will complete your mental model of Spoke and make writing code more predictable.

The responsibilities of the `SpokeEngine` fall into three parts:

- **Scheduling**: Effects and Memos (both are computations) schedule themselves on the engine to be re-run.
- **Batching**: The engine defers flushes until the right moment, allowing it to batch multiple updates together efficiently.
- **Flushing**: It runs all scheduled computations in order, draining the queue until nothing remains.

---

## Scheduling

Effects and Memos are bound to a `SpokeEngine` when created, and schedules itself there whenever it re-runs.

Whenever an `Effect` or `Memo` is **created** — or when one of its dependencies **triggers** — it schedules itself onto its engine.
This means it's added to the `SpokeEngine`'s internal queue of scheduled computations, which will be flushed as part of the next update pass.

The one exception is `Reaction`: it doesn’t schedule itself when created.
Instead, it only schedules when one of its **explicit triggers** fires — which is exactly what makes it ideal for push-based, one-shot logic.

> The word schedule might sound misleading — it suggests the computation will run later, or on another thread.
> In Spoke, scheduling just means “putting it in a queue to run **soon**, usually within the same **call stack**.”
> We’ll clarify exactly how this works when we cover _Batching_ and _Flushing_.

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

Let's look at that same logic — but inside a reactive scope:

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

## Flushing

Once batching ends the `SpokeEngine` flushes all scheduled computations.

Flushing is what actually **executes** the work.
All the scheduling and batching before this point simply ensures that the work is done **once**, at the **right time**, in the **right order**.

---

### What gets flushed?

Anything that called `Schedule()` on the engine.

They're all added to the `SpokeEngine`'s **scheduled set**, and flushed together as a batch.

---

### What does `Flush()` actually do?

Calling `Flush()` directly is only needed in **Manual** flush mode.
If you're using `FlushMode.Immediate` (the default), flushes happen automatically when the batch ends.

---
