# Phase

A `Phase` is a conditional `Effect`.

It mounts when a condition is true, and disposes when it's false. That condition is an `ISignal<bool>`, which means the `Phase` can respond reactively to changes in game state, lifecycles, or custom logic.

Phases are ideal for modeling **stateful sections of behaviour** — things like:

- “Do this while the player is alive”
- “Start scanning when the sensor is enabled”
- “Only run this logic while I'm enabled”

Under the hood, a `Phase` is just syntactic sugar for an `Effect` that disposes itself when the condition flips to false.

---

## Usage

You create a phase inside an `EffectBlock` by calling `UsePhase` on the `EffectBuilder`.

```csharp
s.UsePhase(IsEnabled, s => {
    // Runs only while IsEnabled.Now == true
    s.UseSubscribe(SomeTrigger, DoThing);
});
```

This is equivalent to:

```csharp
s.Use(new Phase("Phase", s.SpokeEngine, IsEnabled, s => {

    s.UseSubscribe(SomeTrigger, DoThing);
}));
```

The inner block is another `EffectBlock`, and just like a regular `Effect`, you can nest anything inside: subscriptions, memos, other phases, reactions and cleanup handlers.

---

## Dynamic Conditions

A `Phase` can mount/unmount based on any reactive boolean:

```csharp
var isAlive = s.UseMemo(s => s.D(Health) > 0);

s.UsePhase(isAlive, s => {
    // Runs only while Health > 0
});
```

You can also create more complex logic inline:

```csharp
s.UsePhase(s.UseMemo(s => s.D(IsVisible) && s.D(CanScan)), s => {
    // Runs only while visible AND able to scan
});
```
