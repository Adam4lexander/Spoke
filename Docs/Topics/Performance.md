---
title: Performance
---

This page shows how Spoke performs, in terms of CPU usage and GC allocations.

---

## Table of Contents

- [Philosophy](#philosophy)
- [Scenario 1: Flush Loop Stress Test](#scenario-1-flush-loop-stress-test)
- [Scenario 2: Forcing GC](#scenario-2-forcing-gc)
- [Scenario 3: Unstable Dynamic Dependencies](#scenario-3-unstable-dynamic-dependencies)
- [Allocation Cheat Sheet](#allocation-cheat-sheet)

---

## Philosophy

First, I'll cover Spoke's performance philosophy.

Spoke was born inside a VR MechWarrior-style immersive sim that runs on the Meta Quest.

Its role is to orchestrate long-lived behaviours that spawn when the conditions are right, and that tear down when those conditions change. Basically, it's a lifecycle manager for behaviours that exist across frames, and that interact in emergent, dynamic ways.

For this use case, I found cognitive complexity to be a far greater bottleneck than CPU usage. On a given frame, not many lifecycle events occur, but those that do require careful orchestration to setup and tear down safely.

Spoke isn't intended to implement frame-by-frame logic; instead, it should orchestrate the systems that do. By starting them up, keeping them in sync with runtime state, and safely tearing them down.

Here are Spoke's guiding principles:

1. Prioritize an intuitive mental model and expressive syntax over micro-performance.

2. GC allocations are OK if they come from irregular lifecycle events (not every frame).

3. GC should be avoided at the tree leaves, where lifecycle events tend to be more frequent.

---

## Scenario 1: Flush Loop Stress Test

```csharp
public class FlushLoopStressTest : SpokeBehaviour {

    State<int> counter = State.Create(0);

    protected override void Init(EffectBuilder s) {

        const int nIterations = 100;

        var splitLeft  = s.Memo(s => Mathf.FloorToInt(s.D(counter) / 2f));
        var splitRight = s.Memo(s => Mathf.CeilToInt(s.D(counter) / 2f));
        var recombine  = s.Memo(s => s.D(splitLeft) + s.D(splitRight));
        var isDone     = s.Memo(s => s.D(recombine) >= nIterations);

        s.Effect(s => {
            if (s.D(isDone)) return;
            counter.Update(x => x + 1);
        }, counter);
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) counter.Set(0);
    }
}
```

Pressing **spacebar** kicks off a cascade of 500 updates (4 memos, 1 effect, 100 iterations). For Spoke's use case, this is a lot. Typical usage might have tens of computations on a frame, not hundreds.

Measured results:

```
- 0.38ms flush time
-    0KB GC alloc
```

> Measured on my laptop, in the Unity 2021 Editor

Why no GC?

Because the allocations already happened when `FlushLoopStressTest` was initialized. The reactive flow was set up before the spacebar was pressed.

The effect and the memos are all tree leaves. They will re-run without allocations, assuming there are no allocations inside their code blocks.

---

## Scenario 2: Forcing GC

_This example intentionally forces allocations to show the cost when subtrees are rebuilt._

```csharp
public class DynamicScopeMounting : SpokeBehaviour {

    Trigger runTestCommand = Trigger.Create();

    protected override void Init(EffectBuilder s) {
        s.Effect(RunTest(100), runTestCommand);
    }

    EffectBlock RunTest(int nIterations) => s => {
        var counter = State.Create(0);

        var splitLeft  = s.Memo(s => Mathf.FloorToInt(s.D(counter) / 2f));
        var splitRight = s.Memo(s => Mathf.CeilToInt(s.D(counter) / 2f));
        var recombine  = s.Memo(s => s.D(splitLeft) + s.D(splitRight));
        var isDone     = s.Memo(s => s.D(recombine) >= nIterations);

        s.Effect(s => {
            if (s.D(isDone)) return;
            counter.Update(x => x + 1);
        }, counter);
    };

    void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) runTestCommand.Invoke();
    }
}
```

This tweaks the previous test case. The reactive flow is wrapped in an effect that re-runs when you press **spacebar**, instantiating a fresh subtree.

This lets us see how much GC was allocated by the **Init** phase in the previous test.

Measured results:

```
- 0.43ms flush time
- 13.2KB GC alloc
```

GC is allocated when tree branches are rebuilt. The allocation sources are:

- Instantiating `Effect` and `Memo` classes.
- Capturing closures for `EffectBlock` and `MemoBlock`.

---

## Scenario 3: Unstable Dynamic Dependencies

This is a subtle source of GC worth mentioning.

```csharp
s.Effect(s => {
    if (s.D(cond1)) return;
    if (s.D(cond2)) return;
    DoSomething();
});
```

Imagine this sequence:

- On the first run, `cond1` is `false`, so only `cond1` is bound as a dependency.
- On the second run, `cond1` is `true`, so `cond2` is bound for the first time.

Between runs, the list of dynamic dependencies changes. For newly bound dependencies, Spoke allocates a small closure to track them, causing a small GC allocation.

If the same dependencies are accessed in the same order across runs, Spoke reuses its previous setup and avoids any new allocations.

---

## Allocation Cheat Sheet

Spoke allocates when you:

- Call `State.Create`, `s.Memo`, `s.Effect`, or similar functions.
- Change dynamic dependencies on rerun.

It does **not** allocate when you:

- Update state (`State.Update(...)`, `State.Set(...)`).
- Re-run effects or memos.
- Subscribe / unsubscribe from triggers.

> **Note:** Closures (e.g. in `s.OnCleanup(() => { ... })`) will allocate if they capture local variables.
