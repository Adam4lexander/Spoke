# Flush Logger

Spoke includes a built-in logging tool called the `FlushLogger`, which shows exactly what happens during a flush.

There are two ways to use it: through the `EffectBuilder` interface or via `SpokeEngine.LogBatch`.

---

## Usage: `EffectBuilder`

The `EffectBuilder` has a method `Log(msg)` that can be used anywhere within an effects code block.

```csharp
public class FlushLoggerTest : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {
        s.Log("Init ran");

        s.Phase(IsStarted, s => {
            s.Log("IsStarted Phase ran");

            s.Effect(s => {
                s.Log("Effect-A ran");
            });
        });
    }
}
```

This results in two debug messages in the Unity console — one for each flush.

The first will be:

```
[FLUSH]
-> Init ran

|--(0)-SpokeLogTest:Init
    |--(1)-Phase
```

The second will be:

```
[FLUSH]
-> IsStarted Phase ran
-> Effect-A ran

|--SpokeLogTest:Init
    |--(0)-Phase
        |--(1)-Effect
```

---

### Interpretation

During a flush, any call to `s.Log(msg)` adds a message to an internal log queue.

Once the flush completes, if any messages were logged, the `FlushLogger` is invoked.

The output has two sections:

- The first lists all logged messages in the order they occurred.
- The second shows the full ownership hierarchy of the flush.
  Numbers to the left of each effect or memo indicate the order in which they ran.

---

## Usage: `SpokeEngine.LogBatch`

You can also trigger the flush logger when batching reactive updates:

```csharp
SpokeEngine.LogBatch("My log message", () => {

    someState.Set("someValue");
});
```

This behaves just like `SpokeEngine.Batch`, but also invokes the `FlushLogger` if any logs were recorded during the flush.

---

## Exception Handling

If any effects or memos throw an exception the flush logger is invoked automatically.

```csharp
public class ExceptionLoggingTest : SpokeBehaviour {

    protected override void Init(EffectBuilder s) {

        s.Effect("SafeEffect", s => {
            // Safe Logic
        });

        s.Effect("DangerEffect", s => {
            throw new System.Exception("Something bad happened!");
        });
    }
}
```

When this happens, Spoke automatically logs the following error message:

```
[FLUSH ERROR]
->

|--(0)-SpokeLogTest:Init
    |--(1)-SafeEffect
    |--(2)-DangerEffect  [Faulted: Exception]


--- |--(2)-DangerEffect  ---
System.Exception: Something bad happened!
  at ExceptionLoggingTest+<>c.<Init>b__0_1
  Stack Trace Continues...
```

---

## Labeled Effects and Memos

Methods like `Effect` and `Memo` have overloads that take a string label as the first parameter.

This label determines how the node appears in the flush logger hierarchy, as shown in the previous example.

Labels are optional, but make the flush logs much easier to read, especially in large graphs.
