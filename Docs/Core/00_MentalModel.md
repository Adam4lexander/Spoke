# The mental model

This page explains the core concepts for Spoke, and the mental model it's built on. If you're just starting, don't worry about diving too deep here. You can refer back here as you become more familiar with Spoke.

At the foundation of Spoke is a tree-based execution model. It has special functions called _Epochs_, that when called, implicitely generate a tree structure to match the call stack. That way, an _Epoch_ outlives the call stack. It's scope persists in a call-tree as a live object. Later, when an _Epoch_ is disposed, first it will dispose the descending _Epochs_ in reverse imperative order.

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
