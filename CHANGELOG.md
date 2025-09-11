# Changelog

## 1.2.1 - 2025-09-11

This release brings some minor tweaks and a performance boost.

ChangeList:

- Implemented a min-heap in `Spoke.Runtime`, which gave a decent boost to performance.
- Tweaked semantics for nested tree flushes. A tree is given a priority boost while the scope it was created in remains on the stack. It can flush nested multiple times during this window, it's not just a one-off eager tick.
- Tweaked the `SpokeSingleton` so its Init method is abstract, not virtual.

## 1.2.0 - 2025-09-04

This is the real v1.0, I'm happy where `Spoke.Runtime` has ended up, and don't expect to make any more big structural changes.

`Spoke.Reactive` isn't impacted if its being used via `SpokeBehaviour`. The `FlushEngine` was removed, and its capabilities put in `SpokeTree`.

ChangeList:

- `SpokeTree`, in the `Spoke.Runtime` module is the root ticker of the tree. It can be auto-flushed by the runtime (taking over from `FlushEngine`), and it can be incrementally ticked by user code.
- Rewrote all documentation, both for `Spoke.Reactive` and `Spoke.Runtime`.
- Split all code into separate files, and commented them. To make the code more readable and understandable.
- Added infinite loop guards to `SpokeRuntime`.

## 1.1.3 - 2025-08-20

Many big changes in `Spoke.Runtime`, although the `Spoke.Reactive` APIs are unchanged. This release adds better logging, a virtual Spoke stack trace, and introspection abilities. The global flush engine from the previous release has been removed. I found a way to make nested flushes safe and predictable.

Nested flushes are very useful in the context of Unity. If one `SpokeBehaviour` instantiates another, its intuitive that the new behaviour will flush immediately to initialize itself. Therefore this release adds some rules where a nested flush is allowed:

- Trees are divided by priority layers, where higher priorities can flush nested in lower priorities. The layer is decided by calling `SpokeRuntime.SpawnTree` or `SpokeRuntime.SpawnEagerTree`.
- When a `SpokeTree` is created during the flush of another, it's allowed to flush once, inside the existing flush. But only if it's on an equal or higher priority layer.
- In all other cases trees are deferred and flushed in the order they were created. Each tree is assigned a timestamp at creation time.

These rules enforce a strict, one-way decision when a nested flush can occur. If A can be nested in B, then B cannot be nested in A.

Changelist:

- `SpokeRuntime` is the global orchestrator for Spoke trees. It's a static class which exposes capability to spawn trees and batching updates.
- A virtual stack is implemented and exposed in `SpokeRuntime`. Spoke produces stack frames when epochs are initialized and ticked.
- Improved logging will print a virtual stack trace in tree-form. So execution flow across the tree structure is apparent.
- `SpokeIntrospect` provides functions for traversing the epoch tree, which could be used in visualizations.
- `FlushLogger` has been removed and replaced with error logging that shows the Spoke stack.
- `SpokeException` is propagated up the Spoke tree, with a snapshot of the Spoke stack at the point of failure.
- Removed `FlushEngine.Batch` (Now in SpokeRuntime), `FlushEngine.Global` and `FlushEngine.AddFlushRegion`.

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
