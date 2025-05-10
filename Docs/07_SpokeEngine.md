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

---

## Flushing
