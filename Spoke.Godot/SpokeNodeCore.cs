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

        /// <summary>True once the node and its children are set up. See <see cref="SpokeNodeCore"/>.</summary>
        ISignal<bool> IsReady { get; }
    }

    /// <summary>
    /// The engine-facing half of a Spoke node. Owns the SpokeTree and translates Godot notifications
    /// into reactive signals. The node base classes are thin shims over this — the logic lives here
    /// once, because C# has no multiple inheritance and Godot's node types are separate hierarchies.
    ///
    /// Lifecycle decisions, and why:
    ///
    /// - The tree spawns on NOTIFICATION_ENTER_TREE, which propagates top-down: a node's Init runs
    ///   before any of its descendants'. That is the direction dependencies actually run — a hub
    ///   node publishes what the nodes beneath it read — and spawning at READY instead would invert
    ///   it, leaving every such node to publish from a hand-written _EnterTree override.
    ///
    /// - IsReady covers the other direction. It goes true at NOTIFICATION_READY, which propagates
    ///   bottom-up, so s.Phase(IsReady, ...) is the place for anything that has to see its own
    ///   children set up first. Once true it stays true, matching Node.IsNodeReady().
    ///
    ///   Between them, both orderings are available as signals, which is the point: callback order
    ///   is the thing Spoke exists to stop you depending on.
    ///
    /// - The tree is disposed on PREDELETE, not EXIT_TREE, so it survives reparenting. A node that
    ///   is removed from the tree and re-added keeps its state; IsInTree just goes false and back.
    ///   Gate anything that needs a live tree with s.Phase(IsInTree, ...).
    ///
    /// - Pause is deliberately not a signal. s.OnProcess already stops while a node can't process,
    ///   which is the part that matters, and pause-bracketed setup/teardown is rare enough that it
    ///   doesn't belong on every node's surface. The recipe for adding it to one node that needs it
    ///   is in README.md, under "Reacting to pause".
    /// </summary>
    public sealed class SpokeNodeCore {

        readonly Node node;
        readonly EffectBlock init;
        readonly Func<bool> visibilityProbe;

        readonly State<bool> isInTree = State.Create(false);
        readonly State<bool> isReady = State.Create(false);
        readonly State<bool> isVisible = State.Create(false);

        SpokeTree<Effect> tree;
        bool isTornDown;

        /// <summary>True while the node is inside the SceneTree. Cycles on reparent.</summary>
        public ISignal<bool> IsInTree => isInTree;

        /// <summary>
        /// True once _Ready has fired — the node's children exist and have run their own Init.
        /// False for the duration of Init itself, because Init runs earlier, on entering the tree.
        ///
        /// Ready is a milestone, not a state: Godot fires _Ready once, and leaving the tree neither
        /// clears it nor causes it to fire again on re-entry. This mirrors that exactly, and mirrors
        /// Node.IsNodeReady(). It's what lets a pooled node come back — IsInTree cycles, so its work
        /// unmounts and remounts, while IsReady holds and never has to happen twice.
        /// </summary>
        public ISignal<bool> IsReady => isReady;

        /// <summary>
        /// True while the node is visible in the tree. Only meaningful for hosts that supplied a
        /// visibility probe — CanvasItem and Node3D descendants. Stays false for plain Nodes.
        /// </summary>
        public ISignal<bool> IsShown => isVisible;

        /// <summary>The tree, once spawned. Null before it enters the tree, and after teardown.</summary>
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
                    Spawn();
                    break;

                case Node.NotificationReady:
                    RefreshVisibility();
                    isReady.Set(true);
                    break;

                case Node.NotificationExitTree:
                    isInTree.Set(false);
                    RefreshVisibility();
                    // Removal alone is not death — the node may be on its way to a new parent.
                    // But a queue_free'd node exits the tree first and is deleted later, and by then
                    // it is too late to run cleanup that touches the tree.
                    if (node.IsQueuedForDeletion()) Teardown();
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
        ///
        /// Disposing the tree is the whole of it. The signals are left alone deliberately: they
        /// report what Godot says about the node, and tearing a tree down doesn't take a node out
        /// of the scene or un-ready it. Forcing them false to make phases unmount would be stating
        /// something untrue to achieve something Dispose already does.
        /// </summary>
        public void Teardown() {
            if (isTornDown) return;
            isTornDown = true;
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
