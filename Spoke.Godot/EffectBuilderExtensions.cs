using System;
using Godot;

namespace Spoke {

    /// <summary>
    /// EffectBuilder extensions specific to Godot.
    ///
    /// Deliberately small: a signal connection and a per-frame callback, each scoped to the block.
    /// Neither has a clean equivalent in plain code, and neither presumes a policy.
    ///
    /// Notably absent is a node-lifetime helper. Adding one means picking what happens at cleanup —
    /// QueueFree, return-to-pool, hide-and-reuse — and that's a per-game decision, not something a
    /// library should decide on your behalf. Inside a Spoke node `this` is the node, so AddChild,
    /// GetNode and QueueFree work as they always do, with s.OnCleanup as the teardown half. See
    /// README.md, "Node lifetimes", for the extension to write if you want one.
    /// </summary>
    public static partial class EffectBuilderExtensions {

        // ---------------------------------------------------------------- signals

        /// <summary>Connects a Godot signal, automatically disconnects.</summary>
        public static void Subscribe(this EffectBuilder s, GodotObject obj, StringName signal, Action fn)
            => s.Subscribe(obj, signal, Callable.From(fn));

        /// <summary>Connects a single-argument Godot signal, automatically disconnects.</summary>
        public static void Subscribe<[MustBeVariant] T0>(this EffectBuilder s, GodotObject obj, StringName signal, Action<T0> fn)
            => s.Subscribe(obj, signal, Callable.From(fn));

        /// <summary>
        /// Connects a Godot signal of any arity, automatically disconnects. Signal arguments cross
        /// the engine boundary as Variants, so there's one overload per argument count — rather than
        /// climb that ladder, anything past a single argument builds its own Callable:
        ///
        ///     s.Subscribe(area, Area2D.SignalName.AreaShapeEntered,
        ///                 Callable.From&lt;Rid, Area2D, long, long&gt;(OnHit));
        /// </summary>
        public static void Subscribe(this EffectBuilder s, GodotObject obj, StringName signal, Callable callable) {
            obj.Connect(signal, callable);
            s.OnCleanup(() => {
                // The emitter may already be gone — freeing an object drops its connections anyway.
                if (GodotObject.IsInstanceValid(obj) && obj.IsConnected(signal, callable)) {
                    obj.Disconnect(signal, callable);
                }
            });
        }

        // ---------------------------------------------------------------- per-frame work

        /// <summary>
        /// Runs a callback every rendered frame while the block is alive — _Process, scoped to the
        /// block instead of the node. Skipped while the host is out of the tree or paused, matching
        /// _Process. To run through a pause, set the host's ProcessMode to Always, exactly as you
        /// would for a hand-written _Process.
        /// </summary>
        public static void OnProcess(this EffectBuilder s, Action<double> fn)
            => OnFrame(s, fn, physics: false);

        /// <summary>
        /// Runs a callback every physics tick while the block is alive — _PhysicsProcess, scoped to
        /// the block. Use for anything touching physics state: MoveAndSlide, forces, shape queries.
        /// </summary>
        public static void OnPhysicsProcess(this EffectBuilder s, Action<double> fn)
            => OnFrame(s, fn, physics: true);

        static void OnFrame(EffectBuilder s, Action<double> fn, bool physics) {
            var node = s.Import<GodotContext>().Node;
            // Reached through the MainLoop rather than node.GetTree(), so this still resolves while
            // the host is detached — the handler itself no-ops until the host is back in the tree.
            if (Engine.GetMainLoop() is not SceneTree tree) {
                throw new InvalidOperationException("OnProcess requires a SceneTree MainLoop");
            }
            Action handler = () => {
                if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) return;
                if (!node.CanProcess()) return;
                fn(physics ? node.GetPhysicsProcessDeltaTime() : node.GetProcessDeltaTime());
            };
            s.Subscribe(tree,
                physics ? SceneTree.SignalName.PhysicsFrame : SceneTree.SignalName.ProcessFrame,
                Callable.From(handler));
        }
    }
}
