using Godot;

namespace Spoke {

    /// <summary>
    /// Runs a SpokeTree for a node you can't rebase onto SpokeNode — a CharacterBody2D you already
    /// wrote, a third-party node, or one node that needs several independent trees.
    ///
    /// SpokeHost is a child node, so Godot's own ownership rules do the work: it enters and leaves
    /// the tree with its target, inherits its pause state, and is freed when the target is freed,
    /// which tears the tree down. Signals reported here describe the target, not the host.
    ///
    /// <code>
    /// public partial class Player : CharacterBody2D {
    ///     public override void _Ready() {
    ///         SpokeHost.Attach(this, s => {
    ///             s.OnProcess(delta => ...);
    ///         });
    ///     }
    /// }
    /// </code>
    /// </summary>
    public sealed partial class SpokeHost : Node {

        SpokeNodeCore core;

        /// <summary>
        /// Attaches a tree to <paramref name="target"/>. Call from _Ready — Godot forbids AddChild
        /// while a node is entering the tree.
        /// </summary>
        public static SpokeHost Attach(Node target, EffectBlock init, string name = "SpokeHost") {
            var host = new SpokeHost { Name = name };
            host.core = new SpokeNodeCore(target, init);
            target.AddChild(host);
            return host;
        }

        /// <summary>True while the target is inside the SceneTree.</summary>
        public ISignal<bool> IsInTree => core.IsInTree;

        /// <summary>True while SceneTree pause is stopping the target from processing.</summary>
        public ISignal<bool> IsPaused => core.IsPaused;

        public sealed override void _Notification(int what) {
            base._Notification(what);
            core?.Notification(what);
        }

        /// <summary>Tears the tree down early. Idempotent.</summary>
        public void Teardown()
            => core?.Teardown();
    }
}
