using System;
using Godot;

namespace Spoke {

    /// <summary>
    /// Implemented by every Spoke node base class. Lets shared code accept "a node running a
    /// SpokeTree" without caring whether it's a Node, Node2D, Node3D or Control.
    /// </summary>
    public interface ISpokeNode {

        /// <summary>The node itself.</summary>
        Node HostNode { get; }

        /// <summary>True while the node is inside the SceneTree. Cycles on reparent.</summary>
        ISignal<bool> IsInTree { get; }

        /// <summary>True while SceneTree pause is stopping this node from processing.</summary>
        ISignal<bool> IsPaused { get; }
    }

    /// <summary>
    /// The engine-facing half of a Spoke node. Owns the SpokeTree and translates Godot notifications
    /// into reactive signals. The node base classes are thin shims over this — the logic lives here
    /// once, because C# has no multiple inheritance and Godot's node types are separate hierarchies.
    ///
    /// Lifecycle decisions, and why:
    ///
    /// - The tree spawns on NOTIFICATION_READY, not ENTER_TREE. _Ready is Godot's setup callback:
    ///   children exist and have run their own _Ready, and the node is no longer 'blocked', so Init
    ///   can call AddChild. Spawning at ENTER_TREE would forbid both. This is also why there's no
    ///   IsReady signal to gate on — Unity needs Awake-vs-Start because Awake can't see other
    ///   objects' initialization; Godot's bottom-up _Ready already solves that.
    ///
    /// - The tree is disposed on PREDELETE, not EXIT_TREE, so it survives reparenting. A node that
    ///   is removed from the tree and re-added keeps its state; IsInTree just goes false and back.
    ///   Gate anything that needs a live tree with s.Phase(IsInTree, ...).
    /// </summary>
    public sealed class SpokeNodeCore {

        readonly Node node;
        readonly EffectBlock init;
        readonly Func<bool> visibilityProbe;

        readonly State<bool> isInTree = State.Create(false);
        readonly State<bool> isPaused = State.Create(false);
        readonly State<bool> isVisible = State.Create(false);

        SpokeTree<Effect> tree;
        bool isTornDown;

        /// <summary>True while the node is inside the SceneTree. Cycles on reparent.</summary>
        public ISignal<bool> IsInTree => isInTree;

        /// <summary>True while SceneTree pause is stopping this node from processing.</summary>
        public ISignal<bool> IsPaused => isPaused;

        /// <summary>
        /// True while the node is visible in the tree. Only meaningful for hosts that supplied a
        /// visibility probe — CanvasItem and Node3D descendants. Stays false for plain Nodes.
        /// </summary>
        public ISignal<bool> IsShown => isVisible;

        /// <summary>The tree, once spawned. Null before _Ready and after teardown.</summary>
        public SpokeTree<Effect> Tree => tree;

        /// <param name="node">The host node.</param>
        /// <param name="init">The block that becomes the root Effect of the tree.</param>
        /// <param name="visibilityProbe">
        /// Supplied by visual node shims. Returns the node's current visible-in-tree state.
        /// </param>
        public SpokeNodeCore(Node node, EffectBlock init, Func<bool> visibilityProbe = null) {
            this.node = node ?? throw new ArgumentNullException(nameof(node));
            this.init = init ?? throw new ArgumentNullException(nameof(init));
            this.visibilityProbe = visibilityProbe;
        }

        /// <summary>
        /// Forwarded from the host's _Notification. Handles every lifecycle event Spoke cares about
        /// through the one callback, which leaves _Ready, _Process, _Input and friends free for the
        /// user — overriding those in a base class would silently break on a missing base call.
        /// </summary>
        public void Notification(int what) {
            switch ((long)what) {

                case Node.NotificationEnterTree:
                    isInTree.Set(true);
                    RefreshVisibility();
                    break;

                case Node.NotificationReady:
                    isPaused.Set(!node.CanProcess());
                    RefreshVisibility();
                    Spawn();
                    break;

                case Node.NotificationExitTree:
                    isInTree.Set(false);
                    RefreshVisibility();
                    // Removal alone is not death — the node may be on its way to a new parent.
                    // But a queue_free'd node exits the tree first and is deleted later, and by then
                    // it is too late to run cleanup that touches the tree.
                    if (node.IsQueuedForDeletion()) Teardown();
                    break;

                case Node.NotificationPaused:
                    isPaused.Set(true);
                    break;

                case Node.NotificationUnpaused:
                    isPaused.Set(false);
                    break;

                case CanvasItem.NotificationVisibilityChanged:
                case Node3D.NotificationVisibilityChanged:
                    RefreshVisibility();
                    break;

                case GodotObject.NotificationPredelete:
                    Teardown();
                    break;
            }
        }

        /// <summary>
        /// Tears the tree down early. Idempotent, and safe to call from user code — the node keeps
        /// working as a plain Godot node afterwards, it just stops running its Spoke logic.
        /// </summary>
        public void Teardown() {
            if (isTornDown) return;
            isTornDown = true;
            isInTree.Set(false);
            isVisible.Set(false);
            tree?.Dispose();
            tree = null;
        }

        void Spawn() {
            if (tree != null || isTornDown) return;
            tree = SpokeTree.Spawn(
                $"{node.GetType().Name}:{node.Name}",
                new Effect("Init", init),
                new GodotContext(node));
        }

        void RefreshVisibility() {
            if (visibilityProbe == null) return;
            isVisible.Set(node.IsInsideTree() && visibilityProbe());
        }
    }
}
