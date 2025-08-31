# Signal

## Table of Contents

- [Overview](#overview)
- [`ISignal<T>`](#isignalt)
  - [Example](#example)
- [`State<T>`](#statet)
  - [Example](#example-1)
  - [`UState<T>`](#ustatet)
- [Advanced Notes](#advanced-notes)

---

## Overview

A signal is a kind of trigger that additionally holds a value. It's a reactive data container that notifies subscribers when its value changes.

Triggers may be the fundamental primitive in Spoke, but signals are the objects you'll work with most often.

---

## `ISignal<T>`

A signal is anything that extends the `ISignal<T>` interface:

```csharp
public interface ISignal<T> : ITrigger<T> {

    T Now { get; } // The signals current value

    // Inherited from ITrigger<T>
    SpokeHandle Subscribe(Action<T> action);
    SpokeHandle Subscribe(Action action);
}
```

In Spoke, a signal is a readonly object. You can access its present value with `signal.Now`, and you can subscribe to the value changing with `signal.Subscribe()`.

---

### Example

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {

        // SpokeBehaviour exposes: IsAwake, IsEnabled and IsStarted
        // They are each an ISignal<bool>

        Debug.Log(IsEnabled.Now);       // Prints: false
        s.Subscribe(IsEnabled, v => {
            Debug.Log($"IsEnabled changed to {v}");
        });

        Debug.Log(IsStarted.Now);       // Prints: false
        s.Phase(IsStarted, s => {
            Debug.Log(IsStarted.Now);   // Prints: true
        });
    }
}
```

---

## `State<T>`

A state is a type of signal that lets you read and write its value. It implements `ISignal<T>` and extends it with the following methods:

```csharp
void Set(T value);              // Sets the value contained by the signal
void Update(Func<T, T> setter); // Sets the value by a function of the existing value
```

When it sets a new value, all subscribers are notified. Crucially, the new value is compared with the old using `EqualityComparer<T>.Default`. When you call `state.Set()` and pass a value equal to its `state.Now`, then it will not trigger.

---

### Example

```csharp
public class MyBehaviour : SpokeBehaviour {

    State<bool> boolState = State.Create(false);
    State<GameObject> goState = State.Create<GameObject>();

    protected override void Init(EffectBuilder s) {

        s.Subscribe(boolState, b => Debug.Log($"boolState is {b}"));
        s.Subscribe(goState, go => {
            if (go != null) {
                Debug.Log($"goState is {go.name}");
            } else {
                Debug.Log($"goState is null");
            }
        });

        boolState.Set(true);        // Prints: boolState is true
        boolState.Set(true);        // No change...
        boolState.Update(b => !b);  // Prints: boolState is false

        gameObject.name = "MyGameObject";
        goState.Set(gameObject);    // Prints: goState is MyGameObject
        goState.Set(null);          // Prints: goState is null
    }
}
```

---

### `UState<T>`

Is just like `State<T>` except it is serializable by Unity and it shows up in the editor.

```csharp
public class MyBehaviour : SpokeBehaviour {

    [SerializeField] UState<string> myName = UState.Create("Spokey");

    public ISignal<string> MyName => myName; // Only exposes Now and Subscribe, so changing name is a private capability

    protected override void Init(EffectBuilder s) {
        s.SubScribe(myName, n => Debug.Log($"myName is {myName}"));
        myName.Set("Bob");      // Prints: myName is Bob
    }
}
```

Reactivity is fully integrated with the Unity editor. `UState` responds to value changes in the Unity Editor, including Undo, deserialization, and runtime modifications. You could use it for pure editor scripts or behaviours with `[ExecuteAlways]`.

---

## Advanced Notes

- **UState Reactivity**: Getting `UState` to integrate cleanly with the Unity Editor — including support for Undo, deserialization, and runtime changes — was _bloody hard_. I believe this is the best set of magic tricks to cover all the edge cases, but be wary: Unity serialization is full of surprises.
