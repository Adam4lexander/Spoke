# Mental Model

This page explains the core concepts for Spoke, and the mental model it's built on. If you're just starting, don't worry about diving too deep. You can refer back here as you become more familiar with Spoke.

## Abstract

At the foundation of Spoke is a tree-based execution model. It has special functions called _Epochs_, that when called, implicitly generate a tree structure to match the call stack. That way, an epoch outlives the call stack. It's scope persists in a call-tree as a live object.

In the epochs function body, it defines continuations (cleanup functions) to be run later when it's detached from the call-tree. These continuations persist in the call-tree, as part of the epochs scope. When an epoch is detached, either by internal or external signal, **all epochs in descending lexical scope will be detached first, in reverse imperative order.**

Therefore, the call-tree encodes not just ownership, but also temporal causality. When an epoch is attached to the call-tree, it can trace an immutable pathway through its younger siblings and ancestors towards the root of the tree. This path forms a dynamic lexical scope that's guaranteed to be static over the epochs lifetime.

I'm not sure if this execution model already has a name, but I found these resonate with me:

- **Structured Execution**: Where the act of executing code produces a live runtime structure
- **Lifecycle Oriented Programming**: Where the lifetimes of objects, and the hierarchy of lifecycles, are first-class citizens

---

## Closures

Before diving into Spokes execution model, lets touch briefly on closures. A closure is created when a function constructs and returns another function, causing its stack-allocated variables to be moved to the heap:

```cs
Action CreateCounter() {
    var counter = 0;
    return () => Debug.Log($"Counter is {counter++}");
}

var counter = CreateCounter();
counter();  // Prints: Counter is 0
counter();  // Prints: Counter is 1
counter();  // Prints: Counter is 2
```

This example has a very simple closure. When you call `CreateCounter()` it's almost like instantiating a class with a private `counter` variable and a single method to increment it.

In all of Spoke's code examples you'll see a lot of nested lambdas, which results in nested closures. This is done very intentially. Spoke wouldn't be nearly as expressive without closures. They're the backbone for Spoke, and it's recommended to have a solid understanding of them to use Spoke comfortably.

---

## Tree-based execution

Let's look at a simple Spoke program:

```cs
var engine = SpokeEngine.Create(FlushMode.Immediate);

engine.Effect("root", s => {
    s.Effect("1", s => {
        s.Effect("11", s => {
            s.Effect("111", s => { });
        });
        s.Effect("12", s => {
            s.Effect("121", s => { });
        });
    });
    s.Effect("2", s => {
        s.Effect("21", s => {
            s.Effect("211", s => { });
        });
        s.Effect("22", s => {
            s.Effect("221", s => { });
        });
    });
});
```

Which constructs the tree structure:

```
(Tree Structure)            (Execution Order)

SpokeEngine                 1
│
root                        2
├── 1                       3
│   ├── 11                  4
│   │   └── 111             5
│   └── 12                  6
│       └── 121             7
└── 2                       8
    ├── 21                  9
    │   └── 211             10
    └── 22                  11
        └── 221             12
```

First notice that the `SpokeEngine` is an active object in the tree. When nodes attach to the tree, they find the nearest ancestor extending `ExecutionEngine`, and schedule themselves there. The engine decides the tempo of execution. `SpokeEngine` will always flush eagerly, executing all scheduled nodes in one synchronous flush. But other implementations are possible, like eagerly executing nodes up to a frame budget, or running nodes on a separate thread.

The second column shows the order the nodes are flushed. It's in a strict imperative order. They're flushed in the same order as their code is written. This is **the most important invariant of the mental model**. The call-tree doesn't just encode ownership, it encodes causality as well. For a node to exist in the tree, all its older siblings must exist too. Even subclasses of `ExecutionEngine` cannot break this invariant.

This invariant is vital for predictable execution behaviour when arbitrary subtrees are scheduled for remount. For example lets say a reactive signal triggers some of the effects to reschedule:

```
SpokeEngine
│
root
├── 1
│   ├── 11
│   │   └── 111*
│   └── 12
│       └── 121
└── 2*
    ├── 21*
    │   └── 211
    └── 22
        └── 221
```

The nodes scheduled for remount are marked by a `*`, including `111`, `2` and `21`.

According to imperative ordering, they will execute in the order `111`, `2` and then `21`. Exactly the order they appear in the tree. In fact since `21` is descending from `2` it would remount anyway, so the fact it was explicitely scheduled makes no difference.

The final execution order will be:

```
(Tree Structure)            (Execution Order)

SpokeEngine
│
root
├── 1
│   ├── 11
│   │   └── 111             1
│   └── 12
│       └── 121
└── 2                       2
    ├── 21                  3
    │   └── 211             4
    └── 22                  5
        └── 221             6
```

---

### Unmount behaviour

When a live node is scheduled for re-execution, it will cause a remount. First the node is unmounted, and then mounted again fresh, rerunning the logic associated to that node.

When a node unmounts, it will first detach its children in reverse-imperative order. When node `2` was scheduled before, the nodes were cleaned up in this order:

```
(Tree Structure)            (Cleanup Order)

.
└── 2                       5
    ├── 21                  4
    │   └── 211             3
    └── 22                  2
        └── 221             1
```

After cleanup it results in the following transitory structure:

```
(Tree Structure)

.
└── 2
```

Node `2` still exists in the tree, it's still 'attached', but its been unmounted. All its descendants have been detached and disposed, and its cleanup functions have run (in reverse declaration order). Immediately after unmounting `2`, Spoke will mount it once again to produce a new subtree.

---

### Deferred Execution

When a node is mounted, it attaches sub-nodes that are scheduled and mounted later. They're not mounted within the same call-stack frame. This has some subtle consequences to be aware of:

```cs
var engine = SpokeEngine.Create(FlushMode.Immediate);

engine.Effect("root", s => {
    var number = 5;
    s.Effect(s => number = 10);
    Debug.Log($"number is {number}");                   // Prints: number is 5
    s.Effect(s => Debug.Log($"number is {number}"));    // Prints: number is 10
});
```

Lets step through the construction of the call-tree to understand what's going on. First at the call to `engine.Effect(...)`:

```
SpokeEngine
│
root*
```

The Effect (named root) is attached to SpokeEngine. It's scheduled for mount, triggering a flush, but it's not mounted yet.

```
SpokeEngine
│
root            (number=5, print: "number is 5")
├── Effect*
│
└── Effect*
```

Now root is mounted, and its mount function is called. It initializes `number = 5`, it attaches two sub-effects, and it prints `number is 5`. The first Effect will set `number = 10`, but only when it mounts. It's not mounted yet, it's attached and scheduled for mounting.

```
SpokeEngine
│
root
├── Effect      (number=10)
│
└── Effect*
```

The first Effect is mounted and sets `number = 10`.

```
SpokeEngine
│
root
├── Effect
│
└── Effect      (prints: "number is 10")
```

This behaviour can take you by surprise if you're not expecting it. But there's a simple fix. If you have code that depends on the output of a sub-node, put that code in a sub-node too.

---

## Runtime classes

`Spoke.Runtime.cs` defines a handful of base classes that implement the tree execution model:

- `Node`: Is a runtime container for a mounted `Epoch` that forms the structure of the lifecycle tree. They're managed automatically by Spoke so you would rarely interact with them directly. Nodes form the fabric of the tree.

- `Epoch`: Is the foundational base class in Spoke. Epochs are invoked declaratively, mounted into nodes, and persist as active objects. They maintain state, respond to context, expose behaviour, and may spawn child epochs.

- `ExecutionEngine`: Is a type of `Epoch`, and an abstract class for controlling the tempo of execution of its subtree. When Epochs are attached to the tree, they schedule themselves on their contextual engine to be mounted.

This engine is the foundation that all the `Spoke.Reactive` behaviour is built on. Many of the reactive objects including `Effect`, `Reaction`, `Phase`, `Memo` and `Dock` are each derived from `Epoch`.

---

### Custom Epochs

You can define your own Epochs by subclassing `Epoch`:

```cs
public class MyCustomEpoch : Epoch {

    public bool IsAttached { get; private set; }
    public bool IsMounted { get; private set; }

    public MyCustomEpoch() {
        Name = "My Epoch"; // Optionally set the name, which will show in debugging views
    }

    protected override void OnAttached(Action<Action> onDetach) {
        // For logic that outside the Mount/Unmount phase
        IsAttached = true;
        onDetach(() => IsAttached = false);
    }

    protected override void OnMounted(EpochBuilder s) {
        // For declaring child epochs or declaring setup/cleanup of lifecycle-bound resources
        IsMounted = true;
        s.OnCleanup(() => IsMounted = false);
    }
}
```

> At the beginning of the page I said an epoch was a function with continuations to run when detached.<br>
> That's the abstract mental model.<br>
> In Spoke it's a class, with 2 epochs in one. OnAttach and OnMount would be distinct nested epochs as I originally defined them. Spoke combines them for convenience.

You can attach the custom epoch into the call-tree by 'calling' it from a parent epoch:

```cs
var engine = SpokeEngine.Create(FlushMode.Immediate);

engine.Effect("root", s => {
    var myEpoch = s.Call(new MyCustomeEpoch()); // Attaches to the tree and returns the epoch instance
    Debug.Log(myEpoch.IsAttached);              // Prints: true
});
```

This would result in the following tree structure:

```
Node[SpokeEngine]
│
Node[Effect(name = "root")]
│
Node[MyCustomEpoch(name = "My Epoch")]
```

Note the `s.Call()` expression. This is the fundamental way for epochs to be 'called' into the lifecycle tree. In `Spoke.Reactive` calls like `s.Effect()` and `s.Memo()` are convenience sugar over `s.Call()`:

```cs
s.Effect("myEffect", s => {});

// Is equivalent to

s.Call(new Effect("myEffect", s => {}));
```

Defining custom Epochs is generally not needed outside of some advanced use cases. The `Spoke.Reactive` epochs already implement a functional composition style via `EffectBuilder`. There are two main reasons to define new epochs:

1. To define a custom DSL in a new problem domain. For example, something like `EffectBuilder`, but designed for procedural generation.
2. To define a lifecycle-controlled manager that facades a sub-tree and gives context to lexically scoped epochs.

I'll expand on these use cases elsewhere. For now it's enough to know that everything in Spoke's call-tree is a kind of `Epoch`, and they are all bound to the same tree-based execution behaviour.

---
