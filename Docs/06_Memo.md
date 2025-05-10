# Memo

This is the second most important reactive computation. A `Memo` is a close companion to the `Effect`. Where an `Effect` runs logic and causes side effects, a `Memo` computes and caches a value based on reactive dependencies.

The name **Memo** comes from _memoization_ — it represents a computed value that remembers its result and only updates when its reactive dependencies change.

Unlike `Effect`, `Memo` has a different update model — it prioritizes efficient recalculation over deterministic flush order. The `SpokeEngine` sorts and flushes Memos in a separate phase to Effects. But we'll get to that later.

---

## Creating a Memo

Although generally not needed, you can create a `Memo` manually like this:

```csharp
var engine = new SpokeEngine(FlushMode.Immediate);

var memo = new Memo("MyMemo", engine, s => s.D(signalA) + s.D(signalB));

// Or equivalently using explicit dependencies
var memo = new Memo("MyMemo", engine, s => {
    return signalA.Now + signalB.Now;
}, signalA, signalB);

memo.Dispose();
```

Assuming `signalA` and `signalB` were both an `ISignal<int>`, that would mean that `memo` is an `ISignal<int>` too.

You can create Memos with the `EffectBuilder` too:

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {

        var memo = s.UseMemo(s => s.D(signalA) + s.D(signalB));
    }
}
```

> This shows how clean and expressive dynamic dependencies can be.

Generally you'll create Memos with `UseMemo()`, which means the memo is auto-cleaned when the parent `Effect` unmounts.

---

## `MemoBuilder`

The function passed to a `Memo` is sometimes called a _selector_ — it computes a new value from other signals.

`Func<MemoBuilder, T>`

The argument to this function, shown as little-_s_, is an instance of `MemoBuilder`. Let's look at that now:

```csharp
public interface MemoBuilder {
    T D<T>(ISignal<T> signal);
    T Use<T>(T disposable) where T : IDisposable;
}
```

It's an **extremely** cut down version of the `EffectBuilder`. It reflects what `Memo` is for: **computing values** — not managing nested lifecycles.

The `Use()` method is provided **just in case** the `Memo` is calculating a disposable value. Although this usecase should be uncommmon.

## Usage

You use `Memo` to transform multiple input signals into a single output signal.

```csharp
var myCash = State.Create(100);
var partnersCash = State.Create(110);

var totalCash = s.UseMemo(s => s.D(myCash) + s.D(partnersCash));

var canBuyHouse = s.UseMemo(s => {
    if (s.D(totalCash) > 1000000) return "No!";
    else return "Double No!";
});

s.UseEffect(s => Debug.Log($"Can we buy a house? -- {s.D(canBuyHouse)}"));
```

Changing either `myCash` or `partnersCash` will trigger a cascade of changes across the Memos. A Memo will only trigger if its selector function produces a value that was different from the previous one.

> Similar to `State` value comparison is given by `EqualityComparer<T>.Default`.

**Only once this couple's `totalCash` exceeds** _1000000_ will the `canBuyHouse` memo recompute into a more softly spoken _No_.

Anyway, let's turn down the existential dread a bit and move on to a more realistic example.

```csharp
public class MyBehaviour : SpokeBehaviour {

    State<float> hitpoints = State.Create(10);
    State<bool> isParalyzed = State.Create(false);
    State<bool> isFrozen = State.Create(false);

    protected override void Init(EffectBuilder s) {

        var isAlive = s.UseMemo(s => s.D(hitpoints) > 0f);
        var isAnimating = s.UseMemo(s => s.D(isAlive) && !s.D(isParalyzed) && !s.D(isFrozen));

        s.UseEffect(s => {
            if (!s.D(isAnimating)) return; // exit if Effect remounts with isAnimating.Now=false

            PlayAnimations();
            s.OnCleanup(() => StopAnimations());
        });
    }
}
```

Memos are like transitive variables in imperative code. They build up to a derived state and tell a narrative along the way.

---

## ⚠️ Mis-Usage

The number one rule when using Memos is to make them pure functions without side effects.

```csharp
// This is fine!
s.UseMemo(s => s.D(num1) + s.D(num2));

// Don't do this
s.UseMemo(s => {
    if (s.D(isHit)) PlaySound(); // <-- Bad: side effect!
    return s.D(health) - 10;
})
```

All side effect code should go into Effects instead. The reason why is tricky to explain, but I'll do my best:

I said before that Memos and Effects are scheduled and flushed in separate phases. Memos are optimized for reactive performance, Effects are optimized for intuition and determinism.

For complex graphs of Memos, Spoke makes no guarantee what order the Memos are run, or how many times the Memo is rerun. For example:

```csharp
var num = State.Create(100);

var half = s.UseMemo(s => s.D(numState) / 2);
var quarter = s.UseMemo(s => s.D(numState) / 4);

var sum = s.UseMemo(s => s.D(half) + s.D(quarter));
```

When `num` is changed, what order will the memos run, and how many times will `sum` run?

Well unfortunately this is a simple enough example that it will behave deterministically:

- Order will be `half`, `quarter` then `sum`.
- `sum` will be run once.

**But** if this was a more complex graph then it may not behave deterministically:

- First execution will run `half`, then `quarter`, then `sum`
- Second execution will run `quarter`, then `sum`, then `half` and then `sum` once more.

So what does a more complex graph look like?

```csharp
var num = State.Create(100);

var halfState = State.Create(num.Now / 2);
var quarterState = State.Create(num.Now / 4);

s.UseEffect(s => {
    var half = s.UseMemo(s => s.D(num) / 2);
    s.UseSubscribe(half, halfState.Set);
}, someTrigger);

s.UseEffect(s => {
    var quarter = s.UseMemo(s => s.D(num) / 4);
    s.UseSubscribe(quarter, quarterState.Set);
}, someOtherTrigger);

var sum = s.UseMemo(s => s.D(halfState) + s.D(quarterState));
```

This is what makes the issue tricky to explain:
It only shows up in patterns that are too complex to demonstrate cleanly.
The problem emerges when you start chaining together Memos, States, and Subscriptions — or wiring Memos into large diamond-shaped graphs with overlapping dependencies.

But this isn’t really a flaw in `Memo`.
It’s a natural consequence of chaining event handlers into complex graphs.
Memos actually provide **more determinism guarantees** than regular events.
The difference is that `Effect` scopes are **provably safe**, even in tangled graphs.
And because reactive frameworks make it _so easy_ to wire things together, you’ll often find yourself building unsafe structures _without realizing it_.
That’s why the rule is simple:
**Side effects go in `Effect`. Values go in `Memo`.**

---

## Advanced Notes

- **Topological Sorting**: Memos are sorted topologically relative to their dependencies, and run in order. But Spoke **only sorts the dirtied portion of the graph** — not the entire system. That’s why complex graphs with overlapping dependencies can behave non-deterministically.
