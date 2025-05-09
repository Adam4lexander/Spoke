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
