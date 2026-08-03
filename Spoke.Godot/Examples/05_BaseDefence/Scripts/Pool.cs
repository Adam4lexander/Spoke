using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

// I included an object pool in this game for realism, and because object pooling introduces a
// lifecycle-management problem (which is Spoke's strength).
// Pools are a classic source of lifecycle bugs: a reused object can carry state left over from its
// previous life if something wasn't reset on despawn.
// This is exactly the surface area for bugs that Spoke can eliminate.
//
// Unity disables an instance to park it; Godot's equivalent is PROCESS_MODE_DISABLED, which is why
// every component here resets per-life state under s.Phase(IsEnabled, ...).

/// <summary>A minimal object pool. Despawn disables an instance in place and stashes it; Spawn re-enables an idle one, or instantiates a new one.</summary>
public static class Pool {

    static readonly Dictionary<PackedScene, Stack<Node2D>> idle = new();
    static readonly Dictionary<Node2D, PackedScene> origin = new();

    /// <summary>Returns an active instance of prefab, reused from its idle pool if one's free, otherwise freshly instantiated.</summary>
    public static Node2D Spawn(PackedScene prefab, Vector2 pos) {
        if (idle.TryGetValue(prefab, out var stack) && stack.Count > 0) {
            var instance = stack.Pop();
            // Position first: re-enabling remounts phases that read GlobalPosition.
            instance.Position = pos;
            instance.Show();
            instance.ProcessMode = Node.ProcessModeEnum.Inherit;
            return instance;
        }
        var fresh = prefab.Instantiate<Node2D>();
        origin[fresh] = prefab;
        fresh.Position = pos;
        GameState.Board.AddChild(fresh);
        return fresh;
    }

    /// <summary>Parks an instance — disabled and hidden, still in the tree — for reuse. Frees it instead if it never came from the pool.</summary>
    public static void Despawn(Node2D instance) {
        if (!GodotObject.IsInstanceValid(instance)) return;
        if (!origin.TryGetValue(instance, out var prefab)) {
            instance.QueueFree();
            return;
        }
        // Two callers can reasonably decide the same unit is finished: the unit itself once its
        // shatter completes, and the block that spawned it when that block ends.
        if (instance.ProcessMode == Node.ProcessModeEnum.Disabled) return;
        instance.ProcessMode = Node.ProcessModeEnum.Disabled;
        instance.Hide();
        if (!idle.TryGetValue(prefab, out var stack)) idle[prefab] = stack = new();
        stack.Push(instance);
    }

    /// <summary>Forgets every instance. Parked units sit in the tree, so the scene frees them along with everything else.</summary>
    public static void Clear() {
        idle.Clear();
        origin.Clear();
    }
}
