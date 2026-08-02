# Spoke.Godot Examples

Four small scenes, each isolating one idea, and then a whole game. 01 to 04 are a single script
each with no assets; 05 is a real project, with art and a scene per unit.

| | Scene | Shows |
|---|---|---|
| 01 | `01_Lifecycle/01_Lifecycle.tscn` | `Init` and `Phase` as lifecycle windows |
| 02 | `02_State/02_State.tscn` | `State<T>` and `s.D(...)` dependency tracking |
| 03 | `03_Effect/03_Effect.tscn` | `Effect` vs `Phase` vs `Reaction`, nesting, `Trigger` |
| 04 | `04_Memo/04_Memo.tscn` | `Memo<T>` derived state, and memos chaining |
| 05 | `05_BaseDefence/05_BaseDefence.tscn` | a complete game, built entirely on Spoke |

Open a scene and press **F6** to run it. Each prints its controls on screen; 01 also logs to the
Output panel.

## 01 — Lifecycle

Godot's lifecycle windows as Spoke phases. `Init` mounts as the node enters the tree and disposes
when it is freed; `IsInTree`, `IsReady` and `IsShown` are windows that open and close underneath it.
Setup and teardown live next to each other rather than in separate callbacks.

Press **T** to detach the node from the tree and watch its phases close and reopen — the Spoke tree
itself survives the trip, which is why reparenting doesn't reset your state.

## 02 — State

`State<T>` is a reactive variable. `s.D(signal)` reads it *and* registers it as a dependency, so the
block re-runs when it changes. Press **SPACE** and watch one effect re-run.

## 03 — Effect

The three block types side by side. All re-run when a dependency fires; they differ in when they
first mount — `Effect` immediately, `Phase` while a condition holds, `Reaction` not until something
triggers it. Toggle the phases with **1** and **2** to see nested blocks dispose bottom-up.

Also shows two Godot-specific things: reactive state reaching the Inspector through an ordinary
exported property (what replaces Unity's `UState<T>`), and `s.OnProcess` standing in for the
coroutine the Unity version of this example uses.

## 04 — Memo

`Memo<T>` is derived state — computed, not set. Memos chain: `labelText` depends on `evenOdd`, which
depends on `count`. Worth noticing that changing the count from 2 to 4 doesn't recompute `labelText`,
because `evenOdd` produced the same value both times.

## 05 — Base Defence

A tower defence game, ported from the Unity example of the same name, down to its balance values
and its level layout. Everything in it extends a Spoke node, and there's no `_Process`, no `_Ready`
and no show/hide bookkeeping in the whole folder.

Worth reading after the first four, for what the idioms look like at scale: a power grid that
propagates outages without polling, overlays that exist only while something wants them, and an
object pool whose reset story is "the scene tree already did it". It's also the one example laid
out like a real project — art, a scene per unit with its stats as exported values, and a root scene
holding those as `PackedScene` fields. Its own [README](05_BaseDefence/README.md) covers the game
and what the port changed.

---

## A note on the `.uid` files

Each script has a committed `.cs.uid` sidecar, and the scenes reference their scripts by `uid://`
rather than by path. That's what lets these scenes work no matter where Spoke ends up in a consuming
project. The rest of `Spoke.Godot` has its `.uid` files gitignored — only `Examples/` needs them.
