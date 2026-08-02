using System;
using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// The game's own EffectBuilder extensions. Adding methods here makes them available as s.Xxx(...)
/// inside any Init, exactly like Spoke's built-in ones.
///
/// Spoke.Godot deliberately ships no node-lifetime helper, because scoping a node to a block means
/// choosing what cleanup does, and that's a per-game call. This game makes that call twice, and
/// they're different: things a block owns outright are freed (s.Own, below), and units go back to
/// the Pool instead. Only the first is common enough to be worth an extension.
///
/// s.Wait and s.Every replace the coroutines the Unity version uses; Godot C# has no coroutines,
/// and both are a few lines over s.OnProcess. They inherit its behaviour for free: they stop while
/// the host can't process, so the whole game freezes on GetTree().Paused without a single check.
/// </summary>
public static class SpokeExtensions {

    /// <summary>
    /// Adds a child, and frees it when the block ends. Every transient thing on screen is one of
    /// these — a turret's beam, a coverage overlay, a radar-tracked marker — and none of them can
    /// outlive the reason they exist, because there's nowhere else to write the teardown.
    /// </summary>
    public static T Own<T>(this EffectBuilder s, Node parent, T node) where T : Node {
        parent.AddChild(node);
        s.OnCleanup(() => {
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        });
        return node;
    }

    /// <summary>
    /// Runs onElapsed once, after a delay, if the block is still mounted by then. Unmount before it
    /// fires and it never fires — which is the whole point: a countdown that outlives its reason to
    /// exist is the bug this replaces.
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

    /// <summary>Runs onTick on a fixed interval while the block is mounted.</summary>
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
