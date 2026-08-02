using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

// I included an object pool in this game for realism, and because object pooling introduces a
// lifecycle-management problem (which is Spoke's strength).
// Pools are a classic source of lifecycle bugs: a reused object can carry state left over from its
// previous life if something wasn't reset on despawn.
// This is exactly the surface area for bugs that Spoke can eliminate.
//
// Unity disables an instance to park it. Godot's equivalent is taking it out of the tree, which is
// why every component here mounts its work under s.Phase(IsInTree, ...).

/// <summary>A minimal object pool. Despawn unparents an instance and stashes it; Spawn re-parents an idle one, or instantiates a new one.</summary>
public static class Pool {

    static readonly Dictionary<PackedScene, Stack<Node2D>> idle = new();
    static readonly Dictionary<Node2D, PackedScene> origin = new();

    /// <summary>Returns an active instance of prefab, reused from its idle pool if one's free, otherwise freshly instantiated.</summary>
    public static Node2D Spawn(PackedScene prefab, Vector2 pos) {
        Node2D instance;
        if (idle.TryGetValue(prefab, out var stack) && stack.Count > 0) {
            instance = stack.Pop();
        } else {
            instance = prefab.Instantiate<Node2D>();
            origin[instance] = prefab;
        }
        instance.Position = pos;
        GameState.Board.AddChild(instance);
        return instance;
    }

    /// <summary>Unparents an instance and returns it to its prefab's idle pool for reuse. Frees it instead if it never came from the pool.</summary>
    public static void Despawn(Node2D instance) {
        if (!GodotObject.IsInstanceValid(instance)) return;
        if (!origin.TryGetValue(instance, out var prefab)) {
            instance.QueueFree();
            return;
        }
        // Two callers can reasonably decide the same unit is finished: the unit itself once its
        // shatter completes, and the block that spawned it when that block ends.
        var parent = instance.GetParent();
        if (parent == null) return;
        parent.RemoveChild(instance);
        if (!idle.TryGetValue(prefab, out var stack)) idle[prefab] = stack = new();
        stack.Push(instance);
    }

    /// <summary>Frees every stashed instance. Idle instances have no parent, so nothing else will free them.</summary>
    public static void Clear() {
        foreach (var stack in idle.Values) {
            foreach (var node in stack) {
                if (GodotObject.IsInstanceValid(node)) node.Free();
            }
        }
        idle.Clear();
        origin.Clear();
    }
}
