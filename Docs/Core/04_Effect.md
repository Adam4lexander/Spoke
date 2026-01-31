---
title: Effect
---

## Table of Contents

- [Overview](#overview)
- [Creating an Effect](#creating-an-effect)
- [`Effect` vs `EffectBlock`](#effect-vs-effectblock)
- [Dependencies](#dependencies)
  - [Explicit Dependencies](#explicit-dependencies)
  - [Dynamic Dependencies](#dynamic-dependencies)
- [`EffectBuilder`](#effectbuilder)
  - [Mutation Windows](#mutation-windows)
  - [Why `s`?](#why-s)
  - [Composition Patterns](#composition-patterns)
- [Deferred Execution](#deferred-execution)
- [Execution Order](#execution-order)
- [Effect Variants](#effect-variants)
  - [`Phase`](#phase)
  - [`Reaction`](#reaction)
  - [`Effect<T>`](#effectt)
- [Advanced Notes](#advanced-notes)

---

## Overview

Effects are blocks of code, arranged into a tree, that rerun automatically when their dependencies change. The dependencies can be a mix of triggers and signals.

When an effect runs, it can make changes to game state, declare cleanup functions, bind resources and attach sub-effects and memos.

When an effect is rerun due to a dependency, it first cleans up everything from the last time. Resources are disposed, cleanup functions are invoked, and sub-effects are cleaned up as well. Think of it like Undo/Redo.

There are three kinds of Effect in Spoke:

- `Effect`: Standard effect behaviour
- `Phase`: Explicit `ISignal<bool>` parameter, when the effect can run
- `Reaction`: Skips the first run

`Phase` and `Reaction` are minor syntactic sugar over `Effect`. Spoke could work with just `Effect` and not the others, but they were included for convenience and improved clarity in your code.

---

## Creating an Effect

```csharp
public class MyBehaviour : SpokeBehaviour {

    [SerializeField] UState<int> number = UState.Create(0);

    protected override void Init(EffectBuilder s) {
        // Init itself is hosted in an Effect

        s.Effect(s => {
            // Declare 'number' a dependency, and store its value
            // This block reruns automatically whenever 'number' changes
            var numberNow = s.D(number);
            Debug.Log($"number is: {numberNow}");
        });
    }
}
```

> `s.D()` reads and tracks a dynamic signal dependency, we'll get to it later

---

## `Effect` vs `EffectBlock`

Effects come in two distinct pieces.

First there is the `Effect` class:

```cs
new Effect(/*...*/);
```

And then there is the `EffectBlock`:

```cs
EffectBlock block = (EffectBuilder s) => {
    DoTheThing();
    s.OnCleanup(() => UndoTheThing());
};
```

`EffectBlock` is a function delegate type. It's the code block assigned to the `Effect`. It's run once after the effect is created. It's run again (after cleanup) each time the effect is triggered by its dependencies.

```cs
// So, when you see:
s.Effect(s => {

});

// It's a convenience wrapper doing:
new Effect(s => {

});
```

`Effect` is a class which takes an `EffectBlock` as parameter. The class is the live object in the tree, and the block is its behaviour.

---

## Dependencies

There are two ways to declare dependencies on an `Effect`:

- **Explicit**: passing them explicitly in the constructor.
- **Dynamic**: auto-binding to `ISignal`'s accessed in the `EffectBlock`.

There are pros and cons to each. You can choose one or the other, or both, depending on your needs.

---

### Explicit Dependencies

```csharp
public class Adder : SpokeBehaviour {

    [SerializeField] UState<int> num1 = new(0);
    [SerializeField] UState<int> num2 = new(0);

    protected override void Init(EffectBuilder s) {
        s.Effect(s => {
            // ISignal.Now reads the current value
            Debug.Log($"Result is: {num1.Now + num2.Now}");
        }, num1, num2); // Dependencies given explicitely as parameters
    }
}
```

Explicit dependencies are anything extending `ITrigger`.

This option is least preferred, because it's less flexible and it's easy to include the wrong dependencies. But it's sometimes necessary, because this option can bind to triggers. The dynamic option only works with signals.

---

### Dynamic Dependencies

Dynamic dependencies are defined by calling a method from the `EffectBuilder`:

`T D<T>(ISignal<T> signal);`

```csharp
public class Adder : SpokeBehaviour {

    [SerializeField] UState<int> num1 = new(0);
    [SerializeField] UState<int> num2 = new(0);

    protected override void Init(EffectBuilder s) {
        s.Effect(s => {
            Debug.Log($"Result is: {s.D(num1) + s.D(num2)}");
        });
    }
}
```

`s.D()` wraps an `ISignal`, binds it as a dynamic dependency, and then returns `ISignal.Now`.

Dynamic dependencies are rebound each time the effect runs:

```cs
public class Adder : SpokeBehaviour {

    [SerializeField] UState<int> num1 = new(0);
    [SerializeField] UState<int> num2 = new(0);
    [SerializeField] UState<bool> addThird = new(true);
    [SerializeField] UState<int> num3 = new(0);

    protected override void Init(EffectBuilder s) {
        s.Effect(s => {
            if (s.D(addThird)) {
                Debug.Log($"Result is: {s.D(num1) + s.D(num2) + s.D(num3)}");
            } else {
                Debug.Log($"Result is: {s.D(num1) + s.D(num2)}");
            }
        });
    }
}
```

> Depending on the value of `addThird`, the effect may or may not declare `num3` as a dependency. This is a big advantage of dynamic dependencies. Expressing this with explicit dependencies is awkward.

Dynamic dependencies are the preferred approach. Their only limitation is that they only work with signals. They can't bind to raw triggers. But this use case is much more rare.

If you want, you can combine both styles. An effect can have a mixture of explicit and dynamic dependencies. Although for clarity, it's probably best to choose one or the other.

---

## `EffectBuilder`

`EffectBuilder` is the type of that `s` parameter passed into the `EffectBlock`:

```cs
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        // Init block has its own 's'
        // Use it to attach a sub-effect to Init
        s.Effect((EffectBuilder s) => {
            // Sub-effect is given its own 's'
        });
    }
}
```

Each `s` is a distinct `EffectBuilder` that's bound to the `Effect` that created it.

You use it to attach children, resources and cleanup callbacks:

```cs
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        // Attach sub-effect
        s.Effect(s => { });

        var num = State.Create(1);
        // Attach a memo
        var add1 = s.Memo(s => s.D(num) + 1);

        // Attach a cleanup callback
        s.OnCleanup(() => DoCleanup());

        // Attach an event subscription
        s.Subscribe(SomeEvent, () => HandleSomeEvent);

        // Attach an IDisposable resource
        s.Use(new SomeDisposableResource());
    }
}
```

Everything attached by `s` becomes owned by the `Effect` that provided `s`. When the effect reruns or cleans up, all of these attachments are cleaned up too.

---

### Mutation Windows

The `EffectBuilder` can only be used when the `Effect` invokes the `EffectBlock`. If you try to capture `s` and use it later, it will throw an error:

```cs
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        s.Subscribe(SomeEvent, () => {
            // When SomeEvent fires, try to attach a new effect
            s.Effect(s => { });
            // It will throw an exception, because SomeEvent is raised after Init() already finished
        });
    }
}
```

Everything attached to the `Effect` must be done synchronously while its `EffectBlock` is on the stack. Afterwards the `Effect` becomes sealed and its `s` no longer functions. Its only unsealed if a dependency causes it to rerun. But that's a complete rebuild, everything attached before is disposed.

To attach more effects outside the mutation window, you must use a `Dock`. [See Dock here.](./06_Dock.md)

---

### Why `s`?

I use the `s` character to represent the `EffectBuilder`, and also the `MemoBuilder`. If you look in the [Spoke Runtime Docs](./00_SpokeRuntime.md), you'll see it's used for `EpochBuilder` and `TickerBuilder` as well. Basically, it's used to represent all DSL-style builders in Spoke.

I chose `s` over other options like `ctx` or `effectBuilder` for a few reasons:

- It has low visual weight, so it doesn't steal attention from the actual Spoke commands
- Variable shadowing of `s` when effects are nested means there's no chance to accidentally use the wrong `EffectBuilder`

Spoke is like a little DSL embedded in C#. The `s` character subtly communicates: _This code is in Spoke's universe_.

---

### Composition Patterns

For complex Spoke code, you may want to break up your effects into re-usable blocks, so `Init` doesn't become a huge nested structure:

```cs
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        s.Effect(SomeBehaviour);
        s.Effect(ConfigurableBehaviour("Config"));
        s.Effect(ConfigurableBehaviour2("Config"));
    }

    // This is my preferred pattern for extracting re-usable chunks of Spoke code
    EffectBlock SomeBehaviour => s => {
        DoThing();
        s.OnCleanup(() => UndoThing());
    };

    // This is why it's my preferred form. It lets you parameterise the EffectBlock
    EffectBlock ConfigurableBehaviour(object config) => s => {
        DoThing(config);
        s.OnCleanup(() => UndoThing());
    };

    // This is exactly the same as the one before, only with less syntax tricks
    // The parameter 'config' is captured in a closure by the EffectBlock delegate
    // I prefer the '=> s => { }' pattern because its more concise
    EffectBlock ConfigurableBehaviour2(object config) {
        return (EffectBuilder s) => {
            DoThing(config);
            s.OnCleanup(() => UndoThing());
        };
    }
}
```

Nesting lambdas like: `() => s => { }` is a common pattern in the JavaScript frameworks that inspired Spoke. I'm not sure it's that common in `C#` though. Spoke takes many idioms from JavaScript and injects them into `C#`. It's not just to be clever, though. Lambdas and closures are the backbone that enable Spoke to be so expressive.

---

## Deferred Execution

Take a look at this example and try to predict the output:

```cs
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        s.Effect(s => {
            var number = 10;
            s.Effect(s => {
                Debug.Log($"number is {number}");
            });
            number = 20;
        });
    }
}
```

> `number` isn't a signal, so it can't be wrapped in `s.D()`

The log will print: `number is 20`

Why not `10`? Because effects aren't run synchronously. The outer effect first runs to completion:

- It declares `number = 10`
- It runs `s.Effect()`, to attach the inner effect, but doesnt run it yet
- It then sets `number = 20`

Only after the outer block completes does the inner block get to run. But by then `number` has already been updated. To fix it, you can rewrite like this:

```cs
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        s.Effect(s => {
            var number = 10;
            s.Effect(s => {
                Debug.Log($"number is {number}");
            });
            // Wrap the mutation in its own Effect, which runs after the one above
            s.Effect(s => {
                number = 20;
            });
        });
    }
}
```

Now it prints: `number is 10`

This behaviour can be surprising if you're not expecting it. But the fix is simple. To control the order of execution, sometimes you need to drop logic into its own effect.

---

## Execution Order

Effects and Memos are always executed in an imperative order:

```cs
public class MyBehaviour : SpokeBehaviour {

    [SerializeField] UState<int> myState = new(0);

    protected override void Init(EffectBuilder s) {

        s.Effect(s => {
            Debug.Log($"Outer-1: {s.D(myState)}");
            s.OnCleanup(() => Debug.Log("Outer-1 cleaned up"));

            s.Effect(s => {
                Debug.Log($"Inner-1: {s.D(myState)}");
                s.OnCleanup(() => Debug.Log("Inner-1 cleaned up"));
            });
        });

        s.Effect(s => {
            Debug.Log($"Outer-2: {s.D(myState)}");
            s.OnCleanup(() => Debug.Log("Outer-2 cleaned up"));

            s.Effect(s => {
                Debug.Log($"Inner-2: {s.D(myState)}");
                s.OnCleanup(() => Debug.Log("Inner-2 cleaned up"));
            });
        });
    }
}
```

Run the script and you'll see:

```
Outer-1: 0
Inner-1: 0
Outer-2: 0
Inner-2: 0
```

Change the number to `1` in the inspector and you'll see:

```
Inner-1 cleaned up
Outer-1 cleaned up
Outer-1: 1
Inner-1: 1

Inner-2 cleaned up
Outer-2 cleaned up
Outer-2: 1
Inner-2: 1
```

This is a core design principle of the Spoke runtime. Changes cascade by walking the tree in an imperative order. When an effect reruns, it disposes its subtree first in reverse attach order.

The [Spoke Runtime Docs](./00_SpokeRuntime.md) explain this in detail if you're curious. But it's not necessary to understand exactly how it works. Just know that you can use the same intuitions you have from imperative, procedural programming. No surprises from things randomly running out-of-order.

---

## Effect Variants

### `Phase`

The `Phase` is an Effect that takes an additional boolean signal to control when it runs:

```cs
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        s.Phase(IsEnabled, s => {
            // Logic when enabled
        });

        // Is syntax sugar over
        s.Effect(s => {
            if (!s.D(IsEnabled)) return;
            // Logic when enabled
        });
    }
}
```

The pattern is very common in Spoke. You could use `Effect` instead, but `Phase` helps to signal intention more clearly. Especially in blocks having many effects and phases.

---

### `Reaction`

The `Reaction` is an Effect that doesn't run on initial attachment. It waits for one of its triggers to fire, before running for the first time:

```cs
public class MyBehaviour : SpokeBehaviour {

    public Trigger OnDamaged = Trigger.Create();

    protected override void Init(EffectBuilder s) {
        s.Reaction(s => {
            // Play sound effect
        }, onDamaged);

        // Is syntax sugar over:
        var isFirst = true;
        s.Effect(s => {
            if (isFirst) {
                isFirst = false;
                return;
            }
            // Play sound effect
        }, onDamaged);
    }
}
```

`Reaction` is rarer than `Effect` and `Phase`. Often it's used to spawn sound and visual effects in response to events.

---

### `Effect<T>`

`Effect<T>` is an Effect that computes a reactive signal. It's a cross between an effect and a memo: it's composable like an effect, and computes a value like a memo.

```cs
public class Adder : SpokeBehaviour {

    [SerializeField] UState<int> num1 = new(0);
    [SerializeField] UState<int> num2 = new(0);

    protected override void Init(EffectBuilder s) {
        // Memos compute a value, which will be wrapped in an ISignal
        var resultFromMemo = s.Memo(s => s.D(num1) + s.D(num2));

        // Effect<T> computes a signal; the result is already wrapped
        var resultFromEffect = s.Effect(s => {
            var result = s.D(num1) + s.D(num2);
            return State.Create(result); // Return an ISignal
        });

        // Log the results from each, they will be the same
        s.Effect(s => {
            Debug.Log($"Result from Memo: {s.D(resultFromMemo)}");
            Debug.Log($"Result from Effect<T>: {s.D(resultFromEffect)}");
        });
    }
}
```

The distinction makes `Effect<T>` highly composable. It can attach its own sub-effects and memos to calculate a signal that's built up by its sub-tree. Put another way, memos compute raw values at the leaves of the tree; `Effect<T>` computes signals at the branches.

---

## Advanced Notes

- **Imperative Execution**: Nested effects always run in a top-to-bottom order. The same order they are attached.

- **Safe Cleanup**: Attachments are cleaned up in the reverse order they were attached.

- **Zero GC Leaves**: Leaf effects won't allocate GC on rerun if the `EffectBlock` is allocation-free.
