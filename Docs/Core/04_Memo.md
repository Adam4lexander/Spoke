# Memo

This is the second most important reactive computation. A `Memo` is a close companion to the `Effect`. Where an `Effect` runs logic and causes side effects, a `Memo` computes and caches a value based on reactive dependencies.

You use `Memo` to transform multiple input signals into a single output signal.

The name **Memo** comes from _memoization_ — it represents a computed value that remembers its result and only updates when its reactive dependencies change.

---

## Creating a Memo

If you're using `SpokeBehaviour` you can create a `Memo` like this:

```csharp
public class MyBehaviour : SpokeBehaviour {

    [SerializeField] UState<string> firstName = UState.Create("Spokey");
    [SerializeField] UState<string> lastName = UState.Create("Spoke");

    protected override void Init(EffectBuilder s) {
        var fullName = s.Memo(s => $"{s.D(firstName) {s.D(lastName)}}");
        s.Effect(s => Debug.Log($"Fullname is {s.D(fullName)}"));
    }
}
```

When you run this code it will start by printing: "Fullname is Spokey Spoke". Changing either `firstName` or `lastName` in the inspector will cause the Memo to recalculate and trigger the Effect to log the value.

---

## `MemoBuilder`

The function passed to a `Memo` is sometimes called a _selector_ — it computes a new value from other signals.

`Func<MemoBuilder, T>`

The argument to this function, shown as little-_s_, is an instance of `MemoBuilder`. Let's look at that now:

```csharp
public interface MemoBuilder {

    T D<T>(ISignal<T> signal);
}
```

It only has the function `D()` for binding and reading a dynamic dependency. All other functions from `EffectBuilder` are missing. This reflects what `Memo` is for: **computing values** — not managing nested lifecycles.

## Example usage

```csharp
public class MyBehaviour : SpokeBehaviour {

    State<float> hitpoints = State.Create(10);
    State<bool> isParalyzed = State.Create(false);
    State<bool> isFrozen = State.Create(false);

    protected override void Init(EffectBuilder s) {

        var isAlive = s.Memo(s => s.D(hitpoints) > 0f);
        var isAnimating = s.Memo(s => s.D(isAlive) && !s.D(isParalyzed) && !s.D(isFrozen));

        s.Effect(s => {

            if (!s.D(isAnimating)) return; // exit if Effect remounts with isAnimating.Now=false

            PlayAnimations();
            s.OnCleanup(() => StopAnimations());
        });
    }
}
```

Memos are like transitive variables in imperative code. They build up to a derived state and tell a narrative along the way.
