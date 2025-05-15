# ⚙️ Performance

This page shows how Spoke performs — both in terms of CPU time and memory usage — and where the costs come from.

---

## Scenario 1: Flush Loop Stress Test

```csharp
public class FlushLoopStressTest : SpokeBehaviour {

    State<int> counter = State.Create(0);

    protected override void Init(EffectBuilder s) {
        const int nIterations = 100;

        var splitLeft = s.UseMemo(s => Mathf.FloorToInt(s.D(counter) / 2f));
        var splitRight = s.UseMemo(s => Mathf.CeilToInt(s.D(counter) / 2f));
        var recombine = s.UseMemo(s => s.D(splitLeft) + s.D(splitRight));
        var isDone = s.UseMemo(s => s.D(recombine) >= nIterations);

        s.UseEffect(s => {
            if (s.D(isDone)) return;
            counter.Update(x => x + 1);
        }, counter);
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) counter.Set(0);
    }
}
```

Pressing **spacebar** resets the counter to `0`, which kicks off a cascade of updates:

- A single flush is started.
- Each `counter.Update` increments the value and retriggers the flush loop — causing **100 total cycles.**
- Each cycle recomputes **four memos** and executes **one effect**.
- Every cycle runs dynamic dependencies, topological sort, and effect scheduling.

Measured results:

```
- 0.38ms flush time
-    0KB GC alloc
```

This is intentionally non-idiomatic usage — Spoke is designed to respond to meaningful, infrequent state changes, not to run tight computation loops.

In real-world usage, a typical flush might involve **1–10 recomputations**, not hundreds. This test forces Spoke through **500+ recomputations** in rapid succession, purely to probe the worst-case overhead.

It’s a pathological case — but it provides a useful stress baseline and demonstrates that Spoke remains stable, fast, and allocation-free even under extreme churn.

---

## Scenario 2: Dynamic Scope Mounting

This example reuses the same logic as before — but instead of being initialized once, it's mounted dynamically when a trigger fires:

```csharp
public class DynamicScopeMounting : SpokeBehaviour {

    Trigger runTestCommand = Trigger.Create();

    protected override void Init(EffectBuilder s) {
        s.UseEffect(RunTest(100), runTestCommand);
    }

    EffectBlock RunTest(int nIterations) => s => {
        var counter = State.Create(0);

        var splitLeft = s.UseMemo(s => Mathf.FloorToInt(s.D(counter) / 2f));
        var splitRight = s.UseMemo(s => Mathf.CeilToInt(s.D(counter) / 2f));
        var recombine = s.UseMemo(s => s.D(splitLeft) + s.D(splitRight));
        var isDone = s.UseMemo(s => s.D(recombine) >= nIterations);

        s.UseEffect(s => {
            if (s.D(isDone)) return;
            counter.Update(x => x + 1);
        }, counter);
    };

    void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) runTestCommand.Invoke();
    }
}
```

Pressing **spacebar** now mounts a _fresh scope_ with new reactive state. This means all `Memo` and `Effect` objects — as well as the backing `State` — are constructed at runtime.

Measured results:

```
- 0.43ms flush time
- 13.2KB GC alloc
```

This is where you pay the price: **allocations occur when you call functions like `UseEffect` and `UseMemo`, which create new reactive objects.** Once mounted, the system runs allocation-free — just like the previous example.

---

## Scenario 3: Unstable Dynamic Dependencies

This is a subtle source of GC worth mentioning — and one that’s easy to miss.

```csharp
s.UseEffect(s => {
    if (s.D(cond1)) return;
    if (s.D(cond2)) return;
    DoSomething();
});
```

Now imagine the following sequence:

- On the first run, `cond1.Now` is `false`, so only `cond1` is accessed.
- On the second run, `cond1` is `true`, so `cond2` is accessed for the first time.

Because the set of accessed dependencies has changed, Spoke needs to track a new one. Internally, this involves creating a small closure for each newly discovered dependency — which results in a GC allocation.

If the same dependencies are accessed in the same order across runs, Spoke reuses its previous setup and avoids any new allocations.

---

## 🧠 Takeaway

Spoke allocates when you:

- Call `State.Create`, `UseMemo`, `UseEffect`, or similar functions.
- Change dynamic dependencies on remount.

It does **not** allocate:

- State updates (`State.Update`, `State.Set`).
- Flush cycles and dependency propagation.
- Subscriptions (`UseSubscribe` and similar).

This gives you predictable cost boundaries: **mounting logic has a setup cost**, but once active, reactive flows stay **allocation-free.**

> **Note:** Closures (e.g. in `s.OnCleanup(() => { ... })`) will allocate if they capture local variables.
