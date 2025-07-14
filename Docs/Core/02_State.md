# State

A `State` is a reactive data container. It holds a value, and it implements an `ITrigger` that notifies subscribers when the value changes.

## Types

### `ISignal<T>`

```csharp
public interface ISignal<T> : ITrigger<T> {
    T Now { get; }
}
```

Before we discuss `State`, we need to understand `ISignal<T>` — a concept representing a value with change notifications.

Many reactive frameworks use **Signal** as the fundamental unit of reactivity — a value that notifies when it changes.

In Spoke, however, the fundamental primitive is the **Trigger**, not the **Signal**. This better aligns with game development needs, where one-shot events often drive behavior more naturally than continuous value tracking.

An `ISignal<T>` extends `ITrigger<T>` and adds a current value, accessed via `Now`.

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        Debug.Log($"Signal started as {mySignal.Now}");
        s.Subscribe(mySignal, v => Debug.Log($"Signal changed to {v}"));
    }
}
```

---

### `IState<T>`

```csharp
public interface IState<T> : ISignal<T> {
    void Set(T value);
    void Update(Func<T, T> setter);
}
```

An `IState` extends `ISignal` with methods to mutate the value it contains, making it like a reactive variable.

```csharp
myState.Set(5);
Debug.Log(myState.Now); // Prints: 5
myState.Update(x => x * 5);
Debug.Log(myState.Now); // Prints: 25
```

---

### `State<T>`

`State<T>` is a concrete implementation of `IState`. It triggers when a **new** value is set.

> Value comparison is given by `EqualityComparer<T>.Default`.

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
