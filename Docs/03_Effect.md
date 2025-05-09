# Effect

Now we get to the good stuff. The `Effect` is where you will write all your code!

The term Effect comes from reactive programming. It refers to logic that reacts to state changes and may cause side effects — like changing a component, spawning something, or updating game state. It's the boundary between reactivity and imperative execution.

In Spoke an `Effect` can be understood as follows:

- It's an object which can be _mounted_ and _disposed_.
- It's a container for child objects which implement IDisposable.
- Those children might also be `Effect`s.
- When an `Effect` is disposed, all its descendants will be disposed too.

There are three kinds of **Effect** in Spoke: `Effect`, `Reaction` and `Phase`. They are all syntactic sugar over the same underlying concept. Spoke **could** exist with only `Effect`. The others exist for convenience.

---

## Creating an Effect

If you use `SpokeBehaviour` then you may never need to create an `Effect` manually, but here's how it's done:

```csharp
// We'll get to SpokeEngine later
var engine = new SpokeEngine(FlushMode.Immediate);

var effect = new Effect("MyEffect", engine, s => {
    // EffectBuilder logic
});
// ...
effect.Dispose();
```

Here is the constructor for `Effect`:

`public Effect(string name, SpokeEngine engine, EffectBlock block, params ITrigger[] triggers)`

First the `name` is how the `Effect` appears in debugging views. We'll get to the `engine` later. The remaining two parameters I'll dive into, but in summary:

- `block`: is a function that takes a builder object and 'creates' the `Effect`.
- `triggers`: are explicit dependencies that remount the `Effect` when they trigger.

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

        var effect = new Effect("innerEffect", s.SpokeEngine, s => {
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
        // Is equivalent to
        s.Use(new Effect("MyEffect", s.SpokeEngine, s => { /* ... */ }));
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
