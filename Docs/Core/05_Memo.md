# Memo

## Table of Contents

- [Overview](#overview)
- [Creating a Memo](#creating-a-memo)
- [`Memo<T>` vs `MemoBlock<T>`](#memot-vs-memoblockt)
- [Dependencies](#dependencies)
- [`MemoBuilder`](#memobuilder)
- [Pure Functions](#pure-functions)
- [Example usage](#example-usage)

---

## Overview

Memos are blocks of code in the tree of effects, that calculate and return a value anytime their dependencies change. As with effects, dependencies can be a mix of triggers and signals.

The value produced by the memo is wrapped in an `ISignal`, so it can be used as a dependency in later effects and memos.

Besides calculating values, memos behave identically to effects. They're both nodes in the tree, executed in imperative order, following the same lifecycle rules. Sometimes reactive engines give memos (or their equivalent) separate execution rules to support optimised dataflows. Spoke doesn't do this. Memos are just another node in the tree with the same execution rules as effects.

---

## Creating a Memo

```csharp
public class Adder : SpokeBehaviour {

    [SerializeField] UState<int> num1 = new(0);
    [SerializeField] UState<int> num2 = new(0);

    protected override void Init(EffectBuilder s) {

        // result will be an ISignal<int>, updated automatically when the inputs change
        var result = s.Memo(s => s.D(num1) + s.D(num2));

        s.Effect(s => {
            Debug.Log($"Result is {s.D(result)}");
        });
    }
}
```

> `s.D()` is explained in the [Effect docs](./04_Effect.md#dynamic-dependencies)

---

## `Memo<T>` vs `MemoBlock<T>`

Just like effects, Memos come in two distinct pieces.

There is the `Memo<T>` class:

```cs
new Memo<T>(/*...*/);
```

And then there is the `MemoBlock<T>`:

```cs
MemoBlock<int> block = (MemoBuilder s) => {
    return s.D(num1) + s.D(num2);
}
```

`MemoBlock<T>` is a function delegate assigned to the `Memo<T>`. Just like an `EffectBlock` is assigned to an `Effect`.

`MemoBlock<T>` is run once after the memo is created, which initializes the signal's value. Then it's run again each time any dependencies change.

---

## Dependencies

`Memo<T>` dependencies work exactly the same as for effects. The docs for `Effect` [explains it here](./04_Effect.md#dependencies).

Memos support both **Explicit** and **Dynamic** dependencies, although it would be unusual to use the explicit form. The point of memos is combining multiple input signals into a single output signal. which makes dynamic dependencies the better fit.

---

## `MemoBuilder`

`MemoBuilder` is the type of the `s` parameter passed into the `MemoBlock<T>`.

It has far less functionality compared to `EffectBuilder`, only offering two functions:

- `s.D()`: For reading signals and binding them as dynamic dependencies
- `s.OnCleanup()`: For cleaning up on reruns

Although `s.onCleanup()` is provided it would be unusual to need it. Memos are intended to run pure functions without side-effects, so there shouldn't be anything to clean up.

---

## Pure Functions

Memos are intended to run pure functions, which are functions without side-effects:

```cs
// This is a pure function
s.Memo(s => s.D(num1) + s.D(num2));

// This isn't a pure function. It modifies application state.
s.Memo(s => {
    WorldState.SomeValue += 1;
    s.OnCleanup(() => WorldState.SomeValue -= 1);
    return "whatever";
});
```

In Spoke, there's nothing stopping you running impure functions in Memos. Other reactive engines (often in JavaScript) do have real technical reasons why you shouldn't cause side effects in memos. But since Spoke runs memos the same as effects, its just a convention.

---

## Example usage

Here's an example resembling actual game logic that demonstrates memos. It's a script controlling animation. The animation should only play when the character is alive, it's not paralyzed and it's not frozen.

```csharp
public class AnimationController : SpokeBehaviour {

    [SerializeField] UState<float> hitpoints = new(10);
    [SerializeField] UState<bool> isParalyzed = new(false);
    [SerializeField] UState<bool> isFrozen = new(false);

    protected override void Init(EffectBuilder s) {

        var isAlive = s.Memo(s => s.D(hitpoints) > 0f);

        var isAnimating = s.Memo(s => s.D(isAlive) && !s.D(isParalyzed) && !s.D(isFrozen));

        s.Effect(s => {
            if (!s.D(isAnimating)) return; // no animations when isAnimating is false
            PlayAnimations();
            Debug.Log("Animation is playing");
            s.OnCleanup(() => Debug.Log("Animation is stopped"));
        });
    }
}
```

Memos are like transitive variables in imperative code. They build up to a derived state and tell a narrative along the way.
