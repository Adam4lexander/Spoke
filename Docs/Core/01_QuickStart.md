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
        var myResource = s.Use(new SomeResource()); // Own an IDisposable, will auto-dispose on cleanup
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

> `s.D(...)` means: read this signal, **and** make it a dependency. If the signal changes, the block reruns automatically. The concise syntax was chosen because `s.D()` is used a lot. Think of it like a hieroglyph instead of a method name.

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

For example, imagine you're making an action-fantasy RTS. You want to add a new unit ability called _Healing Aura_, which works like this:

- While the carrier unit is alive, friendly units inside its aura radius gain +50% speed, a glowing VFX, and heal 5 HP per second
- Magic-immune units are unaffected
- Respond to units entering the radius in any state, or changing state while inside it
- Respond to dynamic faction changes on either side — including the carrier being mind-controlled
- Auras do **not** stack: a unit covered by several carriers is buffed by exactly one of them
- When a carrier dies, every buff it granted unwinds instantly — and any unit still covered by another carrier is re-buffed by that carrier automatically

Here's the whole thing using Spoke:

```cs
public class HealingAura : SpokeBehaviour {

    [Header("References")]
    [SerializeField] Unit carrier;
    [SerializeField] UnitSensor auraRadius;
    [SerializeField] GameObject glowVfxPrefab;

    protected override void Init(EffectBuilder s) {
        s.Phase(carrier.IsAlive, s => {
            var dock = s.Dock(); // Docks are dynamic containers for Effects
            s.Subscribe(auraRadius.OnUnitEnter, unit => dock.Effect(unit, Aura(unit)));
            s.Subscribe(auraRadius.OnUnitExit, unit => dock.Drop(unit));
        });
    }

    // Aura returns a parameterized, re-usable EffectBlock
    // The double-lambda captures 'unit' in a closure
    EffectBlock Aura(Unit unit) => s => {
        if (!s.D(unit.IsAlive)) return;
        if (s.D(unit.IsMagicImmune)) return;
        if (s.D(unit.Faction) != s.D(carrier.Faction)) return;

        // Ask the unit for the exclusive right to buff it. The lease is an
        // IDisposable owned by this block, and IsHeld flips true when granted
        var lease = s.Use(unit.RequestBuff("healing-aura"));

        s.Phase(lease.IsHeld, s => {
            var speedBoost = unit.Stats.AddModifier(Stat.Speed, x => x * 1.5f);
            s.OnCleanup(() => unit.Stats.RemoveModifier(speedBoost));

            var glow = Instantiate(glowVfxPrefab, unit.transform);
            s.OnCleanup(() => Destroy(glow));

            var heal = StartCoroutine(HealTick(unit));
            s.OnCleanup(() => StopCoroutine(heal));
        });
    };

    IEnumerator HealTick(Unit unit) {
        while (true) {
            yield return new WaitForSeconds(1f);
            unit.Health.Update(x => x + 5);
        }
    }
}
```

The example introduces a lot of new concepts, and not all of it may make sense yet. The goal is to show a complex use case that Spoke is well‑suited for.

Once you're familiar with Spoke, you can write code like this very quickly. It may be short, but it's handling a ton of edge cases automatically: units entering the radius already wounded, dying inside it, gaining or losing magic immunity mid-buff, either side changing faction, or the carrier dying and unwinding every buff at once. And when a dying carrier's buffs unwind, any unit still covered by another carrier hands over automatically — the other carrier's lease is granted, its `Phase` mounts, and the buff comes back without a line of coordination code. No matter how a buff ends, the same cleanup path returns the stat modifier, destroys the glow, and stops the heal coroutine. Nothing can leak, and nothing double-applies.

> `RequestBuff` isn't part of Spoke — it's this game's own `Unit` API. It returns an `IDisposable` lease exposing an `ISignal<bool> IsHeld`. The unit grants the lease to one requester at a time, and passes it to the next requester when the holder disposes. Signals and disposables are Spoke's native currency: when your game systems expose them, contested resources compose as easily as owned ones.

Also notice there's no `Update()` method. Nothing is polled or diff-checked per frame. The whole behaviour is driven by events and state changes.

The patterns above give immediate value and serve as an onboarding ramp for diving deeper.


