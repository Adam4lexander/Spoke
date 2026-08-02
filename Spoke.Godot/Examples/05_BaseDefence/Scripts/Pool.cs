using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// A buildable, wrapping the scene it comes from.
///
/// Everything the sidebar needs to describe a building — its name, its price, the coverage it
/// shows — is exported on the building itself, so the scene is the only place those are written
/// down. Reading them means building one and throwing it away, which happens once per kind.
/// </summary>
public sealed class UnitSpec {

    readonly PackedScene scene;

    bool probed;
    string displayName = "";
    int cost;
    CoverageType coverage;
    float radiusPx;

    public UnitSpec(PackedScene scene) => this.scene = scene;

    public Node2D Create() => scene.Instantiate<Node2D>();

    public string DisplayName { get { Probe(); return displayName; } }
    public int Cost { get { Probe(); return cost; } }
    public CoverageType Coverage { get { Probe(); return coverage; } }

    /// <summary>The footprint, known before anything is built, so placement can be previewed.</summary>
    public float RadiusPx { get { Probe(); return radiusPx; } }

    // Only ever called for buildables. The probe never enters the tree, so it never spawns a Spoke
    // tree, and freeing it is a plain destructor.
    void Probe() {
        if (probed) return;
        probed = true;
        var probe = scene.Instantiate<Building>();
        displayName = probe.DisplayName;
        cost = probe.Cost;
        coverage = probe.Coverage;
        radiusPx = probe.RadiusPx;
        probe.Free();
    }
}

/// <summary>
/// A minimal node pool. Despawn pulls an instance out of the tree and stashes it; Spawn re-adds an
/// idle one, or instantiates a fresh one.
///
/// Pooling is here for the same reason it's in the Unity version: it is a classic source of
/// lifecycle bugs. A reused instance carries whatever its last life left behind, and every "reset
/// this on despawn" is a line someone has to remember to write.
///
/// Spoke removes that class of bug, and in Godot it does it through the scene tree itself. Despawn
/// calls RemoveChild, which drops IsInTree to false; Spawn calls AddChild, which raises it again.
/// Anything a unit registered under s.Phase(IsInTree, ...) — its colliders, its entry in
/// Building.All, its accumulated damage — unmounts on despawn and mounts fresh on respawn, because
/// that's what a phase does. Nothing needs an explicit reset path.
///
/// This works because Spoke.Godot disposes a node's tree at PREDELETE rather than at _ExitTree.
/// A pooled unit leaves the tree without being destroyed, so its tree survives the trip intact.
/// </summary>
public static class Pool {

    static readonly Dictionary<UnitSpec, Stack<Node2D>> idle = new();
    static readonly Dictionary<Node2D, UnitSpec> origin = new();

    /// <summary>Returns an active instance of spec, reused if one's free, otherwise freshly built.</summary>
    public static Node2D Spawn(UnitSpec spec, Vector2 position) {
        Node2D instance;
        if (idle.TryGetValue(spec, out var stack) && stack.Count > 0) {
            instance = stack.Pop();
        } else {
            instance = spec.Create();
            origin[instance] = spec;
        }
        instance.Position = position;
        GameState.Board.AddChild(instance);
        return instance;
    }

    /// <summary>Pulls an instance out of the tree and stashes it for reuse. Frees it if it isn't ours.</summary>
    public static void Despawn(Node2D instance) {
        if (!GodotObject.IsInstanceValid(instance)) return;
        if (!origin.TryGetValue(instance, out var spec)) {
            instance.QueueFree();
            return;
        }
        // Already idle. Two callers can reasonably decide the same unit is finished — the unit
        // itself once its shatter completes, and the block that spawned it when that block ends.
        var parent = instance.GetParent();
        if (parent == null) return;
        parent.RemoveChild(instance);
        if (!idle.TryGetValue(spec, out var stack)) idle[spec] = stack = new();
        stack.Push(instance);
    }

    /// <summary>
    /// Frees every stashed instance. Idle instances have no parent, so nothing else will ever free
    /// them — without this, quitting the game reports leaked ObjectDB instances.
    /// </summary>
    public static void Clear() {
        foreach (var stack in idle.Values) {
            foreach (var node in stack) {
                // Free, not QueueFree: these are detached and idle by definition, so there's no
                // in-flight use to wait out, and a queued free may never run if we're shutting down.
                if (GodotObject.IsInstanceValid(node)) node.Free();
            }
        }
        idle.Clear();
        origin.Clear();
    }
}
