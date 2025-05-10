# SpokeEngine

The `SpokeEngine` is the core runtime that powers reactive computation in Spoke. You may rarely interact with it directly — but it's the **most important component** in the system.

Understanding it will complete your mental model of Spoke and make writing code more predictable.

The responsibilities of the `SpokeEngine` fall into three parts:

- **Scheduling**: Effects and Memos (both are computations) schedule themselves on the engine to be re-run.
- **Batching**: The engine defers flushes until the right moment, allowing it to batch multiple updates together efficiently.
- **Flushing**: It runs all scheduled computations in order, draining the queue until nothing remains.

---

## Scheduling

Remember how Effects and Memos each took a `SpokeEngine` as a parameter?

```csharp
var engine = new SpokeEngine(FlushMode.Immediate);

var effect = new Effect("MyEffect", engine, s => { /* ... */ });

var memo = new Memo("MyMemo", engine, s => { /* ... */ } );
```

Every computation is bound to a `SpokeEngine` at the time it's created. That engine is where it will schedule itself whenever it needs to run.

When you create Effects via `UseEffect()`, `UsePhase()`, or `UseReaction()` — or Memos via `UseMemo()` — they automatically inherit the `SpokeEngine` from the parent context.

```csharp
var engine = new SpokeEngine(FlushMode.Immediate);

var effect = new Effect("MyEffect", engine, s => {
    s.UseEffect(s => {
        var isSameEngine = s.SpokeEngine == engine; // True
    });
});
```

Whenever an `Effect` or `Memo` is **created** — or when one of its dependencies **triggers** — it schedules itself onto its engine.
This means it's added to a queue inside the `SpokeEngine`, ready to be flushed later as part of a batch.

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

1. **Explicit Batching** – You call `SpokeEngine.Batch(() => { ... }) to group updates manually.
2. **Internal Deferral** – The engine automatically defers flushing during certain internal operations (like `Trigger.Invoke`) to ensure safety and consistency.

Both mechanisms cause the engine to **defer a flush**.
Both are essential for keeping your logic efficient and deterministic.

Let's break them down.

---

### Explicit Batching

First let's see the problem it's trying to solve:

```csharp
var className = State.Create("Warrior");
var level = State.Create(1);

var effect = new Effect("MyEffect", engine, s => {
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

    protected override void Init(EffectBlock s) {

        var className = State.Create("Warrior");
        var level = State.Create(1);

        s.UseEffect(s => {
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
And during a flush, **all Effects and Memos are automatically deffered**.
Every `EffectBlock` is effectively inside its own `SpokeEngine.Batch()`.

That's why **manual batching only matters outside Spoke** — in external logic, like button handlers or coroutine steps.
Once a reactive trigger causes the engine to flush, **control will not return** until every computation in the queue has been drained.

---

## Flushing

Once batching ends — whether explicitly via `engine.Batch(...)`, or implicitly after internal deferral — the `SpokeEngine` flushes all scheduled computations.

Flushing is what actually **executes** the work.
All the scheduling and batching before this point simply ensures that the work is done **once**, at the **right time**, in the **right order**.

---

### What gets flushed?

Anything that called `Schedule()` on the engine:

- `Effect`s that remounted
- `Memo`s that were invalidated
- `Reaction`s that were triggered

Each of these gets added to the `SpokeEngine`'s **scheduled set**, and flushed together as a batch.

---

### Flush Order: Memos First, Then Effects

Flushes are **divided into two phases:**

1. **Memos flush first**
   - Sorted topologically (by dependency order)
   - Designed for efficient recomputation, **not** deterministic order
   - May flush multiple times in the same pass if dependencies cycle through updates
2. **Effects flush second**
   - Sorted by mount order (older effects flush before newer ones)
   - This gives effects a flush order that **resembles imperative code execution**
   - Think of it like running `Init()` methods top-down — parents flush before children, computations earlier in the block flush before siblings below.

This separation is crucial.

**Memos are allowed to be "noisy"** — re-running as needed until their values stabilize.
**Effects are "stable"** — they flush once per update pass and always in the same order.
This is why Spoke guarantees that **effects will never run with stale dependency values** — but **memos may re-run multiple times**, depending on the graph.

---

### What does `Flush()` actually do?

Calling `Flush()` directly is only needed in **Manual** flush mode.
If you're using `FlushMode.Immediate` (the default), flushes happen automatically when the batch ends.

Here’s a high-level sketch of what the flush logic looks like:

- Grab everything in the `scheduled` set
- Divide into `Memo` and `Effect` buckets
- For Memos:
  - Topologically sort by dependencies
  - Run each `Memo`
  - If new Memos are scheduled, repeat
- For Effects:
  - Sort by mount order
  - Run each `Effect`
  - If new Memos are scheduled, return to Memos

---

### Why it works this way

This two-phase model is what makes Spoke predictable:

- **Memos are allowed to "settle"**
- **Effects see final values**
- ** Flushes are atomic** — the queue is drained completely before any new flush can begin

It’s this flush behavior that lets you write deeply reactive logic without thinking about intermediate states.
You’ll never see half-updated values in your `Effect`s — only clean, consistent snapshots of the world.
