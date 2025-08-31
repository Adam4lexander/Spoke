# Dock

## Table of Contents

- [Overview](#overview)
- [Purpose](#purpose)
- [Usage](#usage)
- [Dropping Attachments](#dropping-attachments)
- [Ownership and Execution Order](#ownership-and-execution-order)
- [Example](#example)

---

## Overview

A `Dock` is a dynamic container for keyed subtrees that lets you attach and detach effects at any time, outside the normal mutation windows. It's a powerful primitive in Spoke that completes its feature set.

The `Dock` is exposed by `Spoke.Runtime`, and it's documented already [here](./00_SpokeRuntime.md#dock). It's incredibly useful in `Spoke.Reactive` though, so this page documents its usage in this context.

---

## Purpose

In the docs for `Effect` [I explained the concept of mutation windows](./04_Effect.md#mutation-windows). The `(EffectBuilder s)` can only attach children during the window that the `EffectBlock` is on the stack. That means you can't attach sub-effects asynchronously:

```cs
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        // This line is fine, its executed inside the EffectBlock's scope
        s.Effect(s => { });

        // This will throw an exception. Init is already popped off the stack when SomeEvent fires
        s.Subscribe(SomeEvent, () => s.Effect(s => { }));
    }
}
```

This is precisely the use-case that `Dock` is designed for. How to mutate the tree due to asynchronous events.

---

## Usage

```cs
public class MyBehaviour : SpokeBehaviour {

    // Define an event to trigger code using the dock
    UnityEvent dockCommand = new();

    protected override void Init(EffectBuilder s) {
        // Attach a dock. It's a node in the tree, like Effect or Memo
        var dock = s.Dock();

        // Subscribe dockCommand, attach an effect when it fires
        s.Subscribe(dockCommand, () => {
            dock.Effect("key", s => {
                Debug.Log("Docked effect ran");
                s.OnCleanup(() => Debug.Log("Docked effect cleaned up"));
            });
        });
    }

    void Update() {
        // Press space to invoke dockCommand
        if (Input.GetKeyDown(KeyCode.Space)) {
            dockCommand.Invoke();
        }
    }
}
```

Run this code and press space. You'll notice each time spacebar is pressed it will clean up the effect attached the last time.

It's because we're re-using the same key: `"key"`. A `Dock` acts like a dictionary. There can be only one thing attached for any key. If you reuse the key of a live attachment, the dock first detaches the existing one before attaching the next. Keys are compared by equality.

---

## Dropping Attachments

You can detach effects from the dock at any time, by calling `dock.Drop()` and pass in its key:

```cs
public class MyBehaviour : SpokeBehaviour {

    // Define a variable to hold the dock we'll create in Init
    Dock dock;

    int counter = 0;

    protected override void Init(EffectBuilder s) {
        // Attach a dock, and store it in the class instance variable
        dock = s.Dock();
    }

    void Update() {
        // On up arrow, attach a dock using counter for key, and increment counter
        if (Input.GetKeyDown(KeyCode.UpArrow)) {
            var myKey = counter;
            dock.Effect(myKey, s => {
                Debug.Log($"Effect {myKey} docked");
                s.OnCleanup(() => Debug.Log($"Effect {myKey} cleaned up"));
            });
            counter += 1;
        }

        // On down arrow, decrement counter and Drop the effect docked at that key
        if (counter > 0 && Input.GetKeyDown(KeyCode.DownArrow)) {
            counter--;
            dock.Drop(counter);
        }
    }
}
```

Dock keys are type `object` and can be anything.

`dock.Drop(key)` on a missing key is a no-op.

---

## Ownership and Execution Order

All effects attached to the dock become children of the dock. This has a couple of consequences:

1. They're cleaned up when the dock is cleaned up
2. Their execution order (and the order of their descendants) depends on the position of the Dock in the tree

---

## Example

Here's an example using Dock that resembles a real gameplay problem. It's a system that manages overhead UI panels for AI actors in range of the player.

When an AI actor comes in range of the player, a UI panel is shown above that actor's head. When the actor moves out of range, the UI panel is disposed.

```csharp
public class OverheadUISystem : SpokeBehaviour {

    // These triggers fire when actors enter or leave range of the player
    // Assumes external code is invoking these triggers
    public Trigger<Actor> onActorInRange = Trigger.Create<Actor>();
    public Trigger<Actor> onActorOutOfRange = Trigger.Create<Actor>();

    protected override void Init(EffectBuilder s) {

        // Create a Dock that will hold one UI Effect per actor
        var dock = s.Dock();

        // Mount a UI when an actor comes into range
        s.Subscribe(onActorInRange, actor => {
            dock.Effect(actor, OverheadUI(actor));
        });

        // Unmount the UI when they go out of range
        s.Subscribe(onActorOutOfRange, actor => {
            dock.Drop(actor);
        });
    }

    // Creates a UI Effect for an actor, and cleans it up when unmounted
    EffectBlock OverheadUI(Actor actor) => s => {
        CreateOverheadUI(actor);
        s.OnCleanup(() => RemoveOverheadUI(actor));
    };
}
```

Notice that the `actor` instance is used as the key in `dock.Effect`.
This makes it easy to later drop that same `Effect` when `onActorOutOfRange` triggers.
