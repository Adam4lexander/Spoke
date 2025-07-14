# Dock

A `Dock` is a dynamic container for child objects that implement `IDisposable`.

It's similar to an `Effect`: you can call `Use(...)` on it to take ownership of a resource.
But there's a **key difference:**

- `Effect` can only `Use(...)` things _during mount_. Once the `EffectBlock` finishes, it becomes sealed.

- `Dock` can `Use(...)` things _anytime_. It's not sealed after construction, so you can mount or replace children at runtime.

```csharp
public class MyBehaviour : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {

        s.Subscribe(someTrigger, () => {
            s.Effect(s => { /* ... */ }); // Error! Builder is sealed
        });

        var dock = s.Dock();

        s.Subscribe(someTrigger, () => {
            dock.Effect("key", s => { /* ... */ }); // This works!
        });
    }
}
```

The `"key"` lets you replace or remove child resources later.
Calling `dock.Drop("key")` will dispose and unmount whatever was mounted under that key.

---

## When to Use a Dock

Use a `Dock` when you need to mount or unmount reactive logic **in response to events or async tasks**, outside the normal `EffectBlock` lifecycle.

For example:

```csharp
public class OverheadUISystem : SpokeBehaviour {

    // These triggers fire when actors enter or leave range of the player
    Trigger<Actor> onActorInRange = Trigger.Create<Actor>();
    Trigger<Actor> onActorOutRange = Trigger.Create<Actor>();

    protected override void Init(EffectBuilder s) {

        // Create a Dock that will hold one UI Effect per actor
        var dock = s.Dock();

        // Mount a UI when an actor comes into range
        s.Subscribe(onActorInRange, actor => {
            dock.Effect(actor, OverheadUI(actor));
        });

        // Unmount the UI when they go out of range
        s.Subscribe(onActorOutRange, actor => {
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

When an AI actor comes in range of the player, a UI panel is shown above that actor's head. When the actor moves out of range, the UI panel is disposed.

Notice also that the `actor` instance is used as the key in `dock.UseEffect`.
This makes it easy to later drop that same `Effect` when `onActorOutRange` triggers.

Keys are of type `object`, so they can be anything: actor instances, strings, numbers — whatever uniquely identifies the resource.
