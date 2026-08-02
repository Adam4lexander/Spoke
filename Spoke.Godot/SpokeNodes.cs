using Godot;

namespace Spoke {

    // Unity gets away with a single SpokeBehaviour because MonoBehaviour is attached by composition.
    // Godot attaches scripts by inheritance, and Node2D / Node3D / Control are separate hierarchies,
    // so there is no one base class that works everywhere. These four shims are the price. Each is
    // the same dozen lines over SpokeNodeCore, which holds the actual logic.
    //
    // If you can't change a base class — a CharacterBody2D you already wrote, or a third-party node —
    // use SpokeHost instead, and give it the node it should drive.

    /// <summary>
    /// Extend instead of Node. Override Init to declare your logic; it runs once, as the node enters
    /// the tree, as the root Effect of a SpokeTree scoped to this node's lifetime.
    /// </summary>
    public abstract partial class SpokeNode : Node, ISpokeNode {

        readonly SpokeNodeCore core;

        protected SpokeNode()
            => core = new SpokeNodeCore(this, Init);

        public Node HostNode => this;

        /// <summary>True while the node is inside the SceneTree. Cycles on reparent.</summary>
        public ISignal<bool> IsInTree => core.IsInTree;

        /// <summary>
        /// True once _Ready has fired — this node and its children are set up. False during Init,
        /// which runs earlier, on entering the tree. Gate anything that has to see its own children
        /// initialised with s.Phase(IsReady, ...).
        /// </summary>
        public ISignal<bool> IsReady => core.IsReady;

        /// <summary>Declare your effects here. Replaces _EnterTree, and the teardown half of _ExitTree.</summary>
        protected abstract void Init(EffectBuilder s);

        /// <summary>
        /// Sealed so a missing base call can't silently orphan the tree. Override OnNotification
        /// instead — _Ready, _Process, _Input and every other virtual are untouched and yours.
        /// </summary>
        public sealed override void _Notification(int what) {
            base._Notification(what);
            core.Notification(what);
            OnNotification(what);
        }

        /// <summary>Godot notifications, after Spoke has handled its own.</summary>
        protected virtual void OnNotification(int what) { }

        /// <summary>Tears down the Spoke tree early. Idempotent.</summary>
        protected void TeardownSpoke()
            => core.Teardown();
    }

    /// <summary>Extend instead of Node2D. See <see cref="SpokeNode"/>.</summary>
    public abstract partial class SpokeNode2D : Node2D, ISpokeNode {

        readonly SpokeNodeCore core;

        protected SpokeNode2D()
            => core = new SpokeNodeCore(this, Init, IsVisibleInTree);

        public Node HostNode => this;

        /// <summary>True while the node is inside the SceneTree. Cycles on reparent.</summary>
        public ISignal<bool> IsInTree => core.IsInTree;

        /// <summary>
        /// True once _Ready has fired — this node and its children are set up. False during Init,
        /// which runs earlier, on entering the tree. Gate anything that has to see its own children
        /// initialised with s.Phase(IsReady, ...).
        /// </summary>
        public ISignal<bool> IsReady => core.IsReady;

        /// <summary>
        /// True while the node is visible in the tree — self and every ancestor. Named IsShown
        /// because IsVisible is already a method on the Godot side.
        /// </summary>
        public ISignal<bool> IsShown => core.IsShown;

        protected abstract void Init(EffectBuilder s);

        public sealed override void _Notification(int what) {
            base._Notification(what);
            core.Notification(what);
            OnNotification(what);
        }

        protected virtual void OnNotification(int what) { }

        protected void TeardownSpoke()
            => core.Teardown();
    }

    /// <summary>Extend instead of Node3D. See <see cref="SpokeNode"/>.</summary>
    public abstract partial class SpokeNode3D : Node3D, ISpokeNode {

        readonly SpokeNodeCore core;

        protected SpokeNode3D()
            => core = new SpokeNodeCore(this, Init, IsVisibleInTree);

        public Node HostNode => this;

        /// <summary>True while the node is inside the SceneTree. Cycles on reparent.</summary>
        public ISignal<bool> IsInTree => core.IsInTree;

        /// <summary>
        /// True once _Ready has fired — this node and its children are set up. False during Init,
        /// which runs earlier, on entering the tree. Gate anything that has to see its own children
        /// initialised with s.Phase(IsReady, ...).
        /// </summary>
        public ISignal<bool> IsReady => core.IsReady;

        /// <summary>
        /// True while the node is visible in the tree — self and every ancestor. Named IsShown
        /// because IsVisible is already a method on the Godot side.
        /// </summary>
        public ISignal<bool> IsShown => core.IsShown;

        protected abstract void Init(EffectBuilder s);

        public sealed override void _Notification(int what) {
            base._Notification(what);
            core.Notification(what);
            OnNotification(what);
        }

        protected virtual void OnNotification(int what) { }

        protected void TeardownSpoke()
            => core.Teardown();
    }

    /// <summary>Extend instead of Control. See <see cref="SpokeNode"/>.</summary>
    public abstract partial class SpokeControl : Control, ISpokeNode {

        readonly SpokeNodeCore core;

        protected SpokeControl()
            => core = new SpokeNodeCore(this, Init, IsVisibleInTree);

        public Node HostNode => this;

        /// <summary>True while the control is inside the SceneTree. Cycles on reparent.</summary>
        public ISignal<bool> IsInTree => core.IsInTree;

        /// <summary>
        /// True once _Ready has fired — this control and its children are set up. False during Init,
        /// which runs earlier, on entering the tree. Gate anything that has to see its own children
        /// initialised with s.Phase(IsReady, ...).
        /// </summary>
        public ISignal<bool> IsReady => core.IsReady;

        /// <summary>
        /// True while the control is visible in the tree — self and every ancestor. Named IsShown
        /// because IsVisible is already a method on the Godot side.
        /// </summary>
        public ISignal<bool> IsShown => core.IsShown;

        protected abstract void Init(EffectBuilder s);

        public sealed override void _Notification(int what) {
            base._Notification(what);
            core.Notification(what);
            OnNotification(what);
        }

        protected virtual void OnNotification(int what) { }

        protected void TeardownSpoke()
            => core.Teardown();
    }
}
