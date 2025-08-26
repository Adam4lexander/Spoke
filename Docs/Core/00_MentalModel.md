# Mental Model

This page explains the core concepts for Spoke, and the mental model it's built on. If you're just starting, don't worry about diving too deep. You can refer back here as you become more familiar with Spoke.

## Abstract

Spoke implements an incremental tree-based execution model. It lets you declare a tree of behaviours, executed in imperative order, and reactively teardown/rebuild subtrees from dynamically changing runtime state. It resembles frontend frameworks like React, but with stricter invariants on execution order. In React you don't normally think about the order components (tree nodes) are mounted, in Spoke you do.

Using Spoke is like writing a coroutine, that runs forwards and in reverse, and that dynamically recompiles and relinks sections of code at runtime. It's not literally doing this. Spoke is built entirely from vanilla C#. But its behaviour is roughly equivalent.

The tree execution model is implemented in `Spoke.Runtime`. The fundamental primitive is the `Epoch`. Every node in the tree is a kind of epoch. Each epoch declares a block of code to run, which may make side-effects, attach child epochs and declare cleanup callbacks. The runtime successively 'ticks' these epochs, causing its code block to run, attaching its child epochs, and scheduling a new batch of epochs to be 'ticked'. If a live epoch is ticked again, it first cleans up from the last time. Child epochs are detached and disposed, and cleanup callbacks are executed. The epochs code block is run again fresh, which may produce a new subtree of epochs and callbacks.

Spoke strongly emphasizes an imperative execution order. Epochs attach their children in the order the code is written, and on cleanup they are detached in the reverse order. Spoke may look declarative, but it is highly imperative. Spoke optimizes for cognitive clarity over raw performance. It builds up trees in the most predictable and intuitive order, instead of the most optimal order. It wants to feel like writing normal imperative code, even when execution flow is indirect and chaotic.

Spoke's intended use case is to be the architectural spine of application logic. It's a meta-architecture where you declare and wire up subsystems depending on the state of the application. It shines most strongly where long-lived behaviours must interact in dynamic and emergent ways. Spoke collapses complexity, which normally results in spaghetti code, into a tree of localized and coherent bundles. Where lifecycles, setup and teardown of application components are managed automatically. Spoke was built for the requirements of complex games in Unity, but its generalised to any C# application with these requirements.

---

## Foreword: When to extend Spoke.Runtime

The information here is mostly to document the runtime behaviour of Spoke, so you understand clearly what's happening in the background when using `Spoke.Reactive`. In practice you'll rarely need to define your own custom Epochs or Tickers.

`Spoke.Runtime` is a low level substrate that enables modules like `Spoke.Reactive` to be built on top. Think of it as a low-level layer for building custom reactive DSLs. Not so much for implementing application logic.

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

In all of Spoke's code examples you'll see a lot of nested lambdas, which results in nested closures. This is done very intentionally. Spoke wouldn't be nearly as expressive without closures. They're the backbone for Spoke, and it's recommended to have a solid understanding of them to use Spoke comfortably.

---

## Performance and GC churn

This documentation may give the impression that Spoke has a large performance and GC cost. Frequently tearing down and rebuilding the tree will cause a large amount of GC churn. Instantiating epochs and creating closures are both a source of GC.

In practical usage though, this cost can be managed. Higher frequency rebuilds tend to be pushed towards the leaves of the tree, where allocations are minimized or avoided completely. Big structural rebuilds of the tree are often results of big structural changes in game logic, like changing from one game mode to another. Also, Spoke isn't designed to be ticked every frame. It's best used to orchestrate the subsystems that do tick each frame, by wiring them up and configuring them to match changing application state.

By keeping Spoke focused on the architectural shape of the code, instead of frame-by-frame logic, it can pay for itself through improved clarity and enabling optimizations that would be too complex to express without it. In my own game, Spoke was a massive improvement to performance. Not because Spoke is fast, its not, but because it let me move so much logic out of Update() loops into event chains that only run when necessary.

---

## Primitives

The core primitives in `Spoke.Runtime` are:

- `Epoch`: A stateful, lifecycle aware node in the spoke tree. They have three lifecycle phases: Attach, Tick and Detach.
- `Ticker`: An epoch that controls the tempo of ticks to its subtree. Nested tickers form a chain of execution gateways for advanced control structures and fault boundaries.
- `SpokeTree`: A ticker that must be at the root of the tree. It exposes the interface for ticking the tree, either reactively by the spoke runtime, or manually by user code.
- `Dock`: An epoch that can dynamically attach and detach keyed subtrees of epochs. It lets you modify the tree outside the `Init` and `Tick` mutation windows.

All primitives are derivations of `Epoch`, and are bound to the same lifecycle contracts. You use Spoke by implementing subclasses of `Epoch` and `Ticker`, composing in `Dock` where needed, and mounting it under a `SpokeTree`.

---

### Epoch

The name _Epoch_ refers to its role as a lifetime window. They mark a new epoch in program structure. All epochs define three lifetime phases:

- **Attach**: When the epoch is attached to the tree, under its parent epoch, it runs `Init()` to declare attachments which persist for the duration of its lifetime.
- **Tick**: When ticked, an epoch declares ephemeral attachments, which exist until the next time it's ticked. These attachments are stored after those declared in `Init`.
- **Detach**: The epoch traverses backwards over its list of attachments, detaching each one in turn. Including all child epochs, cleanup callbacks and `IDisposable` resources.

Now, lets see a very simple implementation of an epoch:

```cs
public class CounterEpoch : Epoch {

    // Expose an API to increment the counter. We'll bind the incrementCommand action to its behaviour
    // inside the Init block.
    Action incrementCommand;
    public void Increment() => incrementCommand?.Invoke();

    protected override TickBlock Init(EpochBuilder s) {
        // Init is called immediately after the epoch is attached to its parent.
        // Declare attachments here that must persist over the epochs lifetime (until its detached)

        // EpochBuilder has DSL functions for attaching children. It can only be used during Init or Tick,
        // and cannot be captured in a lambda or variable for later usage. The exception is anything under Ports.
        incrementCommand = () => s.Ports.RequestTick();

        // Set incrementCommand to null when this epoch detaches, reversing the mutation we just made.
        // Its not strictly required, calling RequestTick() on a detached epoch won't do anything, but it's a good
        // habit to form for the cases where it does matter.
        s.OnCleanup(() => incrementCommand = null);

        int counter = 0;

        // Init must return a 'TickBlock', which is a delegate that's executed each time the epoch ticks.
        // This pattern was chosen for convenience. The TickBlock can capture a closure with everything
        // defined during Init.
        return (EpochBuilder s) => {
            // Increment the counter we captured in our closure
            counter++;

            // Log a message showing how many times we were ticked
            s.Log($"CounterEpoch was ticked {counter} times!");
        };
    }
}
```

> RequestTick() doesn't mean it will happen immediately, although in default cases it often does. The epoch schedules itself on its nearest ticker, which potentially has its own ticker. The tick could be deferred for a number of reasons: the epoch is already ticking, or a different epoch in the tree is prioritised for the next tick. We'll get to this later, the rules controlling this are predictable, but in general requesting to tick only flags the intention. The Spoke runtime ultimately decides when it happens.

Now we can test `CounterEpoch` by spawning a new `SpokeTree`:

```cs
// The CounterEpoch requests a tick immediately after Init, so straight away you'll see a log message
// that it was ticked once.
var tree = SpokeTree.Spawn(new CounterEpoch());

// Call Increment() to trigger a second log message. SpokeTree.Main refers to the top-most epoch passed
// to SpokeTree.Spawn(). Think of it like the main function of a Spoke program. Its exposed so you can
// get at any public API methods you want on the top-most epoch.
tree.Main.Increment();

// When finished, call tree.Dispose to detach the entire tree. Including the SpokeTree epoch itself.
tree.Dispose();
```

If you did run this you would see log messages in the console that look like this:

```
CounterEpoch was ticked 1 times!

<------------ Spoke Frame Trace ------------>
0: Bootstrap SpokeTree <SpokeTree>
1: Tick SpokeTree <SpokeTree>
2: Tick CounterEpoch <CounterEpoch>
3: Init Log: CounterEpoch was ticked 1 times! <LambdaEpoch>

<------------ Spoke Tree Trace ------------>
|--(0,1)-SpokeTree
    |--(2)-CounterEpoch
        |--(3)-Log: CounterEpoch was ticked 1 times!
```

The Spoke runtime implements its own virtual stack. With stack frames logged when you called `s.Log()`. The _Spoke Tree Trace_ dumps a structural representation of the tree, with stack frame indices shown against their associated epochs. This comes in useful for debugging issues and understanding execution flows.

You may be surprised to see that `s.Log()` actually attached an epoch (type `LambdaEpoch`). Log messages are persisted into the tree, as epochs, so they're visible when introspecting the tree.

---

#### Epoch attachments

Let's examine the attachment and detachment behaviour of epochs more closely:

```cs
public class MyEpoch : Epoch {

    protected override TickBlock Init(EpochBuilder s) {
        // 'Call' in an epoch to attach it. The word 'Call' reflects how Spokes imperative execution
        // order constructs a tree of epochs extruded from the virtual Spoke stack.
        s.Call(new SomeEpoch());

        // Take ownership of an IDisposable object. It will be disposed automatically.
        s.Use(new SomeResource());

        // Subscribe to an event, and unsubscribe on detachment.
        Action eventHandler = () => Debug.Log("Event triggered!");
        SomeEvent += eventHandler;
        s.OnCleanup(() => SomeEvent -= eventHandler);

        return s => {
            s.Call(new SomeOtherEpoch());
        };
    }
}
```

After Init, `MyEpoch` will have the attachment list: `[SomeEpoch, SomeResource, OnCleanup]`.

Then after Tick, the attachment list will be extended to: `[SomeEpoch, SomeResource, OnCleanup, SomeOtherEpoch]`. All attachments are appended to the same list, in the order that they are declared. The epoch remembers which index the TickBlock started from, which is index 3 in this case. On subsequent ticks, attachments are rolled back to this index. When `MyEpoch` detaches, the entire list is rolled back. This is what I meant by Spoke resembling a coroutine that runs forwards and backwards.

Spoke assumes that in imperative code, dependencies are fed forward in a chain of causality. Objects tend to only depend on those created before it. Detaching in reverse order unwinds this same chain of causality.

---

#### Choosing Init or Tick

Both `Init` and `Tick` take an `EpochBuilder`, giving the same capabilities for adding attachments. Which one you choose to rely on depends on context, as they have different execution semantics: Init happens synchronously on attachment, while Tick is deferred.

You could use Init for everything, and ignore Tick completely. But then you would lose the incremental computation abilities. The whole tree would be constructed in a single tick. And the whole tree would be torn down, from the root, in a single `SpokeTree.Dispose()`. For some use cases, that might be desirable.

In general I stick to the following patterns:

- Put simple initialization in Init, so the epochs public API (if any) is wired up even before its been ticked.
- Put all other logic in Tick.

---

#### Fault Handling

Epochs catch exceptions thrown from Init or Tick, and wraps them in a `SpokeException`, including a snapshot of the virtual Spoke stack at the point of failure. The exception propagates upwards through the chain of epochs and tickers, marking each one as faulted on the way. The `SpokeTree` catches the exception and marks itself as faulted. All epochs and tickers which faulted will no longer be able to tick.

You can catch the `SpokeException` at any point to stop it propagating further up the tree. If it reaches the `SpokeTree` then the entire tree has faulted and will cease to be executed. You can access the fault marked on an epoch with `Epoch.Fault`.

Let's see an example and the resulting stack trace:

```cs
// Define an epoch which throws an exception on tick
public class MyBrokenEpoch : Epoch {
    protected override TickBlock Init(EpochBuilder s) {
        return s => {
            throw new Exception("An error has occurred");
        };
    }
}

// Spawn a tree to run the epoch, and observe the fault
var tree = SpokeTree.Spawn(new MyBrokenEpoch());

// Access the fault from either the tree or the MyBrokenEpoch, they will both have it
var isFaulted = tree.Fault != null;  // True
```

If you catch the exception before it reaches the SpokeTree then you can contain the fault and stop the whole tree from becoming faulted. This is one use case for implementing custom tickers.

---

#### Exports and Imports

Spoke implements a simple kind of dependency injection, where resources can be exported from one epoch, and then imported by other epochs further down the tree. This lets you share common dependencies without having to explicitely feed it down through the tree via epoch constructor props.

In Spoke, exports are lexically scoped. Which may be surprising if you're familiar with React. Epochs import resources, not just from their direct ancestors, but also their earlier siblings, their parents earlier siblings and so on. It's functionally equivalent to typical scoping rules in programming languages.

```cs
// Define a contextual data object to share in the tree
public class ContextData {
    public string Data { get; }
    public ContextData(string data) => Data = data;
}

// Define an epoch that imports and uses the context object
public class ImportingEpoch : Epoch {
    protected override TickBlock Init(EpochBuilder s) {
        if (s.TryImport<ContextData>(out var ctx)) {
            s.Log($"Context is {ctx.Data}");
        } else {
            s.Log($"Cannot find context");
        }
        return null;
    }
}

// Define an epoch to export the contextual data, and attach epochs that import it
public class MainEpoch : Epoch {
    protected override TickBlock Init(EpochBuilder s) {
        s.Call(new ImportingEpoch());       // Will log: Cannot find context
        s.Export(new ContextData("Foobar"));
        s.Call(new ImportingEpoch());       // Will log: Context is Foobar
        s.Export(new ContextData("Baz"));
        return s => {
            s.Call(new ImportingData());    // Will log: Context is Baz
        };
    }
}
```

The list of attachments for `MainEpoch` would look like this:

`[ImportingEpoch, Export<ContextData>, ImportingEpoch, Export<ContextData>, ImportingEpoch]`

`s.Import` searches backwards through the attachment list of its epoch, up to the parents attachment list and backwards through that, all the way to the root. It stops once it finds an Export with a matching type. Lexical scoping is possible in Spoke because of the strict imperative execution. Once an epoch is attached, its guaranteed that everything on its dynamic lexical scope will exist over its lifetime.

---

#### `LambdaEpoch`

Instead of subclassing from `Epoch`, you can use `LambdaEpoch` to define them in a functional composition style. Its constructor takes an `InitBlock`, which is a function delegate type matching the `TickBlock Init(EpochBuilder s)` method you'd normally override in `Epoch`:

```cs
SpokeTree.Spawn(new LambdaEpoch("Main", s => {
    s.Log("Main is attached");
    s.Call(new LambdaEpoch("Child1", s => {
        s.Log("Child1 is attached");
        return s => {
            s.Log("Child1 is ticked");
        };
    }));
    return s => {
        s.Log("Main is ticked");
        s.Call(new LambdaEpoch("Child2", s => {
            s.Log("Child2 is attached");
            return s => {
                s.Log("Child2 is ticked");
            };
        }));
    };
}));
```

It's the same pattern as `Effect` taking an `EffectBlock` in `Spoke.Reactive`. They're great for declaring inline, one-off behaviour without having to define a new class. If you want to deep dive Spokes runtime behaviour, the `LambdaEpoch` is a great tool for writing short, self-contained test cases.

---

### Ticker

A `Ticker` is an abstract class, and a type of epoch that acts as an execution gateway for ticking the epochs descending from it. You've already seen the `SpokeTree`, which is a ticker that must exist at the root of the tree. It's possible to define other tickers, which can be nested in the tree.

When an epoch attaches to the tree, it finds its nearest ancestor ticker and records it. When the epoch requests to be ticked, that request is directed to this ticker. The same is true for tickers. When they have descendants requesting a tick, the ticker will first need to request a tick from its own ticker. The exception is `SpokeTree`, as its the root of the tree, it doesn't have any tickers beyond it.

Tickers may implement fault boundaries, loops, retries and other control structures. Let's see how to implement a ticker fault boundary:

```cs
// The FaultBoundary ticker should capture any faults thrown from its descendants and trap them so they don't
// bubble up the tree. Its subtree will be marked faulted, and cease to tick. But the rest of the tree can
// continue on.
public class FaultBoundary : Ticker {

    Epoch childEpoch;

    public FaultBoundary(Epoch childEpoch) {
        this.childEpoch = childEpoch;
    }

    // Tickers must implement a Bootstrap method, where they wire up their ticking logic.
    // This is different from the Init method in Epoch. Although it is executed during the
    // Init phase.
    protected override Epoch Bootstrap(TickerBuilder s) {

        // Store ports, which includes the Pause() method that we'll need later
        var ports = s.Ports;

        // Register a callback when this ticker receives a tick from its ticker. When the
        // ticker has pending epochs to tick, it automatically schedules itself, unless
        // its paused. You don't explicitly RequestTick on a ticker.
        s.OnTick(s => {
            // Call TickNext(), wrapped in a try-catch. It will tick the next pending epoch
            // and catch any exceptions. You can call TickNext as many times as you want
            // inside a single OnTick block.
            try {
                s.TickNext();
            } catch (SpokeException ex) {
                // An exception occured. Log the error, and then pause the ticker, so it
                // stops receiving ticks that trigger the OnTick callback. We've caught
                // the error so it won't propagate up to the next ticker.
                Debug.LogError("Fault boundary caught an error, will stop", ex);
                ports.Pause();
            }
        });

        // Bootstrap should return an unattached epoch that will be attached as the first
        // descendant under this ticker. Here we've supplied it via a constructor prop.
        return child;
    }
}
```

> OnTick must advance execution by calling TickNext(), or pause itself. If it does neither then it will throw an exception. As this is high-risk behaviour for inducing infinite loops.

Tickers are powerful because they force execution flow through them to Tick any epochs that descend from them. They enable complex control structures, even incremental loops, where looping conditions are derived from the completion of subtrees attached on each iteration. I've found them extremely useful in DSLs for procedural generation, not so much for general reactivity like in `Spoke.Reactive`.

---

#### Ordering Epochs by Tree Coords

Tickers control the tempo of execution, by choosing how often `TickNext()` is called. But they have no control over which epoch is ticked. The ticker maintains an ordered list of epochs that requested a tick. The epochs are sorted by their _Tree Coords_, which is a list of numbers that reflects the position in the tree when walked in an imperative order.

For example in this tree:

```
(Tree Structure)            (Tree Coord)

SpokeTree                   []
│
Main                        [0]
├── epoch                   [0,0]
│   ├── epoch               [0,0,0]
│   └── epoch               [0,0,1]
└── epoch                   [0,1]
    ├── epoch               [0,1,0]
    └── epoch               [0,1,1]
```

And lets see the order of execution when there are multiple epochs in the tree which have requested a tick:

```
(Tree Structure)            (Tick Order)

SpokeTree                   -
│
Main                        -
├── epoch                   -
│   ├── epoch(*)            1st
│   └── epoch               -
└── epoch(*)                2nd
    ├── epoch(*)            3rd
    └── epoch               -
```

> Epochs marked with `(*)` are requesting a tick.

It's equivalent to walking the tree in imperative order, skipping epochs that don't need ticks, and ticking the epochs that do. The epoch marked _3rd_ won't get a tick, because it will be detached first when its parent is ticked _2nd_.

Now I've oversimplified this a bit. Epochs maintain two Tree Coords, one for their personal coordinate, and one for the coordinate of their _Tick Cursor_. Since epochs can attach children in their Init phase, their _Tick Cursor_ is the coordinate from where their ephemeral attachments start. This is the coordinate used for sorting. Epochs attached in the Init phase will be ticked before their parent, while Epochs attached in the Tick phase will be ticked after.

In case it sounds complicated, the objective is to make ticking order as intuitive as possible. If you imagine your Spoke code as an imperative, stackful program, then its simple to intuit the ticking order. The Tree Coords should enable this intuition, without needing to be thought of directly.

---

### SpokeTree

The `Spoketree` is a special kind of ticker that must exist at the root of a tree. All ticks within the tree must pass through `SpokeTree`, and these signals will originate from outside the tree. Generally, `SpokeTree` will flush its scheduled epochs on receiving a single tick. Although its possible to construct a manually flushed `SpokeTree` and tick it incrementally from user code.

---

#### Flushing

To be precise what a flush is, here is an ultra-simple example of a ticker that flushes:

```cs
public class FlushingTicker : Ticker {
    Epoch child;

    public FlushingTicker(Epoch child) => this.child = child;

    protected override Epoch Bootstrap(TickerBuilder s) {
        var ports = s.Ports;
        s.OnTick(s => {
            // On receiving a single tick, tick all pending epochs in a loop until there is
            // nothing remaining. Holds the thread until the entire subtree has stabilized
            while (ports.HasPending) s.TickNext();
        });
        return child;
    }
}
```

> In practice, you'd probably want to add guards against potential infinite loops, in case the subtree oscillates and never finishes ticking.

---

#### Spawning Trees

The easiest way to spawn trees is by the convenience methods on `SpokeTree`:

```cs
// Spawns a default auto-flushed tree. Pass it an epoch that will be attached directly below the
// SpokeTree ticker. This tree will request ticks from the Spoke runtime automatically, when
// it has any pending work.
SpokeTree.Spawn(Epoch main);

// Spawns an auto-flushed tree, but with a higher priority flush-layer compared to Spawn().
// The flush-layer determines when a tree can start flushing, during the flush of another.
// This is called a nested flush, which we'll discuss soon.
SpokeTree.SpawnEager(Epoch main);

// Spawns a tree thats manually driven by user code. You call SpokeTree.Flush() to initiate
// a flush, or SpokeTree.Tick() to progress one tick.
SpokeTree.SpawnManual(Epoch main);
```

---

#### Auto Flushed Trees

Both `SpokeTree.Spawn()` and `SpokeTree.SpawnEager()` will spawn a tree in auto-flush mode. This means the tree is flushed automatically by the runtime. When the `SpokeTree` receives any tick requests from its dependants it requests a tick from the Spoke runtime. The only difference between them is the flush layer. `SpawnEager()` creates a tree with a higher flushing priority then `Spawn()` does.

Spoke is single-threaded, and it can support multiple trees. That means, at a given time there can be any number of trees requesting a tick. The runtime has strict rules for orchestrating these requests and deciding which tree flushes when. The rules are straightforward:

- For any given flush layer, there can be only one tree being flushed at a time. Pending trees are ordered by their creation time, so older trees are flushed before newer trees.
- If a tree is flushing, and a tree of a higher priority flush layer is scheduled, then a nested flush is initiated. The incoming tree is flushed synchronously, before passing control back to the first one to finish its flush.
- If a tree is flushing, and a new tree **of equal or higher priority is spawned**, then a nested flush is initiated. This rule overrides the first, and its the only time that a tree can flush nested in another of equal flush layer.

Lets see some examples to drive home how these rules work in practice:

```cs
// SpokeRuntime.Batch() holds the runtime from running anything while inside the code block.
// I'm using it to ensure both trees are scheduled before the runtime begins flushing anything.
SpokeRuntime.Batch(() => {
    SpokeTree.Spawn("Tree1", new LambdaEpoch(s => s => s.Log("Tree1 Flushed")));
    SpokeTree.Spawn("Tree2", new LambdaEpoch(s => s => s.Log("Tree2 Flushed")));
});
// Will show, "Tree1 Flushed", "Tree2 Flushed"
// Tree1 is older so it will always flush before Tree2, if the runtime has to choose.

SpokeRuntime.Batch(() => {
    SpokeTree.Spawn("Default", new LambdaEpoch(s => s => s.Log("Default Flushed")));
    SpokeTree.SpawnEager("Eager", new LambdaEpoch(s => s => s.Log("Eager Flushed")));
});
// Will show: "Eager Flushed", "Default Flushed"
// The eager one is flushed first, but its not nested. A nested flush would only happen if the
// eager one was scheduled while the default one was mid-flush.

// So lets see an actual nested flush in action.
SpokeTree.Spawn("Outer", new LambdaEpoch(s => s => {
    s.Log("Before");
    SpokeTree.Spawn("Inner", new LambdaEpoch(s => s => {
        s.Log("Inner Flushed");
    }));
    s.Log("After");
}));
// Will show: "Before", "Inner Flushed", "After"
// This is the one chance "Inner" has to flush nested in "Outer".
// Because they have equal flush layers.
```

Let's see the Spoke stack trace produced by that log: "Inner FLushed":

```
<------------ Spoke Frame Trace ------------>
0: Bootstrap Outer <SpokeTree>
1: Tick Outer <SpokeTree>
2: Tick LambdaEpoch <LambdaEpoch>
3: Bootstrap Inner <SpokeTree>
4: Tick Inner <SpokeTree>
5: Tick LambdaEpoch <LambdaEpoch>
6: Init Log: Inner Flushed <LambdaEpoch>

<------------ Spoke Tree Trace ------------>
|--(0,1)-Outer
    |--(2)-LambdaEpoch
        |--Log: Before

|--(3,4)-Inner
    |--(5)-LambdaEpoch
        |--(6)-Log: Inner Flushed
```

> The stack trace shows both trees: "Outer" and "Inner". Nested flushes are reflected on the stack so they're visible and debuggable in case of problems.

So why all the rules? And why allow nested flushes at all? The third rule, where trees can flush nested on creation, is extremely useful in Unity. Every `SpokeBehaviour` has its own `SpokeTree`. Often, in Unity, a `MonoBehaviour` will instantiate another, via `AddComponent<T>()`. If a `SpokeBehaviour` instantiates another, it aligns with expectations, that the second will be fully initialized by the time control returns to the first. Just like MonoBehaviours `Awake` and `OnEnable` are invoked synchronously when you `AddComponent`.

A deeper reason these rules were chosen, is to enforce a one-way control flow. If tree A can be nested in B, then B can never be nested in A. If this wasn't true then nested flushing would be unpredictable and dangerous.

Finally, I haven't explained the purpose for `SpawnEager()`. It's intended to model multi-phase flushes. It enables write-then-read, where a tree can set a reactive signal, and then read a derived signal with its fresh value. It's an advanced feature, and probably best ignored unless you're very familiar with Spoke.

---

#### Manual Flushed Trees

A tree created with `SpokeTree.Manual()` is under user-control. You decide when it ticks, or when it flushes:

```cs
var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
    s.Call(new LambdaEpoch(s => s => s.Log("1")));
    s.Call(new LambdaEpoch(s => s => s.Log("2")));
    s.Call(new LambdaEpoch(s => s => s.Log("3")));
}));

// Either call tick
tree.Tick();    // Logs: 1

// Or call flush
tree.Flush();   // logs: 2 then 3
```

> '1' wasn't logged on flush because it had already been ticked once

Calling `Tick()` or `Flush()` is guaranteed to happen synchronously. Although an exception will be thrown if re-entrancy is detected. Nested flushes from auto-flushed trees are possible. The manual tree has the same flush layer as the tree created from `SpokeTree.Spawn()`.

Manually flushed trees are rarely useful with `Spoke.Reactive`. I built it for a procedural generation DSL I was experimenting with. It lets trees be ticked. So heavy proc gen logic can be spread across frames without causing lag spikes.

---

### Dock

The `Dock` is the final primitive in Spoke runtime arsenal, and it's an important one. It lets you dynamically attach keyed epochs to the tree, outside the normal mutation windows: Init and Tick.

```cs
// Spawn a tree with a dock inside
SpokeTree.Spawn(new LambdaEpoch(s => {
    dock = s.Call(new Dock());
    return null;
}));

// Attach a new epoch to the dock
dock.Call("key", new LambdaEpoch(s => {
    Debug.Log("Docked epoch attached");
    s.OnCleanup(() => Debug.Log("Docked epoch detached"));
    return null;
}));
// Will print: Docked epoch attached

// Then drop the docked epoch, passing its key to dispose it
dock.Drop("key"); // Will print: Docked epoch detached
```

> If you `Dock.Call()` and re-use the key of an existing docked epoch, that existing epoch will first be attached. Before the new one is attached.

Docks are dynamic containers for epochs. They're very often used for event handlers. For example, say you have an event that triggers when an enemy is nearby, and you want to attach an epoch to track that enemy. You would need to use a dock. Because the event would have been triggered outside the parents mutation window.
