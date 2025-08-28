using UnityEngine;

namespace Spoke {

    /// <summary>
    /// The easiest way to use Spoke is to extend SpokeBehaviour instead of MonoBehaviour.
    /// It sets up a SpokeTree, and exposes lifecycle signals you can use to manage state.
    /// 
    /// A single method: Init(), is called during Awake to set up your effects.
    /// Override this method instead of Awake, OnEnable, Start, etc.
    /// 
    /// Works fine with [ExecuteAlways] behaviours
    /// </summary>
    public abstract class SpokeBehaviour : MonoBehaviour {

        State<bool> isAwake = State.Create(false);
        State<bool> isEnabled = State.Create(false);
        State<bool> isStarted = State.Create(false);

        /// <summary>True after Awake, False after Destroyed</summary>
        public ISignal<bool> IsAwake => isAwake;

        /// <summary>True while the behaviour is enabled</summary>
        public ISignal<bool> IsEnabled => isEnabled;

        /// <summary>True after Start has run</summary>
        public ISignal<bool> IsStarted => isStarted;

        SpokeHandle sceneTeardown, appTeardown;
        SpokeTree root;

        /// <summary>
        /// Override Init to set up your Spoke effects.
        /// It's an EffectBlock that will be hosted by the root Effect in the tree.
        /// </summary>
        protected abstract void Init(EffectBuilder s);

        protected virtual void Awake() {
            DoInit();
        }

        protected virtual void OnDestroy() {
            DoTeardown();
        }

        protected virtual void OnEnable() {
            if (root == null) DoInit(); // Domain reload may not run Awake
            isEnabled.Set(true);
        }

        protected virtual void OnDisable() {
            isEnabled.Set(false);
        }

        protected virtual void Start() {
            isStarted.Set(true);
        }

        void DoInit() {
            root = SpokeTree.Spawn($"{GetType().Name}:SpokeTree", new Effect("Init", Init), new UnitySpokeLogger(this));
            sceneTeardown = SpokeTeardown.Scene.Subscribe(scene => { if (scene == gameObject.scene) DoTeardown(); });
            appTeardown = SpokeTeardown.App.Subscribe(() => DoTeardown());
            isAwake.Set(true);
        }

        void DoTeardown() {
            sceneTeardown.Dispose();
            appTeardown.Dispose();
            // Avoid setting enabled=false for ExecuteAlways behaviours on domain reload. Because
            // it will be serialized as disabled on each reload.
            if (Application.isPlaying) enabled = false;
            isEnabled.Set(false);
            root?.Dispose();
            root = default;
            isAwake.Set(false);
        }
    }
}