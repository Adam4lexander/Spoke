# SpokeTree

## Table of Contents

- [Overview](#overview)
- [Usage with SpokeBehaviour](#usage-with-spokebehaviour)
- [Standalone Usage](#standalone-usage)
- [Flushing Model](#flushing-model)
  - [Two Universes](#two-universes)
- [Batching](#batching)

---

## Overview

`SpokeTree` is the root node for a tree of effects.

Its role is an execution scheduler. When an effect or memo needs to be re-run, `SpokeTree` is what drives those executions.

`SpokeTree` is explained in detail in the [Spoke Runtime docs](./00_SpokeRuntime.md#spoketree). This page will give a high-level overview of how it relates to `Spoke.Reactive`. Just enough to reveal the "why" so the model feels intuitive.

---

## Usage with SpokeBehaviour

Every `SpokeBehaviour` creates its own distinct `SpokeTree`. It's created in `Awake`, and it attaches a root effect that hosts `Init`. If you didn't want to use `SpokeBehaviour`, you could replicate this pattern in any `MonoBehaviour`:

```cs
using UnityEngine;
using Spoke;

public abstract class CustomSpokeBehaviour : MonoBehaviour {
    // Create a signal to reflect if I'm enabled/disabled
    State<bool> isEnabled = new(false);

    // Expose it as an ISignal (read-only interface)
    public ISignal<bool> IsEnabled => isEnabled;

    // We'll store the tree so we can dispose it in OnDestroy
    SpokeTree tree;

    // // Subclasses provide the root EffectBlock
    protected abstract void Init(EffectBuilder s);

    void Awake() {
        // Spawn the SpokeTree, create its root Effect, and give Init as the EffectBlock
        // The tree is now live; it will flush scheduled descendants automatically
        tree = SpokeTree.Spawn(new Effect("Init", Init));
    }

    void OnDestroy() {
        // Dispose the tree, it will unwind and detach all descendants
        tree.Dispose();
    }

    void OnEnable() => isEnabled.Set(true);
    void OnDisable() => isEnabled.Set(false);
}
```

> This replicates almost everything `SpokeBehaviour` does. The built-in behaviour has a little extra machinery, but not much.

Because every `SpokeBehaviour` has its own `SpokeTree`, there can be many trees alive at once. The Spoke runtime schedules and orchestrates them if multiple trees request flushing at the same time.

---

## Standalone Usage

You can spawn `SpokeTree`s anywhere:

```cs
var tree = SpokeTree.Spawn(new Effect("Init", s => {
    s.Effect(s => {
        Debug.Log("Effect-1 ran");
    });
    s.Effect(s => {
        Debug.Log("Effect-2 ran");
    });
}));

// Don't forget to dispose it somewhere
tree.Dispose();
```

`Spoke.Reactive` and `Spoke.Runtime` have no dependency on Unity. The `Spoke.Unity` package is a thin, optional integration layer. `SpokeBehaviour` is provided for convenience, but you can roll your own integration.

---

## Flushing Model

`SpokeTree` is the execution scheduler for its descendants. Effects and memos schedule themselves to run on their `SpokeTree`. When a tree receives pending work, it schedules itself on the global Spoke runtime.

This triggers a flush, starting at the runtime layer (which drains every pending tree), then within each scheduled `SpokeTree` (which drains every pending descendant). When there is no pending work left, control returns to user code.

```cs
// Create a signal to trigger work in the tree
State<bool> shouldRunTree = new(false);

// Spawn a SpokeTree. Host a Phase at the top that runs only when shouldRunTree=true
var tree = SpokeTree.Spawn(new Effect("Init", s => {
    s.Phase(shouldRunTree, s => {
        Debug.Log("Init ran");
        s.Effect(s => {
            Debug.Log("Inner Effect ran");
        });
    });
}));

Debug.Log("Before set shouldRunTree=true");
shouldRunTree.Set(true);
Debug.Log("After set shouldRunTree=true");

tree.Dispose();
```

Logs:

```
Before set shouldRunTree=true
Init ran
Inner Effect ran
After set shouldRunTree=true
```

On `shouldRunTree.Set(true)`, Spoke takes control of the thread and flushes all work before returning control. There's nothing fancy happening, the `Phase` subscribed a callback on the `shouldRunTree` signal.

---

### Two Universes

It's helpful to think of your code running in one of two universes:

- **User code**: code that runs while Spoke is idle
- **Spoke code**: Spoke is flushing and running `EffectBlock`s / `MemoBlock`s

General flow:

1. User code updates a signal or invokes a trigger
2. Spoke takes control and flushes all pending effects and memos
3. Control returns to user code, continuing where it left off

User code sees only the final result of the flush. When it regains control, Spoke has already updated everything.

Spoke code is actively performing the updates and is subject to Spoke's execution rules.

Consider setting `shouldRunTree` **inside** Spoke code:

```cs
SpokeTree.Spawn(new Effect("Init", s => {
    // Create the signal inside an EffectBlock this time
    State<bool> shouldRunTree = new(false);

    // Run the Phase when shouldRunTree == true
    s.Phase(shouldRunTree, s => {
        Debug.Log("Outer Phase ran");
        s.Effect(s => {
            Debug.Log("Inner Effect ran");
        });
    });

    // Set the signal from within the EffectBlock
    Debug.Log("Before set shouldRunTree=true");
    shouldRunTree.Set(true);
    Debug.Log("After set shouldRunTree=true");
}));
```

Logs:

```
Before set shouldRunTree=true
After set shouldRunTree=true
Outer Phase ran
Inner Effect ran
```

This is the same scenario described on the [Effect page: deferred execution](./04_Effect.md#deferred-execution). `SpokeTree` runs its descendants in **imperative order**, one unit after another. It won't pre-empt an effect mid-way to run another. Calling `shouldRunTree.Set(true)` schedules the Phase, but it runs only after _Init_ has completed.

You can make it run earlier by updating `shouldRunTree` in its own effect:

```cs
SpokeTree.Spawn(new Effect("Init", s => {
    State<bool> shouldRunTree = new(false);

    s.Phase(shouldRunTree, s => {
        Debug.Log("Outer Phase ran");
        s.Effect(s => {
            Debug.Log("Inner Effect ran");
        });
    });

    // Wrap in Effects, so the logic is scheduled instead of immediate
    s.Effect(s => {
        Debug.Log("Before set shouldRunTree=true");
        shouldRunTree.Set(true);
    });

    // The Phase runs before this Effect, because it appears earlier by imperative order
    s.Effect(s => {
        Debug.Log("After set shouldRunTree=true");
    });
}));
```

Think of each `Effect`, `Phase`, `Reaction`, and `Memo` as a **unit of work**. The `SpokeTree` orchestrates these units and ensures an imperative ordering. Once a unit starts, it completes without interruption.

> For details on nested flushes, priorities, and cross-tree orchestration, see the runtime docs.

---

## Batching

Sometimes you'll want to update multiple signals before letting Spoke flush. Use `SpokeRuntime.Batch(...)`:

```cs
State<string> firstName = new("");
State<string> lastName = new("");

SpokeTree.Spawn(new Effect("Init", s => {
    var isNonEmpty = s.Memo(s => s.D(firstName) != "" || s.D(lastName) != "");
    s.Phase(isNonEmpty, s => {
        Debug.Log($"Fullname is {s.D(firstName)} {s.D(lastName)}");
    });
}));

SpokeRuntime.Batch(() => {
    firstName.Set("Max");
    lastName.Set("Payne");
});
// Logs: Fullname is Max Payne

// Without batching:
firstName.Set("Humpty"); // Logs: Fullname is Humpty Payne
lastName.Set("Dumpty");  // Logs: Fullname is Humpty Dumpty
```

`SpokeRuntime.Batch` defers the start of a flush until the delefgate completes.
