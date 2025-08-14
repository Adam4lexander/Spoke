# Changelog

## 1.1.2 - 2025-08-14

This release focuses on `FlushEngine` (previously SpokeEngine), and the choice to have a global engine for all behaviours, or each behaviour having its own. In past releases, every `SpokeBehaviour` had its own `FlushEngine`. This lets an engine flush, and during its flush trigger another to engine flush, resulting in a nested flush. This is powerful, but dangerous if not careful. The default now is a single global `FlushEngine` at the root of all `SpokeBehaviour` engines, so only one can flush at a time.

Nested flushing is still possible by creating seperate trees with `SpokeRoot.Create(new FlushEngine(...))`. But this is now an advanced opt-in feature, not the default.

Another important change is `s.Export` and `s.Import`. Example:

```cs
s.Effect(s => {
    s.Export(new SomeResource());
    s.Effect(s => {
        var resource = s.Import<SomeResource>();
    });
});
```

Exported objects are visible on the lexical scope. All descendants and later siblings can import it.

Changelist:

- `SpokeEngine` renamed to `FlushEngine`, and the base class `ExecutionEngine` takes the name `SpokeEngine`
- `FlushEngine.Batch()` is a static method that holds all engines from flushing. It replaces the per-engine batching
- `FlushEngine.Global` is one engine that all `SpokeBehaviour` will attach to. The implication is only one `SpokeBehaviour` has flush at once
- `FlushEngine.AddFlushRegion` lets you conveniently attach a nested `FlushEngine` under another. This is what `SpokeBehaviour` is using
- `s.Export` and `s.Import` replaces `TryGetLexical` and `TryGetContext`
- Correctly handle Epochs that dispose their own root, or drop themselves from a dock mid-execution
- Correctly order all `Call`, `OnCleanup` and `Use` resources in a single array. Now they are truly disposed in reverse-declaration order

## 1.1.1 - 2025-08-04

This update continues to refine the declarative lifecycle tree in `Spoke.Runtime`. The documented behaviour of Spoke and its reactive engine is unchanged. The reactivity APIs in Spoke are stabilizing, even though the runtime changes often.

Eventually the runtime will be a key abstraction in Spoke, where Reactivity is one possible modules built in top.

Key changes:

- Implemented `PackedTreeCoords` for efficient sorting of execution order
- Moved `Dock` from `Spoke.Reactive` to `Spoke.Runtime`. It's a core primitive of the epoch model
- Builders like `EffectBuilder` are structs instead of classes
- Add `UnityEvent` overloads for `EffectBuilder.Subscribe`
- Remove Node from `Spoke.Runtime`, now Epoch manages tree structure
- Implement functional composition style configuration of Epoch and ExecutionEngine, instead of abstract lifecycle methods
- Removed Epoch.GetSubEpochs. Epochs can look up, but not down. For safety reasons. An Epoch instance must explicitely offer its internal state upwards. Cannot poke down through the tree

## 1.1.0 - 2025-07-14

This is a big update with quite a few breaking changes. Sorry anyone who might be trying Spoke out! It's mostly just naming changes on the external API surface. The bigger changes are internal.

I was working on my game and found two areas in my code with a similar shape to Spoke:

- A CPU-budget aware coroutine-style system implemented in pure functions (no IEnumerator).
- A procedural generation system for populating levels with missions, bases, enemies and rewards.

Both these problems involved creating a tree of nested lifecycles. Just like Spoke. Even though they had nothing to do with reactivity.

I worked out a common abstraction to unify all three domains. It's a tree-execution engine implemented in `Spoke.Runtime.cs`. The reactive code, now in `Spoke.Reactive.cs`, is built on top of this runtime. The reactive behaviour is the same as before, just with some name changes. The key difference is that the mental model has solidified, it's all-in on the declarative lifecycle tree now. No more separate flush phase for Memos.

The key changes are:

- Added a new file `Spoke.Runtime.cs` that implements a base abstraction for experimenting with future modules
- Renamed `Spoke.cs` to `Spoke.Reactive.cs`, and refactored in terms of the runtime
- Removed all 'Use' prefixes from DSLs, `s.UseEffect()` is now `s.Effect()`. 'Use' made sense before, but no longer
- Removed the separate flush phase for memos. Now they are flushed in imperative order same as effects
- Added an `Effect<T>`, which is similar to Memo, but it can be nested, where Memo cannot
- Added `Epoch` as a base class for all objects that live in the lifecycle tree
- Added dynamic lexical scope. Where effects can find contextual objects in younger siblings, not just ancestors. Similar to how lexical scope works in programming languages

## 1.0.1 - 2025-06-26

- Convert all ids to use `long` instead of `int`
- Add `CreateContext` and `GetContext` to `EffectBuilder`
- Skip execution of Memos with an ancestor scheduled to remount
- Fix issue with `SpokeSingleton` not cleaning up when its scene is unloaded

## 1.0.0 - 2025-05-17

- Initial public release
- Core reactive engine (`Spoke.cs`)
- Unity integration (`Spoke.Unity.cs`)
- Example usage and documentation
