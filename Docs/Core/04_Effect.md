# Effect

Effects are block of code, arranged into a tree, that rerun automatically when their dependencies change. These dependencies can be a mix of triggers and signals.

When an effect runs, it makes changes to game state, declares cleanup functions, binds resources and attaches sub-effects and memos.

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

First there is the the `Effect` class:

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

`EffectBlock` is a function delegate type. It's the code block assigned to the `Effect`. It's run once when the effect is created and mounted. It's run again (after cleanup) each time the effect is triggered by its dependencies.

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

> `number` isn't a signal, so it can't be wrapped in s.D()

The log will print: `number is 20`

Why not `10`? Because effects aren't run synchronously. The outer effect first runs to completion:

- It declares `number = 10`
- It runs `s.Effect()`, to attach the inner effect, but doesnt run it yet
- It then sets `number = 20`

Only after the outer block completes, does the inner block get to run. But by then `number` has already been updated. To fix you can rewrite like this:

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

This option is least preferred, because it's less flexible and easy to put the wrong dependencies. But it's sometimes necessary, because this option can bind to triggers. The dynamic option only works with signals.

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

If you want you can combine both styles. An effect can have a mixture of explicit and dynamic dependencies. Although for clarity, it's probably best to choose one or the other.

---

## `EffectBlock`

The function passed into the Effects constructor is the type `EffectBlock`. It's a delegate function type shown below.

```csharp
public delegate void EffectBlock(EffectBuilder s);
```

An `EffectBlock` is a function that takes an `EffectBuilder` as parameter. It's the block of code that's mounted by an Effect. It can take several different forms:

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        // Init is an EffectBlock

        s.Effect(s => {
            // This lambda is an EffectBlock
        });

        s.Effect(MyFirstEffect);
        s.Effect(MySecondEffect);
        s.Effect(MyEffectWithParam("Hello"));
    }

    void MyFirstEffect(EffectBuilder s) {
        // This is an EffectBlock
    }

    EffectBlock MySecondEffect => s => {
        // This is an EffectBlock too. It's roughly equivalent in form to 'MyFirstEffect'
        // I prefer this form, even though the double lambda syntax might looks weird
        // It stands out next to regular non-Spoke methods.
    };

    EffectBlock MyEffectWithParam(string msg) => s => {
        // This EffectBlock demonstrates an advantage of using double-lambdas.
        // The pattern lets you parameterise the EffectBlock.
        // The parameters are stored in a closure. And the EffectBlock returned is bound to that closure.
        // I use this pattern a lot.
    };
}
```

Each form has one thing in common. It's a function that takes an `EffectBuilder` as a parameter. Next, let's take a look at what the `EffectBuilder` is.

## `EffectBuilder`

The `EffectBuilder` is an object with a DSL-style interface for nesting reactive logic. When an Effect is created, it internally creates an `EffectBuilder` that's bound to it. When the Effect mounts, it calls the `EffectBlock` function and passes it's internal `EffectBuilder` into it.

During the scope of the `EffectBlock` function, the Effect unseals itself for modification. This is the **only** chance to modify the Effect, in this mount cycle, before the Effect seals itself again. When the `EffectBlock` returns, the `EffectBuilder` becomes ineffective. Trying to use it will throw an exception.

Here is a simplified view what the `EffectBuilder` interface exposes:

```csharp
public interface EffectBuilder {

    // Engage the FlushLogger to log the current flush to console
    void Log(string msg);

    // Track a dynamic dependency and return its value
    T D<T>(ISignal<T> signal);

    // Take ownership of IDisposable objects, and dispose them when I unmount
    T Use<T>(T disposable) where T : IDisposable;
    // A SpokeHandle is a disposable struct with zero GC
    void Use(SpokeHandle trigger);

    // Subscribe to an ITrigger, unsubscribe when I unmount
    void Subscribe(ITrigger trigger, Action action);
    void Subscribe<T>(ITrigger<T> trigger, Action<T> action);

    // Create nested Epochs
    ISignal<T> Memo<T>(Func<MemoBuilder, T> selector, params ITrigger[] triggers);
    void Effect(EffectBlock func, params ITrigger[] triggers);
    void Reaction(EffectBlock action, params ITrigger[] triggers);
    void Phase(ISignal<bool> mountWhen, EffectBlock func, params ITrigger[] triggers);
    IDock Dock();

    // Register functions to run when I unmount
    void OnCleanup(Action cleanup);
}
```

---

## What is with little-_s_

You might have noticed I repeatedly use _s_ as the parameter name in the `EffectBlock`.

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        s.Effect(s => {
            s.Effect(s => {
                s.Phase(IsEnabled, s => { });
            });
        });

        var num = s.Memo(s => {
            // I use it here too, which isn't even an EffectBuilder! It's a MemoBuilder!
            return 0;
        });
    }
}
```

This is a convention I’ve adopted across all Spoke code. The _s_ stands for _Scope_ — or if you like, _Spoke_. It's the soul of the system. When you see a function that takes a little-_s_ you **know** you're in a reactive function.

There's a practical reason too. It prevents accidental leakage of `EffectBuilder`'s down the hierarchy.

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder outerBuilder) {

        outerBuilder.Effect(innerBuilder => {
            outerBuilder.Effect(/*...*/) // Oops wrong builder!
        });
    }
}
```

Spoke would catch that mistake and throw an error — but it’s better to avoid it altogether. Besides, trying to think of a good parameter name for an `Effect` three levels deep gets hard.

The little-_s_ in comparison is unobtrusive yet immediately recognisable, and lets you focus on what matters.

---

## Phase

The `Phase` is an Effect that takes an additional boolean signal to control when it mounts.

### Example

```cs
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        // IsEnabled is an ISignal<bool> exposed by SpokeBehaviour
        s.Phase(IsEnabled, s => {
            // Run code when enabled
            s.OnCleanup(() => {
                // Run code when disabled
            });
        });

        // Is roughly equivalent to this form using a regular Effect
        s.Effect(s => {
            if (s.D(IsEnabled)) return;
            // Run code when enabled
            s.OnCleanup(() => {
                // Run code when disabled
            });
        });
    }
}
```

`Phase` is a simple syntactic wrapper that targets an extremely common use-case in Spoke. It was made a discrete object type for improving overall code clarity.

---

## Reaction

The `Reaction` is a type of Effect that doesn't mount on initial attachment. It waits for one of its triggers to fire, before mounting for the first time.

## Example

```cs
public class MyBehaviour : SpokeBehaviour {

    public Trigger OnDamaged = Trigger.Create();

    protected override void Init(EffectBuilder s) {
        s.Reaction(s => {
            // Play damage effect
        }, onDamaged);

        // Is roughly equivalent to this form:
        var isFirst = true;
        s.Effect(s => {
            if (isFirst) {
                isFirst = false;
                return;
            }
            // Play damage effect
        }, onDamaged);
    }
}
```

Due to `Reaction` skipping the first mount, it's typically used with explicit dependencies only. Dynamic dependencies would all be skipped initially, only an explicit dependency could trigger it for the first time.

Use cases for Reaction are less common, but it comes in handy for one-shot logic like: playing an effect on damage, or triggering an announcer phrase in response to a game event.

---

## Advanced Notes

- **Deferred Execution**: Nested Effects are scheduled and deferred, then executed in order.

- **Deterministic Flush**: Effects flush after Memos, in mount order. Execution is intuitive and consistent.

- **Safe Cleanup**: Cleanup functions run in reverse order, then child `IDisposable`s are disposed in reverse.

- **Zero GC Leaves**: Leaf Effects won't allocate GC on remount if the `EffectBlock` is allocation-free.
