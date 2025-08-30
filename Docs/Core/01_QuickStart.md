# QuickStart Guide

## Table of Contents

- [Setup](#setup)
- [SpokeBehaviour](#spokebehaviour)
- [Common Patterns](#common-patterns)
  - [Event Subscription](#event-subscriptions)
  - [`IDisposable` Management](#idisposable-management)
  - [Synchronising State](#synchronising-state)
  - [Managing Coroutines](#managing-coroutines)
- [Complex Example](#complex-example)

---

## Setup

For Unity, you'll need **`Spoke.Runtime/`**, **`Spoke.Reactive/`** and **`Spoke.Unity/`** in your project. Either copy the folders somewhere in your project's **`Assets/`** directory, or clone Spoke there directly.

---

## SpokeBehaviour

The easiest way to use Spoke is to subclass `SpokeBehaviour` instead of `MonoBehaviour`:

```csharp
using Spoke;

public class MyBehaviour : SpokeBehaviour {

    // Override Init. Replaces: Awake, OnEnable, Start, OnDisable and OnDestroy
    protected override void Init(EffectBuilder s) {

        // Awake logic here ...

        s.OnCleanup(() => {
            // OnDestroy logic here ...
        });

        s.Phase(IsEnabled, s => {
            // OnEnable logic here
            s.OnCleanup(() => {
                // OnDisable logic here
            });
        });

        s.Phase(IsStarted, s => {
            // Start logic here
        });
    }
}
```

> `IsEnabled` and `IsStarted` are reactive signals. They hold a `bool` and notify subscribers when their value changes. `Phase` runs when its signal becomes `true` and cleans up when the signal becomes `false`.

Phases are composable. You can reorder and nest them however you like:

```cs
public class MyBehaviour : SpokeBehaviour {

    [SerializeField] UState<bool> SomeBool = new(true); // Serializable signal shown in the Inspector

    protected override void Init(EffectBuilder s) {
        // When I'm awake
        s.Phase(IsStarted, s => {
            // And I'm started
            s.Phase(IsEnabled, s => {
                // And I'm enabled
                s.Phase(SomeBool, s => {
                    // And SomeBool is True
                    DoTheThing();
                    s.OnCleanup(() => UndoTheThing());
                });
            });
        });
    }
}
```

The mental model is a tree. When a `Phase` cleans up because its signal becomes `false`, it unwinds and cleans up its subtree.

---

## Common Patterns

### Event Subscriptions

```cs
public class MyBehaviour : SpokeBehaviour {

    public UnityEvent SomeEvent;
    public UnityEvent SomeOtherEvent;

    protected override void Init(EffectBuilder s) {
        // When I'm awake, subscribe to SomeEvent
        s.Subscribe(SomeEvent, SomeEventHandler);
        s.Phase(IsStarted, s => {
            // When I'm awake and started, subscribe to SomeOtherEvent
            s.Subscribe(SomeOtherEvent, SomeOtherEventHandler);
        });
    }

    void SomeEventHandler() { /* ... */ }
    void SomeOtherEventHandler() { /* ... */ }
}
```

> Subscriptions are automatically removed when the block is cleaned up.

---

### `IDisposable` Management

```cs
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        var myResource = s.Use(new SomeResource); // Own an IDisposable, will auto-dispose on cleanup
    }
}
```

---

### Synchronising State

```cs
public class MyCalculator : SpokeBehaviour {

    public enum OpKind { Add, Subtract, Multiply, Divide }

    [SerializeField] Text outputText; // Text to display calculator results on

    [SerializeField] UState<float> number1;
    [SerializeField] UState<OpKind> operation;
    [SerializeField] UState<float> number2;

    protected override void Init(EffectBuilder s) {

        // Memo is a reactive signal, like UState, that computes its value depending on other signals
        var result = s.Memo(s => {
            switch (s.D(operation)) {
                case OpKind.Add: return s.D(number1) + s.D(number2);
                case OpKind.Subtract: return s.D(number1) - s.D(number2);
                case OpKind.Multiply: return s.D(number1) * s.D(number2);
                case OpKind.Divide: return s.D(number1) / s.D(number2);
            }
        });

        // Effect is the same as Phase, only without the first ISignal<bool> parameter
        s.Effect(s => {
            outputText.text = $"Result is: {s.D(result)}";
        });
    }
}
```

> `s.D(...)` means: read this signal, **and** make it a dependency. If the signal changes, the block reruns automatically. The concise syntax was chosen because `s.D()` is used alot. Think of it like a hieroglyph instead of a method name.

---

### Managing Coroutines

```cs
public class DanceBehaviour : SpokeBehaviour {

    public enum DanceKind { None, Swing, Waltz }

    [SerializeField] UState<DanceKind> WhichDance;

    protected override void Init(EffectBuilder s) {
        s.Phase(IsEnabled, s => {
            // When I'm enabled

            // Depend on 'WhichDance' and store in a variable
            var whichDanceNow = s.D(WhichDance);

            if (whichDanceNow == DanceKind.Swing) {
                // Start SwingDance, and stop on cleanup
                var routineInstance = StartCoroutine(SwingDanceRoutine());
                s.OnCleanup(() => StopCoroutine(routineInstance));
            }
            else if (whichDanceNow == DanceKind.Waltz) {
                // Start Waltz, and stop on cleanup
                var routineInstance = StartCoroutine(WaltzRoutine());
                s.OnCleanup(() => StopCoroutine(routineInstance));
            }
        });
    }

    IEnumerator SwingDanceRoutine() {
        // Update swing dance logic
        yield return null;
    }

    IEnumerator WaltzRoutine() {
        // Update waltz logic
        yield return null;
    }
}
```

---

## Complex Example

Those were some simple patterns you can start using immediately. But Spoke really shines as complexity increases and game logic depends on a mixture of events and runtime state.

For example, imagine you're making a 3rd person action-fantasy game. You want to add a new player ability called _Spell Breaker_, which works like this:

- The player presses a button to activate it
- If a hostile wizard nearby is casting a spell, the cast is interrupted
- Show a "Activate SpellBreaker!" UI prompt when any valid wizard is casting
- Respond to dynamic faction changes, in case a friendly wizard becomes hostile
- Respond to wizards entering range who were already mid-cast
- If a wizard becomes _Frozen_ mid-cast, their cast pauses and becomes immune to interruption until thawed

Here's the whole thing using Spoke:

```cs
public class SpellBreakerController : SpokeBehaviour {

    [Header("References")]
    [SerializeField] ActorSensor actorSensor;
    [SerializeField] GameObject spellBreakerUI;

    // Spoke.Trigger is an event
    Trigger interruptSpellCommand = new();
    // Spoke.State is like UState, but not serializable
    State<int> numberOfSpellCasts = new(0);

    protected override void Init(EffectBuilder s) {
        var dock = s.Dock(); // Docks are dynamic containers for Effects
        s.Subscribe(actorSensor.OnActorInRange, actor => {
            if (actor.IsWizard) dock.Effect(actor, WizardTracker(actor));
        });
        s.Subscribe(actorSensor.OnActorOutOfRange, actor => {
            dock.Drop(actor);
        });
        s.Effect(s => {
            spellBreakerUI.SetActive(s.d(numberOfSpellCasts) > 0);
        });
    }

    // WizardTracker returns a parameterized, re-usable EffectBlock
    // The double-lambda captures 'wizard' in a closure
    EffectBlock WizardTracker(Actor wizard) => s => {
        if (s.D(wizard.IsFriendly)) return;
        if (s.D(wizard.IsFrozen)) return;
        s.Phase(wizard.IsCastingSpell, s => {
            s.Subscribe(interruptSpellCommand, () => wizard.InterruptSpell());
            numberOfSpellCasts.Update(x => x + 1);
            s.OnCleanup(() => numberOfSpellCasts.Update(x => x - 1));
        });
    };

    void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) interruptSpellCommand.Invoke();
    }
}
```

I've introduced a lot of new concepts here and not expecting it to be quickly understandable. The goal is to show a complex use case that Spoke is well‑suited for.

Once you're familiar with Spoke, you can write code like this very quickly. It may be short, but it's handling a ton of edge cases automatically, like wizards entering/leaving range while mid-cast, or wizards dynamically changing factions.

The patterns above give immediate value and serve as an onboarding ramp for diving deeper.
