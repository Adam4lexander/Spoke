using System;
using Godot;

namespace Spoke.Examples.BaseDefence;

// Extend Spoke by adding extension methods to EffectBuilder: they become new s.Xxx(...) calls
// usable inside any Init, exactly like Spoke's built-in ones. Godot C# has no coroutines, so
// s.Wait and s.Every stand in for the Unity version's s.Coroutine, and s.Own scopes a node to
// the block that made it.
public static class SpokeExtensions {

    /// <summary>Adds a child, and frees it when the surrounding effect unmounts.</summary>
    public static T Own<T>(this EffectBuilder s, Node parent, T node) where T : Node {
        parent.AddChild(node);
        s.OnCleanup(() => {
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        });
        return node;
    }

    /// <summary>
    /// Runs onElapsed once after a delay, if the surrounding effect is still mounted by then.
    /// Unmount before it fires and it never fires -- no cancellation to remember.
    /// </summary>
    public static void Wait(this EffectBuilder s, double seconds, Action onElapsed) {
        var elapsed = 0.0;
        var fired = false;
        s.OnProcess(delta => {
            if (fired) return;
            elapsed += delta;
            if (elapsed < seconds) return;
            fired = true;
            onElapsed();
        });
    }

    /// <summary>Runs onTick on a fixed interval while the surrounding effect is mounted.</summary>
    public static void Every(this EffectBuilder s, double seconds, Action onTick) {
        if (seconds <= 0.0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var elapsed = 0.0;
        s.OnProcess(delta => {
            elapsed += delta;
            // Catch up if a long frame swallowed several intervals, but never spiral: cap the
            // number of ticks a single frame can produce.
            for (var i = 0; elapsed >= seconds && i < 4; i++) {
                elapsed -= seconds;
                onTick();
            }
        });
    }
}
