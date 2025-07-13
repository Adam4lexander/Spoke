# The mental model

This page explains the core concepts for Spoke, and the mental model it's built on. If you're just starting, don't worry about diving too deep here. You can refer back here as you become more familiar with Spoke.

At the foundation of Spoke is a tree-based execution model. It has special functions called _Epochs_, that when called, implicitely generate a tree structure to match the call stack. That way, an _Epoch_ outlives the call stack. It's scope persists in a call-tree as a live object. Later, when an _Epoch_ is disposed, first it will dispose the descending _Epochs_ in reverse imperative order.

I'm not sure if this execution model already has a name, but I found these resonate with me:

- **Structured Execution**: Where the act of executing code produces a live runtime structure
- **Lifecycle Oriented Programming**: Where the lifetimes of objects, and the hierarchy of lifecycles, are first-class citizens
