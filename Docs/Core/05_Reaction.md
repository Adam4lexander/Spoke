# Reaction

A `Reaction` is an `Effect` that only runs **when triggered**.

It's ideal for **one-shot logic** that reacts to external triggers — like receiving an event, crossing a threshold, or responding to damage.

Unlike a regular `Effect`, a `Reaction` does **not** schedule itself automatically. It only runs when **one of its explicit `ITrigger`s fires**.

This makes it perfect for logic that:

- Doesn't need to mount at startup.
- Should only run **on demand**.
- Doesn't need dynamic dependency tracking.

---

## Usage

You create a reaction inside an `EffectBlock` using `UseReaction`.

```csharp
s.Reaction(s => {

    Debug.Log("I took damage!");

}, ReceiveDamage);
```

You can also create one manually:

```csharp
var reaction = new Reaction("TookDamage", s.SpokeEngine, s => {

    Debug.Log("I took damage!");

}, ReceiveDamage);
```

---

## Key Differences from `Effect`

- `Reaction` **does not schedule itself** — it only runs in response to triggers.
- It does **not support dynamic dependencies** — only explicit `ITrigger`s.
- It is otherwise identical to `Effect`: it mounts, runs an `EffectBlock`, and cleans up children afterward.

Think of a `Reaction` as a **stateless fire-once effect**, ideal for handling push-based updates.

---

## Common Use Cases

- Responding to sensor pings:

```csharp
s.Reaction(s => {

    FlashLight();

}, ReceivePing);
```

- Reacting to damage events:

```csharp
s.Reaction(s => {

    PlayHitAnimation();

}, DamageTaken);
```
