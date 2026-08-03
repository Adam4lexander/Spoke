using System;
using System.Collections.Generic;
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
    ///   doesn't belong on every node's surface. The one node that needs it can drive a State from
    ///   NOTIFICATION_PAUSED and NOTIFICATION_UNPAUSED in its own OnNotification.
    ///
    /// - s.OnProcess and s.OnPhysicsProcess are dispatched from here, so they land at the node's own
    ///   point in the frame and get tree order, ProcessPriority and pause for free. They ride the
    ///   internal process notifications, leaving the node's own SetProcess and _Process untouched.
    ///   Within the node they run in Spoke tree order, so a phase that remounts doesn't shuffle
    ///   itself to the back.
    /// </summary>
    internal sealed class SpokeNodeCore {

        readonly Node node;
        readonly Node notifier;
        readonly EffectBlock init;
        readonly Func<bool> visibilityProbe;

        readonly State<bool> isInTree = State.Create(false);
        readonly State<bool> isReady = State.Create(false);
        readonly State<bool> isVisible = State.Create(false);

        readonly Jobs process = new();
        readonly Jobs physics = new();
        readonly Action<long> dropProcess, dropPhysics;
        long nextJobId = 1; // 0 is reserved: it's what a blanked job reads as

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
        /// <param name="notifier">
        /// The node forwarding notifications here, when it isn't the host itself. SpokeHost is a
        /// child of the node it describes.
        /// </param>
        public SpokeNodeCore(Node node, EffectBlock init, Func<bool> visibilityProbe = null, Node notifier = null) {
            this.node = node ?? throw new ArgumentNullException(nameof(node));
            this.init = init ?? throw new ArgumentNullException(nameof(init));
            this.visibilityProbe = visibilityProbe;
            this.notifier = notifier ?? node;
            dropProcess = id => Drop(process, id, isPhysics: false);
            dropPhysics = id => Drop(physics, id, isPhysics: true);
        }

        /// <summary>Takes on a per-frame callback. Dispose the handle to give it up again.</summary>
        internal SpokeHandle Register(FrameTick tick, Action<double> fn, bool isPhysics) {
            var jobs = isPhysics ? physics : process;
            // Processing follows the job count, so an idle node costs nothing per frame.
            if (jobs.Count == 0) SetProcessing(true, isPhysics);
            var job = new Job(nextJobId++, tick, fn);
            jobs.Add(job);
            return SpokeHandle.Of(job.Id, isPhysics ? dropPhysics : dropProcess);
        }

        void Drop(Jobs jobs, long id, bool isPhysics) {
            if (jobs.Remove(id) && jobs.Count == 0) SetProcessing(false, isPhysics);
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

                case Node.NotificationInternalProcess:
                    process.Dispatch(notifier.GetProcessDeltaTime());
                    break;

                case Node.NotificationInternalPhysicsProcess:
                    physics.Dispatch(notifier.GetPhysicsProcessDeltaTime());
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
                new GodotContext(node, this));
        }

        void RefreshVisibility() {
            if (visibilityProbe == null) return;
            isVisible.Set(node.IsInsideTree() && visibilityProbe());
        }

        void SetProcessing(bool on, bool isPhysics) {
            if (!GodotObject.IsInstanceValid(notifier)) return;
            if (isPhysics) notifier.SetPhysicsProcessInternal(on);
            else notifier.SetProcessInternal(on);
        }

        // A registered callback. Its FrameTick is where it sits in the Spoke tree, and sorts it.
        readonly struct Job {

            public readonly long Id;
            public readonly FrameTick Tick;
            public readonly Action<double> Fn;

            public Job(long id, FrameTick tick, Action<double> fn) {
                Id = id;
                Tick = tick;
                Fn = fn;
            }

            public void Run(double delta) => Fn?.Invoke(delta);
        }

        // One of the node's two callback lists, kept in Spoke tree order. Only adding can disturb
        // that order, so the sort waits for the next dispatch rather than running every frame.
        class Jobs {

            static readonly Comparison<Job> byTreeOrder = (a, b) => a.Tick.CompareTo(b.Tick);

            readonly List<Job> jobs = new();
            readonly List<Job> running = new(); // the snapshot a dispatch is walking; empty otherwise
            bool isSorted = true;

            public int Count => jobs.Count;

            public void Add(in Job job) {
                jobs.Add(job);
                isSorted = false;
            }

            public bool Remove(long id) {
                var found = false;
                for (var i = 0; i < jobs.Count; i++) {
                    if (jobs[i].Id != id) continue;
                    jobs.RemoveAt(i);
                    found = true;
                    break;
                }
                // A job dropped mid-dispatch doesn't run again this frame.
                for (var i = 0; i < running.Count; i++) {
                    if (running[i].Id == id) running[i] = default;
                }
                return found;
            }

            public void Dispatch(double delta) {
                if (jobs.Count == 0) return;
                if (!isSorted) {
                    jobs.Sort(byTreeOrder);
                    isSorted = true;
                }
                // Walked over a snapshot, because a callback can unmount the block holding the next.
                running.AddRange(jobs);
                try {
                    foreach (var job in running) job.Run(delta);
                } finally {
                    running.Clear();
                }
            }
        }
    }
}
