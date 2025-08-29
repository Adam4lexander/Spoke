# Trigger

A trigger is an event broadcaster. It can be subscribed to and can notify subscribers with optional event data. It's conceptually similar to `UnityEvent` or a C# `event`.

In Spoke, a trigger is also the **core primitive** that drives reactive computation. When a trigger is invoked, all its dependants are notified and re-executed.

You don't **need** to use triggers to replace all `UnityEvent` or C# events. Spoke can manage subscriptions to any of these. But triggers are tightly integrated with `Spoke.Reactive` and have some important capabilities.

---

## Types

### `ITrigger` / `ITrigger<T>`

```csharp
public interface ITrigger {
    SpokeHandle Subscribe(Action action);
    void Unsubscribe(Action action);
}

public interface ITrigger<T> : ITrigger {
    SpokeHandle Subscribe(Action<T> action);
    void Unsubscribe(Action<T> action);
}
```

- `ITrigger` allows subscription without passing event data.

- `ITrigger<T>` includes the event data payload.

- Both support two styles of use: **disposable handles** and **manual unsubscribe**.

---

## Subscribing

### ✅ Preferred: Subscribe + Dispose

This is the idiomatic Spoke style. `Subscribe()` returns a lightweight `SpokeHandle` — a `struct` that implements `IDisposable`. Disposing it unsubscribes.

> This pattern avoids GC allocations and integrates cleanly with Spoke’s reactive scopes.

```csharp
var handle = myTrigger.Subscribe(evt => ReactToEvent(evt));
// ...
handle.Dispose();
```

Used within Spoke scopes:

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        // Subscribed when scope is mounted, auto-disposed when unmounted
        s.Subscribe(myTrigger, evt => ReactToEvent(evt));
    }
}
```

---

### ⚠️ Alternative: Subscribe / Unsubscribe

This resembles Unity-style events and works fine, but doesn’t leverage Spoke's lifecycle integration.

```csharp
public class MyBehaviour : MonoBehaviour {

    void OnEnable() {
        myTrigger.Subscribe(ReactToEvent);
    }

    void OnDisable() {
        myTrigger.Unsubscribe(ReactToEvent);
    }
}
```

This can be useful when migrating from `UnityEvent` to `Trigger`, but it's not recommended for new code.

---

## Creating a Trigger

### `Trigger<T>`

This is the concrete implementation for a trigger. It exposes methods for `Subscribe` and also `Invoke`. Call `Invoke` on the trigger to broadcast an event to all its subscribers.

```csharp
public class MyBehaviour : SpokeBehaviour {

    // Keep the Trigger instance private, so only I can Invoke it
    Trigger<string> _myTrigger = Trigger.Create<string>();

    // ITrigger exposes 'Subscribe' but not 'Invoke'. External code can subscribe to my trigger, but not call it
    public ITrigger<string> MyTrigger => _myTrigger;

    void DoInvokeTrigger() {
        _myTrigger.Invoke("Hello");
    }
}
```

---

### `Trigger`

Sometimes you need a trigger, but you don't need an event payload, just a pure notification. You can create one like this:

```csharp
public class MyBehaviour : SpokeBehaviour {

    Trigger _myTrigger = Trigger.Create();

    public ITrigger MyTrigger => _myTrigger;

    void DoInvokeTrigger() {
        _myTrigger.Invoke();
    }
}
```

> Internally, `Trigger.Create()` will instantiate a `Trigger<Unit>`, where `Unit` is struct defined with no fields. Spoke doesn't actually have a class `Trigger`.

---

## Combining with delegates or `UnityEvent`

Spoke needs its own event emitting primitive to drive reactive updates. `ITrigger` is the common interface for binding to reactive dependencies.

If you're already using delegates or UnityEvents, and you need them to behave like an `ITrigger`, this is how to do it:

```cs
public class MyBehaviour : SpokeBehaviour {

    public UnityEvent SomeUnityEvent;
    public event Action SomeDelegateEvent;

    protected override void Init(EffectBuilder s) {
        // Create a trigger that will facade SomeUnityEvent and also SomeDelegateEvent
        var trigger = Trigger.Create();

        // When the UnityEvent is invoked, it will invoke the trigger
        s.Subscribe(SomeUnityEvent, trigger.Invoke);

        // When the SomeDelegateEvent is invoked, it will also invoke the trigger
        SomeDelegateEvent += trigger.Invoke;
        // Manual cleanup when the behaviour unmounts, s.Subscribe() does this for us automatically
        s.OnCleanup(() => SomeDelegateEvent -= trigger.Invoke);

        // Now we can bind reactive objects to the trigger
        s.Reaction(s => {
            Debug.Log("Either 'SomeUnityEvent' or 'SomeDelegateEvent' fired");
        }, trigger);
    }
}
```

---

## Advanced Notes

- **Zero GC**: Uses object pools and value types internally to avoid garbage allocations.

- **Mutation Safety**: During `Invoke()`, it clones the subscriber list to ensure safe and deterministic behavior even if the subscriber list changes mid-call.

- **Re-entrancy Safety**: Re-entrant `Invoke()` calls are queued and flushed in order, one event at a time.

- **Batched Notifications**: If a `Trigger` invalidates a reactive computation (e.g. to rerun an `Effect` or `Memo`), execution is deferred until all subscribers have been notified. Equivalent to calling `SpokeRuntime.Batch()`.
