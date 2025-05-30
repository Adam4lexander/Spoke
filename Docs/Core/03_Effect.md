# Effect

Now we get to the good stuff. The `Effect` is where you will write almost all of your reactive game logic.

In reactive programming, an _effect_ refers to any logic that reacts to state changes and might cause side effects — like updating a component, spawning an entity, or applying damage. It’s the boundary between declarative reactivity and imperative action.

In Spoke an `Effect` can be understood as follows:

- It's an object which can be _mounted_ and _disposed_.
- It's a container for child objects which implement `IDisposable`.
- Those children might also be `Effect`s.
- When an `Effect` is disposed, all its descendants will be disposed too.

There are three kinds of **Effect** in Spoke: `Effect`, `Reaction` and `Phase`. They are all syntactic sugar over the same underlying concept. Spoke could work with just `Effect`, but the others exist to make your intent clearer, your code shorter, and your mental model sharper.

---

## Creating an Effect

If you're using `SpokeBehaviour`, you may never need to create an `Effect` manually. But here’s how it's done:

```csharp
// We'll get to SpokeEngine later
var engine = new SpokeEngine(FlushMode.Immediate);

var effect = new Effect("MyEffect", engine, s => {
    // EffectBuilder logic
});

// ...

effect.Dispose();
```

The constructor for `Effect` is:

`public Effect(string name, SpokeEngine engine, EffectBlock block, params ITrigger[] triggers)`

Let's break that down:

- `name`: Appears in debugging views.
- `engine`: We'll come back to this when we cover SpokeEngine.
- `block`: A function that takes a builder object and creates the `Effect`.
- `triggers`: Are explicit dependencies that remount the `Effect` when they trigger.

---

## `EffectBlock`

```csharp
public delegate void EffectBlock(EffectBuilder s);
```

An `EffectBlock` is a function that takes an `EffectBuilder` as parameter.

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        // Init is an EffectBlock

        var effect = new Effect("innerEffect", s.Engine, s => {
            // This lambda is an EffectBlock
        });
        s.Use(effect); // so effect is auto-cleaned

        s.UseEffect(s => {
            // This lambda is also an EffectBlock
        });

        s.UseEffect(MyFirstEffect);
        s.UseEffect(MySecondEffect);
        s.UseEffect(MyEffectWithParam("Hello"));
    }

    void MyFirstEffect(EffectBuilder s) {
        // This is an EffectBlock
    }

    EffectBlock MySecondEffect => s => {
        // This is an EffectBlock too
    };

    EffectBlock MyEffectWithParam(string msg) => s => {
        // And this too is an EffectBlock
    };
}
```

That's a lot of patterns, and I'm hinting at what `EffectBuilder` is too. The takeaway is: `EffectBlock` shows up _everywhere_ in Spoke. Whether it's written inline, passed around as a lambda, or stored as a function — you're always working with one.

Let’s take a look at what the `EffectBuilder` actually is.

## `EffectBuilder`

When an `Effect` 'mounts' it runs its `EffectBlock` and passes in an `EffectBuilder`. This is the `Effect`'s **only** chance to build itself, once the `EffectBlock` returns, the `Effect` is sealed. After that, it cannot be changed unless it's disposed and mounted again.

Let's have a look at the `EffectBuilder` interface.

```csharp
public interface EffectBuilder {

    SpokeEngine Engine { get; }
    void Log(string msg);

    T D<T>(ISignal<T> signal);

    void Use(SpokeHandle trigger);
    T Use<T>(T disposable) where T : IDisposable;

    void UseSubscribe(ITrigger trigger, Action action);
    void UseSubscribe<T>(ITrigger<T> trigger, Action<T> action);

    ISignal<T> UseMemo<T>(Func<MemoBuilder, T> selector, params ITrigger[] triggers);
    ISignal<T> UseMemo<T>(string name, Func<MemoBuilder, T> selector, params ITrigger[] triggers);

    void UseEffect(EffectBlock func, params ITrigger[] triggers);
    void UseEffect(string name, EffectBlock func, params ITrigger[] triggers);

    void UseReaction(EffectBlock action, params ITrigger[] triggers);
    void UseReaction(string name, EffectBlock action, params ITrigger[] triggers);

    void UsePhase(ISignal<bool> mountWhen, EffectBlock func, params ITrigger[] triggers);
    void UsePhase(string name, ISignal<bool> mountWhen, EffectBlock func, params ITrigger[] triggers);

    IDock UseDock();
    IDock UseDock(string name);

    void OnCleanup(Action cleanup);
}
```

Let's walk through the most important builder methods, starting with ownership.

---

### `Use`

Most builder methods in Spoke are prefixed with `Use`. This isn't just naming convention — it reflects the system’s **ownership model**.

The most fundamental of these is:

`T Use<T>(T disposable) where T : IDisposable;`

When you `Use(...)` something, you're saying:
_“This effect now owns this resource. Dispose it when I unmount.”_

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {

        s.UseEffect("MyEffect", s => { /* ... */ });

        // Equivalent manual version:
        s.Use(new Effect("MyEffect", s.Engine, s => { /* ... */ }));
    }
}
```

---

### `OnCleanup`

An `EffectBuilder` can register any number of cleanup actions. When the `Effect` is disposed it will first run each of these actions.

```csharp
var effect = new Effect("MyEffect", MyEngine, s => {

    s.OnCleanup(() => Debug.Log("Effect cleaned up!"));
});

// ...

effect.Dispose(); // Prints: Effect cleaned up!
```

---

## Dependencies

Remember the final parameter to the `Effect` constructor: `params ITrigger[] triggers`? Let's go back to that.

In Spoke there are several objects (including `Effect`) that extend `SpokeEngine.Computation`. These have dependencies over a set of `ITrigger` which cause them to be disposed and then remounted when any of them trigger.

For an `Effect` this means it will be disposed, including descendants, and then its `EffectBlock` is run again fresh.

There are two ways that dependencies on an `Effect` can be defined:

- **Explicit**: passing them explicitly in the constructor.
- **Dynamic**: auto-binding to `ISignal`'s accessed in the `EffectBlock`.

There are pros and cons to each. You can choose one or the other, or both, depending on your needs.

---

### Explicit Dependencies

```csharp
var myTrigger = Trigger.Create();
var myState = State.Create(0);

var effect = new Effect("MyEffect", myEngine, s => {

    Debug.Log($"myState is {myState.Now}");

}, myTrigger, myState); // any number of dependencies in final args

// Instantiating effect Prints: myState is 0

myTrigger.Invoke(); // Prints: myState is 0
myState.Set(1);     // Prints: myState is 1
```

Any number of `ITrigger` can be given explicitly to the `Effect` constructor. The same is true for `UseEffect()`.

- **Advantages**: Works with `ITrigger`, can give more control and clarity
- **Disadvantages**: More verbose, requires more discipline, easy to forget a dependency

---

### Dynamic Dependencies

Dynamic dependencies are defined by calling a method from the `EffectBuilder`:

`T D<T>(ISignal<T> signal);`

```csharp
var name = State.Create("Spokey");
var age = State.Create(0);

var effect = new Effect("MyEffect", myEngine, s => {

    Debug.Log($"name: {s.D(name)}, age: {s.D(age)}");
});

// Instantiating effect Prints: name: Spokey, age: 0

name.Set("Reacts"); // Prints: name: Reacts, age: 0
age.Set(1);         // Prints: name: Reacts, age 1
```

`D()` is a method that wraps a `ISignal`. It makes that `ISignal` a dynamic dependency and then returns `ISignal.Now`.

If the `Effect` remounts, it will clear its dynamic dependencies, and then discover dependencies again on its next run. Dynamic dependencies can change on each run.

```csharp
var effect = new Effect("MyEffect", myEngine, s => {
    // Totally fine
    if (s.D(condition)) {
        DoSomething(s.D(foo));
    } else {
        SomethingElse(s.D(bar));
    }
});
```

- **Advantages**: Better ergonomics, more flexible, more powerful.
- **Disadvantages**: Only supports `ISignal`, easier to misuse.

I personally prefer dynamic dependencies. They feel better. They're more fun. And they handle complex logic more elegantly, like poking into nested signals.

That said, there are times where explicit dependencies are needed. Like if you need to remount an `Effect` from a pure event. Dynamic dependencies won’t catch pure events — only explicit dependencies will work in this case.

You can combine both styles freely — Spoke will track dynamic dependencies _and_ honor any explicit `ITrigger`s you pass in.

---

## What is with little-_s_

You might have noticed I repeatedly use _s_ as the parameter name in the `EffectBlock`.

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {

        s.UseEffect(s => {

            s.UseEffect(s => {

                s.UsePhase(IsEnabled, s => { });
            });
        });

        var num = s.UseMemo(s => {
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

    protected override void Init(EffectBuilder initBuilder) {

        initBuilder.UseEffect(innerBuilder => {

            initBuilder.UseEffect(/*...*/) // Oops wrong builder!
        });
    }
}
```

Spoke would catch that mistake and throw an error — but it’s better to avoid it altogether. Besides, trying to think of a good parameter name for an `Effect` three levels deep gets hard.

The little-_s_ in comparison is unobtrusive yet immediately recognisable, and lets you focus on what matters.

---

## Advanced Notes

- **Deferred Execution**: Nested Effects are scheduled and deferred, then executed in order.

- **Deterministic Flush**: Effects flush after Memos, in mount order. Execution is intuitive and consistent.

- **Safe Cleanup**: Cleanup functions run in reverse order, then child `IDisposable`s are disposed in reverse.

- **Zero GC Leaves**: Leaf Effects won't allocate GC on remount if the `EffectBlock` is allocation-free.
