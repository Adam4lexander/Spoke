# Trigger

A `Trigger` is an event. It can be subscribed to and can notify subscribers with optional event data. It's conceptually similar to `UnityEvent` or a C# `event`.

In Spoke, `Trigger` is also the **core primitive** that drives reactive computation.

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
        s.UseSubscribe(myTrigger, evt => ReactToEvent(evt));
    }
}
```

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

This is the concrete implementation. It implements both `ITrigger` and `ITrigger<T>`, and adds `Invoke(T param)` to emit events.

```csharp
public class MyBehaviour : SpokeBehaviour {

    private Trigger<string> _myTrigger = Trigger.Create<string>();

    public ITrigger<string> MyTrigger => _myTrigger; // Only exposes subscribe

    void DoInvokeTrigger() {
        _myTrigger.Invoke("Hello");
    }
}
```

> `Trigger.Create()` without a type parameter returns a trigger that emits no data — internally, it's just `Trigger<Unit>`, where `Unit` is an empty struct.

---

## Advanced Notes

- **Zero GC**: Uses object pools and value types internally to avoid garbage allocations.

- **Safe Unsubscribes**: During `Invoke()`, it clones the subscriber list to ensure safe and deterministic behavior even if a subscriber unsubscribes mid-call.

- **Deferred Invocation**: Nested `Invoke()` calls are deferred and flushed in order, one batch at a time.

- **Reactivity-safe**: If a `Trigger` invalidates a reactive computation (e.g. remounting an `Effect` or recomputing a `Memo`), the flush is deferred until all subscribers have been notified — preserving deterministic update order.
