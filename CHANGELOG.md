# Changelog

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
