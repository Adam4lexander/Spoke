---
title: Teardown
---

Unity has a frustrating quirk when unloading a scene: it calls `OnDisable()` and `OnDestroy()` on all behaviours in a **nondeterministic order**.

This means one component may try to access another that's already been destroyed, resulting in hard-to-track exceptions. To avoid it, you'd need to defensively guard almost every line of teardown logic.

Spoke provides a clean solution: `UnitySignals`. This utility exposes reactive signals for Unity lifecycle events. `SpokeBehaviour` subscribes to these signals, letting you **tear down scenes safely and deterministically**, without risk of Unity's destruction-order bugs.

---

## Problem Description

Consider this `MonoBehaviour`:

```csharp
public class MyMonoBehaviour : MonoBehaviour {

    [SerializeField] Renderer myRenderer;

    void OnEnable() {
        myRenderer.sharedMaterial.color = Color.green;
    }

    void OnDisable() {
        myRenderer.sharedMaterial.color = Color.red;
    }
}
```

This works fine most of the time, but if the scene is unloaded, Unity might destroy `myRenderer` **before** `OnDisable()` runs. When that happens, the line accessing `myRenderer.sharedMaterial` throws an exception.

This failure is intermittent, depending entirely on the destruction order Unity chooses.

---

## The Same Problem in Spoke

Without `UnitySignals`, Spoke would run into the same issue:

```csharp
public class MyBehaviour : SpokeBehaviour {

    [SerializeField] Renderer myRenderer;

    protected override void Init(EffectBuilder s) {

        s.Phase(IsEnabled, s => {
            myRenderer.sharedMaterial.color = Color.green;

            s.OnCleanup(() => {
                myRenderer.sharedMaterial.color = Color.red;
            });
        });
    }
}
```

Again, when the scene unloads, `OnCleanup` runs, but `myRenderer` may already be gone. Whether this throws an exception depends on timing and destruction order.

---

## The Fix: `UnitySignals`

`UnitySignals` solves this by letting you **trigger teardown logic before Unity begins destroying objects.**

Here's what it exposes:

```csharp
public static class UnitySignals {

    public static ISignal<bool> IsPlaying { get; }
    public static ITrigger AppTeardown { get; }
    public static ITrigger<Scene> SceneTeardown { get; }

    public static void NotifySceneTeardown(Scene scene) { /* ... */ }
}
```

`SpokeBehaviour` already subscribes to these signals:

- When `UnitySignals.AppTeardown` fires, all `SpokeBehaviour` instances clean themselves up.
- When `UnitySignals.SceneTeardown` fires, only behaviours from the given scene clean up.

Teardown behaves exactly like natural destruction:

- `IsEnabled` and `IsAwake` are set to false
- The `Init` scope is unmounted

But it happens **before Unity destroys anything**, so you can safely access any fields or scene objects.

---

### Scene Teardown Caveat

`UnitySignals.AppTeardown` is wired up automatically and fires at the right time.

However, Unity provides **no reliable hook** for early scene teardown. `SceneManager.sceneUnloaded` fires **too late**; by then, objects may already be destroyed.

So if you want to trigger `UnitySignals.SceneTeardown`, you must **trigger it manually** before unloading:

```csharp
public void ChangeScene(string nextScene) {

    var currScene = SceneManager.GetActiveScene();
    // Notify Spoke that the currScene is being unloaded
    UnitySignals.NotifySceneTeardown(currScene);

    SceneManager.LoadScene(nextScene);
}
```

It's a small cost, and in return you never have to deal with scene teardown bugs again.
