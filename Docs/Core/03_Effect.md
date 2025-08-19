# Effect

In Spoke, the `Effect` is the most important primitive for expressing logic. Put simply it's a block of code, subscribed to a set of triggers. If one or more of the these triggers fire, the Effect is scheduled for remount.

Effects are a derivative of `Epoch` described in [00_MentalModel](./00_MentalModel.md).

Effects are functional blocks that combine _setup_ and _cleanup_ logic into one cohesive package. When the Effect is mounted, its code block is executed, which may register one or more cleanup functions. When the Effect is unmounted, these cleanup functions are executed in reverse order.

Effect blocks can create its own sub-effects or sub-epochs to form a lifecycle tree. The lifetimes of these children are bound to the parent effect. If the parent effect is to be unmounted, these children will be unmounted first.

There are three kinds of **Effect** in Spoke: `Effect`, `Reaction` and `Phase`. They are all syntactic sugar over the same underlying concept. Spoke could work with just `Effect`, but the others exist for convenience and improved clarity in your code.

---

## Creating an Effect

If you're using `SpokeBehaviour`, the `Init()` method is already mounted to an Effect. Creating additional sub-effects is easy:

```csharp
public class MyBehaviour : SpokeBehaviour {

    [SerializeField] UState<int> number = UState.Create(0);

    protected override void Init(EffectBuilder s) {
        // This function 'Init', already runs inside an Effect defined in SpokeBehaviour

        // Create a child Effect, bound to the lifetime of 'Init'
        s.Effect(s => {
            // Read 'number' and bind it as a dependency. So this function re-runs when 'number' changes
            var numberNow = s.D(number);
            Debug.Log($"number is: {numberNow}");
        });
    }
}
```

> `s.D()` reads and tracks a dynamic signal dependency, which I'll explain in a later section.

When you run the code, it will first print: `number is: 0`.

Each time you change the number in the Unity inspector, it will trigger the Effect to rerun, and print the updated value.

---

### Manual Creation

You can create effects yourself without having to use `SpokeBehaviour`:

```cs
// A reactive state we can test with
var number = State.Create(0);

// Spawn a `SpokeTree` by invoking the `SpokeRuntime`. The root epoch should be an engine. The `FlushEngine`
// is built for reactivity.
SpokeRuntime.SpawnTree(new FlushEngine(s => {
    s.Effect(s => {
        Debug.Log($"number is: {s.D(number)}");
    });
}));
```

This pretty much replicates what `SpokeBehaviour` is doing before it calls `Init`.

---

## Dependencies

There are two ways that dependencies on an `Effect` can be defined:

- **Explicit**: passing them explicitly in the constructor.
- **Dynamic**: auto-binding to `ISignal`'s accessed in the `EffectBlock`.

There are pros and cons to each. You can choose one or the other, or both, depending on your needs.

---

### Explicit Dependencies

```csharp
var myTrigger = Trigger.Create();
var myState = State.Create(0);

SpokeRuntime.SpawnTree(new FlushEngine(s => {
    s.Effect(s => {
        Debug.Log($"myState is {myState.Now}");
    }, myTrigger, myState); // any number of dependencies in final args
}));

// Instantiating effect Prints: myState is 0

myTrigger.Invoke(); // Prints: myState is 0
myState.Set(1);     // Prints: myState is 1
```

Any number of `ITrigger` can be given explicitly to the `Effect` constructor.

- **Advantages**: Works with `ITrigger`, can give more control and clarity
- **Disadvantages**: More verbose, requires more discipline, easy to forget a dependency

---

### Dynamic Dependencies

Dynamic dependencies are defined by calling a method from the `EffectBuilder`:

`T D<T>(ISignal<T> signal);`

```csharp
var name = State.Create("Spokey");
var age = State.Create(0);

SpokeRuntime.SpawnTree(new FlushEngine(s => {
    s.Effect(s => {
        Debug.Log($"name: {s.D(name)}, age: {s.D(age)}");
    });
});

// Instantiating effect Prints: name: Spokey, age: 0

name.Set("Reacts"); // Prints: name: Reacts, age: 0
age.Set(1);         // Prints: name: Reacts, age 1
```

`D()` is a method that wraps a `ISignal`. It makes that `ISignal` a dynamic dependency and then returns `ISignal.Now`.

If the `Effect` remounts, it will clear its dynamic dependencies, and then discover dependencies again on its next run. Dynamic dependencies can change on each run.

```csharp
SpokeRuntime.SpawnTree(new FlushEngine(s => {
    s.Effect(s => {
        // Totally fine
        if (s.D(condition)) {
            DoSomething(s.D(foo));
        } else {
            SomethingElse(s.D(bar));
        }
    });
});
```

- **Advantages**: Better ergonomics, more flexible, more powerful.
- **Disadvantages**: Only supports `ISignal`, easier to misuse.

I personally prefer dynamic dependencies. They feel better. They're more fun. And they handle complex logic more elegantly, like poking into nested signals.

That said, there are times where explicit dependencies are needed. Like if you need to remount an `Effect` from a pure event. Dynamic dependencies won’t catch pure events — only explicit dependencies will work in this case.

You can combine both styles freely — Spoke will track dynamic dependencies _and_ honor any explicit `ITrigger`s you pass in.

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
